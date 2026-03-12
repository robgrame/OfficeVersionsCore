using OfficeVersionsCore.Services;
using OfficeVersionsCore.Services.BackgroundTasks;
using OfficeVersionsCore.HealthChecks;
using OfficeVersionsCore.Infrastructure;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using System.Reflection;
using System.Threading.RateLimiting;
using Azure.Identity;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;

try
{
    var builder = WebApplication.CreateBuilder(args);

// Early logging to console for startup diagnostics
Console.WriteLine($"[STARTUP] Starting OfficeVersionsCore at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
Console.WriteLine($"[STARTUP] Environment: {builder.Environment.EnvironmentName}");
Console.WriteLine($"[STARTUP] Content Root: {builder.Environment.ContentRootPath}");
Console.WriteLine($"[STARTUP] Web Root: {builder.Environment.WebRootPath}");

// Add Application Insights - only if ConnectionString is configured
var aiConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(aiConnectionString))
{
    Console.WriteLine("[STARTUP] Application Insights: ENABLED");
    builder.Services.AddApplicationInsightsTelemetry(options =>
    {
        options.ConnectionString = aiConnectionString;
    });
}
else
{
    Console.WriteLine("[STARTUP] Application Insights: DISABLED (no connection string found)");
}

// Configure Serilog early with Application Insights support
builder.Host.UseSerilog((ctx, services, config) =>
{
    config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "OfficeVersionsCore")
        .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
        .Enrich.WithProperty("Version", "1.0.0");

    // On Azure App Service, write logs to D:\home\LogFiles\Application\ so they appear in Log Stream.
    // The HOME env var is always set by Azure App Service (e.g. D:\home).
    var azureHome = System.Environment.GetEnvironmentVariable("HOME");
    if (!string.IsNullOrEmpty(azureHome))
    {
        var azureLogPath = Path.Combine(azureHome, "LogFiles", "Application", "log-.log");
        config.WriteTo.File(
            azureLogPath,
            rollingInterval: Serilog.RollingInterval.Day,
            retainedFileCountLimit: 7,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj} <s:{SourceContext}>{NewLine}{Exception}");
        Console.WriteLine($"[STARTUP] Serilog file sink: {azureLogPath}");
    }

    // Add Application Insights sink if running in Azure (non-Development)
    // Only add if Application Insights is properly configured
    if (!ctx.HostingEnvironment.IsDevelopment())
    {
        try
        {
            var aiConfig = services.GetService<Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration>();
            if (aiConfig != null && !string.IsNullOrEmpty(aiConfig.ConnectionString))
            {
                config.WriteTo.ApplicationInsights(
                    aiConfig,
                    new TraceTelemetryConverter(),
                    LogEventLevel.Information);
            }
        }
        catch (Exception ex)
        {
            // Fallback: log to console if Application Insights fails
            Console.WriteLine($"Warning: Could not configure Application Insights logging: {ex.Message}");
        }
    }
});

    if (Uri.TryCreate(builder.Configuration["AppConfig"], UriKind.Absolute, out var endpoint))
    {
        // Use Azure Active Directory authentication.
        // The identity of this app should be assigned 'App Configuration Data Reader' or 'App Configuration Data Owner' role in App Configuration.
        // For more information, please visit https://aka.ms/vs/azure-app-configuration/concept-enable-rbac
        try
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                // Reduce timeout so startup doesn't hang for ~100s when identity is unavailable
                Retry = { NetworkTimeout = TimeSpan.FromSeconds(10) }
            });

            // Determine the label for this application instance.
            // Use APPCONFIG_LABEL env var if set, otherwise fall back to the environment name.
            var appConfigLabel = builder.Configuration["AppConfigLabel"]
                                 ?? builder.Environment.EnvironmentName;

            builder.Configuration.AddAzureAppConfiguration(options =>
            {
                options.Connect(endpoint, credential)
                // 1. Load keys with no label (shared/common settings)
                .Select(KeyFilter.Any, LabelFilter.Null)
                // 2. Load keys with the app-specific label (overrides shared ones)
                .Select(KeyFilter.Any, appConfigLabel)
                .ConfigureRefresh(refresh =>
                {
                    // All configuration values will be refreshed if the sentinel key changes.
                    refresh.Register("Office365Versions:Settings:Sentinel", "Office365Versions", refreshAll: true)
                           .SetRefreshInterval(TimeSpan.FromMinutes(5));
                })
                // Trim the app-specific prefix so keys map to standard config paths
                .TrimKeyPrefix("Office365Versions:");
            });
            builder.Services.AddAzureAppConfiguration();
            Console.WriteLine($"[STARTUP] Azure App Configuration: CONNECTED (label: {appConfigLabel})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STARTUP] Azure App Configuration: FAILED - {ex.Message}");
            Console.WriteLine("[STARTUP] Continuing without Azure App Configuration (using local settings)");
        }
    }

    // Add services to the container.
    builder.Services.AddRazorPages();
builder.Services.AddControllers()
    .AddXmlSerializerFormatters(); // Add XML support for Content Negotiation

