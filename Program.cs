using OfficeVersionsCore.Services;
using OfficeVersionsCore.Services.BackgroundTasks;
using Serilog;
using Azure.Identity;
using Azure.Storage.Blobs;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using Microsoft.Extensions.Azure;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.OpenApi.Models;
using System.Reflection;

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
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} <s:{SourceContext}>{NewLine}{Exception}",
            theme: AnsiConsoleTheme.Code);

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

// Add Azure clients with configuration based on environment
builder.Services.AddAzureClients(clientBuilder =>
{
    // Check if we should use Azurite for local development
    var useAzurite = builder.Environment.IsDevelopment() && 
                     builder.Configuration.GetValue<bool>("AzuriteStorage:UseAzurite", false);
    
    if (useAzurite)
    {
        // Use Azurite for local development
        var azuriteConnectionString = builder.Configuration["AzuriteStorage:ConnectionString"] ?? "UseDevelopmentStorage=true";
        clientBuilder.AddBlobServiceClient(azuriteConnectionString);
        
        // Log that we're using Azurite
        Console.WriteLine("Using Azurite for local storage development");
    }
    else
    {
        // Use managed identity for Azure environments
        var storageAccountName = builder.Configuration["StorageAccountName"] ?? "officeversionscorestrg";
        var blobUri = new Uri($"https://{storageAccountName}.blob.core.windows.net");
        
        // Add BlobServiceClient using DefaultAzureCredential
        clientBuilder.AddBlobServiceClient(blobUri);
        clientBuilder.UseCredential(new DefaultAzureCredential());
    }
});

// HttpClient factory (can extend later with proxy/timeout logic)
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("Office365Scraper", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Office365VersionScraper");
    client.Timeout = TimeSpan.FromMinutes(2);
});

// Register Office 365 service
builder.Services.AddScoped<IOffice365Service, Office365Service>();

// Register the background service for Office 365 version scraping
// Only register if enabled in configuration
if (builder.Configuration.GetValue<bool>("Office365Scraper:Enabled", false))
{
    builder.Services.AddHostedService<Office365VersionScraper>();
}

// Add storage container initializer service
builder.Services.AddHostedService<StorageContainerInitializer>();

// Add Swagger for API documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Office Versions API",
        Version = "v1",
        Description = "API for Office 365 version tracking",
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

// Setup URL rewrite rules for SEO optimization
var options = new RewriteOptions();
// Redirect /sitemap.xml to the SitemapController
options.AddRedirect("^sitemap\\.xml$", "sitemap");
app.UseRewriter(options);

app.UseStaticFiles();

app.UseCors();
app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

app.Run();
