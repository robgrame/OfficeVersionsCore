using OfficeVersionsCore.Services;
using Microsoft.Extensions.Logging;

namespace OfficeVersionsCore.Infrastructure;

/// <summary>
/// Initializes Google Search Console data on application startup
/// </summary>
public class GoogleSearchConsoleInitializer
{
    private readonly ILogger<GoogleSearchConsoleInitializer> _logger;
    private readonly IGoogleSearchConsoleService _gscService;
    private readonly IWebHostEnvironment _env;

    public GoogleSearchConsoleInitializer(
        ILogger<GoogleSearchConsoleInitializer> logger,
        IGoogleSearchConsoleService gscService,
        IWebHostEnvironment env)
    {
        _logger = logger;
        _gscService = gscService;
        _env = env;
    }

    /// <summary>
    /// Initialize GSC data from CSV file
    /// Looks for: wwwroot/data/gsc-export.csv
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            var gscFilePath = Path.Combine(_env.WebRootPath, "data", "gsc-export.csv");

            if (!File.Exists(gscFilePath))
            {
                _logger.LogWarning($"GSC export file not found at {gscFilePath}. SEO optimizations will not be available.");
                return;
            }

            _logger.LogInformation($"Loading GSC data from {gscFilePath}...");
            await _gscService.LoadFromFileAsync(gscFilePath);
            _logger.LogInformation("GSC data loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing GSC data");
            // Don't throw - allow application to continue without GSC data
        }
    }
}
