using OfficeVersionsCore.Services;
using Microsoft.AspNetCore.Mvc;

namespace OfficeVersionsCore.Controllers;

/// <summary>
/// API endpoint for Google Search Console data and SEO metrics
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SeoController : ControllerBase
{
    private readonly IGoogleSearchConsoleService _gscService;
    private readonly ILogger<SeoController> _logger;

    public SeoController(
        IGoogleSearchConsoleService gscService,
        ILogger<SeoController> logger)
    {
        _gscService = gscService;
        _logger = logger;
    }

    /// <summary>
    /// Get top performing queries from Google Search Console
    /// </summary>
    /// <param name="count">Number of top queries to return (default: 10)</param>
    /// <returns>List of top performing queries sorted by clicks</returns>
    [HttpGet("top-queries")]
    public async Task<IActionResult> GetTopQueries([FromQuery] int count = 10)
    {
        try
        {
            if (count < 1 || count > 100)
                count = 10;

            var queries = await _gscService.GetTopQueriesAsync(count);
            return Ok(new
            {
                success = true,
                count = queries.Count,
                data = queries
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving top queries");
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Get high-potential queries (low CTR, high impressions) for optimization
    /// </summary>
    /// <param name="count">Number of queries to return (default: 10)</param>
    /// <returns>List of high-potential optimization opportunities</returns>
    [HttpGet("high-potential-queries")]
    public async Task<IActionResult> GetHighPotentialQueries([FromQuery] int count = 10)
    {
        try
        {
            if (count < 1 || count > 100)
                count = 10;

            var queries = await _gscService.GetHighPotentialQueriesAsync(count);
            return Ok(new
            {
                success = true,
                count = queries.Count,
                data = queries
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving high-potential queries");
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Find a specific query and get its performance data
    /// </summary>
    /// <param name="query">The search query to find</param>
    /// <returns>Query performance data</returns>
    [HttpGet("query/{query}")]
    public async Task<IActionResult> FindQuery(string query)
    {
        try
        {
            var queryData = await _gscService.FindQueryAsync(query);

            if (queryData == null)
                return NotFound(new { success = false, error = "Query not found" });

            return Ok(new
            {
                success = true,
                data = queryData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error finding query: {query}");
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Get suggested meta tags for a query
    /// </summary>
    /// <param name="query">The search query</param>
    /// <returns>Suggested meta tags for SEO</returns>
    [HttpGet("meta-tags/{query}")]
    public async Task<IActionResult> GetMetaTags(string query)
    {
        try
        {
            var (title, description, keywords) = await _gscService.GetMetaTagsForQueryAsync(query);

            return Ok(new
            {
                success = true,
                data = new
                {
                    title,
                    description,
                    keywords
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error generating meta tags for query: {query}");
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Get cached Google Search Console data summary
    /// </summary>
    /// <returns>Summary statistics</returns>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        try
        {
            var data = await _gscService.GetCachedDataAsync();

            if (data == null)
                return NotFound(new { success = false, error = "GSC data not loaded" });

            return Ok(new
            {
                success = true,
                data = new
                {
                    totalQueries = data.AllQueries.Count,
                    totalClicks = data.TotalClicks,
                    totalImpressions = data.TotalImpressions,
                    averageCTR = Math.Round(data.AverageCTR, 2),
                    averagePosition = Math.Round(data.AveragePosition, 2),
                    zeroCTRQueries = data.ZeroCTRQueries.Count,
                    topQueries = data.GetTopQueries(5),
                    highPotentialQueries = data.GetHighPotentialQueries(5)
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating GSC summary");
            return BadRequest(new { success = false, error = ex.Message });
        }
    }
}
