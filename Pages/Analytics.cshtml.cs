using Microsoft.AspNetCore.Mvc.RazorPages;
using OfficeVersionsCore.Services;
using OfficeVersionsCore.Models;

namespace OfficeVersionsCore.Pages;

/// <summary>
/// Page model for displaying Google Search Console analytics dashboard
/// </summary>
public class AnalyticsModel : PageModel
{
    private readonly IGoogleSearchConsoleService _gscService;
    private readonly ILogger<AnalyticsModel> _logger;

    public AnalyticsModel(IGoogleSearchConsoleService gscService, ILogger<AnalyticsModel> logger)
    {
        _gscService = gscService;
        _logger = logger;
    }

    public GoogleSearchConsoleData? GscData { get; set; }
    public List<QueryPerformance> TopQueries { get; set; } = [];
    public List<QueryPerformance> HighPotentialQueries { get; set; } = [];
    public int ZeroCtrQueriesCount { get; set; }
    public decimal AverageCtr { get; set; }
    public decimal AveragePosition { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            GscData = await _gscService.GetCachedDataAsync();

            if (GscData == null)
            {
                _logger.LogWarning("GSC data not available");
                return;
            }

            TopQueries = GscData.GetTopQueries(10);
            HighPotentialQueries = GscData.GetHighPotentialQueries(10);
            ZeroCtrQueriesCount = GscData.ZeroCTRQueries.Count;
            AverageCtr = Math.Round(GscData.AverageCTR, 2);
            AveragePosition = Math.Round(GscData.AveragePosition, 2);

            _logger.LogInformation($"Analytics page loaded with {GscData.AllQueries.Count} queries");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading GSC analytics data");
        }
    }
}