// HttpClient factory (can extend later with proxy/timeout logic)
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("Office365Scraper", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Office365VersionScraper");
    client.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHttpClient("WindowsScraper", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "WindowsVersionsScraper");
    client.Timeout = TimeSpan.FromMinutes(2);
});

// Register Storage Service for local file system storage
// Must be Singleton because BackgroundServices (HostedServices) are Singleton and consume it
builder.Services.AddSingleton<IStorageService, LocalStorageService>();

// Add Distributed Caching - Using in-memory cache only (no Redis dependency)
// If you want to enable Redis in the future, you can use:
// builder.Services.AddStackExchangeRedisCache(options => { ... });
builder.Services.AddMemoryCache();

// Register cache service
builder.Services.AddScoped<ICacheService, DistributedCacheService>();

// Register Security Monitoring service (Singleton - tracks state across requests)
builder.Services.AddSingleton<ISecurityService, SecurityService>();

// Register Security Alert service (email + Telegram)
builder.Services.AddSingleton<ISecurityAlertService, SecurityAlertService>();

// Register the background service for periodic security analysis and alerting
if (builder.Configuration.GetValue<bool>("SecurityMonitoring:Enabled", true))
{
    builder.Services.AddHostedService<SecurityMonitoringBackgroundService>();
}

// Register Google Search Console service
builder.Services.AddScoped<IGoogleSearchConsoleService, GoogleSearchConsoleService>();

// Register GSC Initializer
builder.Services.AddScoped<GoogleSearchConsoleInitializer>();

// Register Office 365 service
builder.Services.AddScoped<IOffice365Service, Office365Service>();

// Register Windows versions service
builder.Services.AddScoped<IWindowsVersionsService, WindowsVersionsService>();

// Register Windows version mapper service
builder.Services.AddScoped<IWindowsVersionMapper, WindowsVersionMapper>();

// Register the background service for Office 365 version scraping
// Only register if enabled in configuration
if (builder.Configuration.GetValue<bool>("Office365Scraper:Enabled", false))
{
    builder.Services.AddHostedService<Office365VersionScraper>();
}

// Register the background service for Windows version scraping
// Only register if enabled in configuration
if (builder.Configuration.GetValue<bool>("WindowsScraper:Enabled", false))
{
    builder.Services.AddHostedService<WindowsVersionsScraper>();
}

// Add Swagger for API documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Generate swagger docs for v1
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Office & Windows Versions API",
        Version = "v1",
        Description = "API for Office 365 and Windows version tracking (v1)",
        Contact = new OpenApiContact
        {
            Name = "Office Versions Core",
            Url = new Uri("https://github.com/rgp-net/Office365Versions.com")
        }
    });

    // Include XML comments if available
    try
    {
        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            c.IncludeXmlComments(xmlPath);
        }
    }
    catch
    {
        // Ignore XML comment errors
    }

    // Set operation IDs based on controller and action name
    c.CustomOperationIds(apiDesc =>
    {
        return apiDesc.ActionDescriptor.RouteValues.TryGetValue("action", out var actionName) 
            ? actionName 
            : null;
    });

    // Enable annotations
    c.EnableAnnotations();
});

// Add CORS policy for API endpoints
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Add Response Compression (GZIP)
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});

// Add Rate Limiting for endpoints (.NET 10 built-in)
// API rate limiting is primarily handled by Azure API Management (APIM).
// In-app rate limiting is kept for Razor Pages and as a secondary defense layer.
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    // Rejection behavior when limit is exceeded
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    
    rateLimiterOptions.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        
        var retryAfterSeconds = 60;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            retryAfterSeconds = (int)retryAfter.TotalSeconds;
            context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        }
        
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Too Many Requests",
            message = "Rate limit exceeded. Please try again later.",
            retryAfter = retryAfterSeconds
        }, cancellationToken: cancellationToken);
    };

    // API Strict - Secondary defense for expensive endpoints (20 requests per minute)
    rateLimiterOptions.AddFixedWindowLimiter(policyName: "api-strict", options =>
    {
        options.PermitLimit = 20;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 2;
    });

    // Concurrency Limiter - For resource-intensive operations (max 10 concurrent)
    rateLimiterOptions.AddConcurrencyLimiter(policyName: "api-concurrent", options =>
    {
        options.PermitLimit = 10;
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 5;
    });

    // Razor Pages - Permissive (1000 requests per minute)
    rateLimiterOptions.AddFixedWindowLimiter(policyName: "pages", options =>
    {
        options.PermitLimit = 1000;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 20;
    });

    // Global per-IP rate limiting (200 requests per minute per IP)
    rateLimiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var ipAddress = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault() 
            ?? httpContext.Connection.RemoteIpAddress?.ToString() 
            ?? "unknown";
        
        return RateLimitPartition.GetFixedWindowLimiter(ipAddress, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 200,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 10
        });
    });
});

// Add Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<StorageHealthCheck>("storage")
    .AddCheck<DataFreshnessHealthCheck>("data-freshness");

var app = builder.Build();

Console.WriteLine("[STARTUP] Application built successfully");
Console.WriteLine($"[STARTUP] Is Development: {app.Environment.IsDevelopment()}");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    // HSTS disabled - Azure handles this at load balancer level
    // app.UseHsts();
}

