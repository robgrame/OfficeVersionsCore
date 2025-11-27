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

var builder = WebApplication.CreateBuilder(args);

// Add Application Insights
builder.Services.AddApplicationInsightsTelemetry();

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

    // Add Application Insights sink if running in Azure (non-Development)
    if (!ctx.HostingEnvironment.IsDevelopment())
    {
        // This will use the Application Insights instrumentation key from configuration
        config.WriteTo.ApplicationInsights(
            services.GetRequiredService<Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration>(),
            new TraceTelemetryConverter(),
            LogEventLevel.Information);
    }
});

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers();

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

// Register Google Search Console service
builder.Services.AddScoped<IGoogleSearchConsoleService, GoogleSearchConsoleService>();

// Register GSC Initializer
builder.Services.AddScoped<GoogleSearchConsoleInitializer>();

// Register Office 365 service
builder.Services.AddScoped<IOffice365Service, Office365Service>();

// Register Windows versions service
builder.Services.AddScoped<IWindowsVersionsService, WindowsVersionsService>();

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
            Url = new Uri("https://github.com/robgrame/OfficeVersionsCore")
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

// Add Rate Limiting for API endpoints (.NET 10 built-in)
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.AddFixedWindowLimiter(policyName: "fixed", options =>
    {
        options.PermitLimit = 100;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 10;
    });
});

// Add Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<StorageHealthCheck>("storage")
    .AddCheck<DataFreshnessHealthCheck>("data-freshness");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

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

app.UseHttpsRedirection();

// Add Security Headers Middleware
app.Use(async (context, next) =>
{
    // Strict-Transport-Security: Enforce HTTPS for 1 year
    context.Response.Headers.Add("Strict-Transport-Security", "max-age=31536000; includeSubDomains; preload");
    
    // X-Content-Type-Options: Prevent MIME type sniffing
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    
    // X-Frame-Options: Prevent clickjacking
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    
    // X-XSS-Protection: Enable browser XSS protection
    context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    
    // Referrer-Policy: Control referrer information
    context.Response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");
    
    // Permissions-Policy: Control browser features
    context.Response.Headers.Add("Permissions-Policy", "geolocation=(), microphone=(), camera=()");
    
    await next();
});

// Enable Response Compression
app.UseResponseCompression();

// Apply Rate Limiting middleware
app.UseRateLimiter();

// Setup URL rewrite rules for SEO optimization
var rewriteOptions = new RewriteOptions();
// Redirect /sitemap.xml to the SitemapController
rewriteOptions.AddRedirect("^sitemap\\.xml$", "sitemap");
app.UseRewriter(rewriteOptions);

app.UseStaticFiles();

app.UseCors();
app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

// Map Health Check endpoint
app.MapHealthChecks("/health");

// Initialize Google Search Console data
try
{
    var gscInitializer = app.Services.CreateScope().ServiceProvider.GetRequiredService<GoogleSearchConsoleInitializer>();
    await gscInitializer.InitializeAsync();
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Error initializing Google Search Console data on startup");
    // Don't fail startup if GSC initialization fails
}

app.Run();
