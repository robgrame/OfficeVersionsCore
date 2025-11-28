using Microsoft.AspNetCore.Mvc;
using OfficeVersionsCore.Services;
using System.Reflection;

namespace OfficeVersionsCore.Controllers
{
    /// <summary>
    /// Diagnostics endpoint for Azure troubleshooting
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [ApiExplorerSettings(IgnoreApi = true)] // Exclude from Swagger
    public class DiagnosticsController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<DiagnosticsController> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly IStorageService _storageService;

        public DiagnosticsController(
            IConfiguration configuration,
            ILogger<DiagnosticsController> logger,
            IWebHostEnvironment env,
            IStorageService storageService)
        {
            _configuration = configuration;
            _logger = logger;
            _env = env;
            _storageService = storageService;
        }

        /// <summary>
        /// Health check endpoint with detailed diagnostics
        /// </summary>
        [HttpGet("health")]
        public async Task<ActionResult<object>> HealthCheck()
        {
            try
            {
                var diagnostics = new
                {
                    Status = "OK",
                    Timestamp = DateTime.UtcNow,
                    Environment = _env.EnvironmentName,
                    Framework = Assembly.GetEntryAssembly()?.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName,
                    ContentRootPath = _env.ContentRootPath,
                    WebRootPath = _env.WebRootPath,
                    Configuration = new
                    {
                        Office365StoragePath = _configuration["Office365:StoragePath"],
                        WindowsVersionsStoragePath = _configuration["WindowsVersions:StoragePath"],
                        Office365ScraperEnabled = _configuration.GetValue<bool>("Office365Scraper:Enabled"),
                        WindowsScraperEnabled = _configuration.GetValue<bool>("WindowsScraper:Enabled"),
                        HasApplicationInsights = !string.IsNullOrEmpty(_configuration["ApplicationInsights:ConnectionString"])
                    },
                    Storage = await CheckStorageAccessAsync(),
                    Files = await CheckRequiredFilesAsync()
                };

                return Ok(diagnostics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in diagnostics endpoint");
                return StatusCode(500, new { 
                    Status = "ERROR", 
                    Message = ex.Message,
                    StackTrace = ex.StackTrace 
                });
            }
        }

        /// <summary>
        /// Endpoint to check environment variables (SAFE - no secrets exposed)
        /// </summary>
        [HttpGet("env")]
        public ActionResult<object> EnvironmentInfo()
        {
            return Ok(new
            {
                MachineName = Environment.MachineName,
                OSVersion = Environment.OSVersion.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
                Is64BitProcess = Environment.Is64BitProcess,
                RuntimeVersion = Environment.Version.ToString(),
                CurrentDirectory = Environment.CurrentDirectory,
                AspNetCoreEnvironment = _env.EnvironmentName,
                // Safe variables only
                Variables = new
                {
                    ASPNETCORE_ENVIRONMENT = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                    DOTNET_ENVIRONMENT = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
                    WEBSITE_SITE_NAME = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"),
                    WEBSITE_INSTANCE_ID = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID")
                }
            });
        }

        private async Task<object> CheckStorageAccessAsync()
        {
            try
            {
                var testPath = "diagnostics/test.txt";
                var testContent = $"Test write at {DateTime.UtcNow}";
                
                await _storageService.WriteAsync(testPath, testContent);
                var readContent = await _storageService.ReadAsync(testPath);
                var exists = await _storageService.ExistsAsync(testPath);
                
                // Cleanup
                await _storageService.DeleteAsync(testPath);
                
                return new
                {
                    CanWrite = true,
                    CanRead = readContent == testContent,
                    CanDelete = true,
                    Message = "Storage access is working correctly"
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    CanWrite = false,
                    CanRead = false,
                    CanDelete = false,
                    Error = ex.Message
                };
            }
        }

        private async Task<object> CheckRequiredFilesAsync()
        {
            var requiredFiles = new[]
            {
                "wwwroot/data/gsc-export.csv",
                "appsettings.json"
            };

            var fileStatus = new Dictionary<string, object>();

            foreach (var file in requiredFiles)
            {
                var fullPath = Path.Combine(_env.ContentRootPath, file);
                var exists = System.IO.File.Exists(fullPath);
                
                fileStatus[file] = new
                {
                    Exists = exists,
                    FullPath = fullPath,
                    Size = exists ? new FileInfo(fullPath).Length : 0
                };
            }

            // Check storage files
            var storageFiles = new[]
            {
                $"{_configuration["Office365:StoragePath"]}/m365LatestVersions.json",
                $"{_configuration["WindowsVersions:StoragePath"]}/windowsLatestVersions.json"
            };

            foreach (var file in storageFiles)
            {
                var exists = await _storageService.ExistsAsync(file);
                fileStatus[file] = new
                {
                    Exists = exists,
                    Location = "Storage"
                };
            }

            return fileStatus;
        }

        /// <summary>
        /// Force crash endpoint for testing error handling (Development only)
        /// </summary>
        [HttpGet("crash")]
        public ActionResult ForceCrash()
        {
            if (!_env.IsDevelopment())
            {
                return Forbid();
            }

            throw new InvalidOperationException("Forced crash for testing error handling");
        }
    }
}