// Configure custom error pages for specific status codes
app.UseStatusCodePagesWithReExecute("/Error{0}");

// Additional fallback for 500 errors
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.Redirect("/Error500");
        await Task.CompletedTask;
    });
});

// Enable Swagger
app.UseSwagger(c =>
{
    c.RouteTemplate = "swagger/{documentName}/swagger.json";
});

app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Office Versions API v1");
    c.RoutePrefix = "swagger";
    c.DocumentTitle = "Office Versions API";
    c.EnableDeepLinking();
    c.DisplayRequestDuration();
});

app.UseSerilogRequestLogging(options =>
{
    // Customize request logging for better visibility
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
});

// HTTPS Redirection: Only in Development (Azure handles HTTPS at load balancer)
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Add Security Headers Middleware
app.Use(async (context, next) =>
{
    // Strict-Transport-Security: Enforce HTTPS for 1 year
    context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
    
    // X-Content-Type-Options: Prevent MIME type sniffing
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    
    // X-Frame-Options: Prevent clickjacking
    context.Response.Headers["X-Frame-Options"] = "DENY";
    
    // X-XSS-Protection: Enable browser XSS protection
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    
    // Referrer-Policy: Control referrer information
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    
    // Permissions-Policy: Control browser features
    context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    
    await next();
});

// Apply Security Monitoring Middleware (IP blocking + suspicious request detection)
// Must run early, before routing and static files
if (builder.Configuration.GetValue<bool>("SecurityMonitoring:Enabled", true))
{
    app.UseMiddleware<SecurityMiddleware>();
}

// Apply API Gateway enforcement middleware
// Blocks direct access to /api/* endpoints unless the request comes through APIM
if (builder.Configuration.GetValue<bool>("ApiManagement:Enabled", false))
{
    app.UseMiddleware<ApiGatewayMiddleware>();
}

// Enable Response Compression
app.UseResponseCompression();

// Apply Rate Limiting middleware
app.UseRateLimiter();

// Setup URL rewrite rules for SEO optimization
var rewriteOptions = new RewriteOptions();
// Redirect /sitemap.xml to the SitemapController
rewriteOptions.AddRedirect("^sitemap\\.xml$", "sitemap", statusCode: 301);
// Redirect /robots.txt is served statically from wwwroot (no rewrite needed)
app.UseRewriter(rewriteOptions);

app.UseStaticFiles();

app.UseCors();
// Only use App Configuration middleware if it was successfully registered during startup
try
{
    if (Uri.TryCreate(builder.Configuration["AppConfig"], UriKind.Absolute, out _))
    {
        app.UseAzureAppConfiguration();
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[STARTUP] UseAzureAppConfiguration skipped: {ex.Message}");
}
app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

// Map sitemap.xml directly at root for better SEO discoverability
app.MapGet("/sitemap.xml", async (HttpContext context) =>
{
    context.Response.Redirect("/sitemap", permanent: true);
}).ExcludeFromDescription(); // Exclude from Swagger

// Map Health Check endpoint
app.MapHealthChecks("/health");

// Initialize Google Search Console data
using (var scope = app.Services.CreateScope())
{
    try
    {
        Console.WriteLine("[STARTUP] Initializing Google Search Console data...");
        var gscInitializer = scope.ServiceProvider.GetService<GoogleSearchConsoleInitializer>();
        if (gscInitializer != null)
        {
            await gscInitializer.InitializeAsync();
            app.Logger.LogInformation("Google Search Console data initialized successfully");
            Console.WriteLine("[STARTUP] GSC initialization: SUCCESS");
        }
        else
        {
            app.Logger.LogWarning("GoogleSearchConsoleInitializer service not registered");
            Console.WriteLine("[STARTUP] GSC initialization: SKIPPED (service not registered)");
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error initializing Google Search Console data on startup - continuing anyway");
        Console.WriteLine($"[STARTUP] GSC initialization: FAILED - {ex.Message}");
        Console.WriteLine($"[STARTUP] GSC Stack Trace: {ex.StackTrace}");
        // Don't fail startup if GSC initialization fails
    }
}

Console.WriteLine("[STARTUP] Starting web server...");
app.Run();
Console.WriteLine("[STARTUP] Application stopped");
}
catch (Exception ex)
{
    Console.WriteLine("[FATAL] Application failed to start!");
    Console.WriteLine($"[FATAL] Exception Type: {ex.GetType().Name}");
    Console.WriteLine($"[FATAL] Message: {ex.Message}");
    Console.WriteLine($"[FATAL] Stack Trace:\n{ex.StackTrace}");
    
    if (ex.InnerException != null)
    {
        Console.WriteLine($"[FATAL] Inner Exception: {ex.InnerException.Message}");
        Console.WriteLine($"[FATAL] Inner Stack Trace:\n{ex.InnerException.StackTrace}");
    }
    
    // Log to Application Insights if available
    try
    {
        Log.Fatal(ex, "Application failed to start");
    }
    catch
    {
        // Ignore logging failures during fatal error handling
    }
    
    throw; // Re-throw to ensure Azure logs the error
}
