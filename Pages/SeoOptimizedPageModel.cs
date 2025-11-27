using Microsoft.AspNetCore.Mvc.RazorPages;
using OfficeVersionsCore.Services;

namespace OfficeVersionsCore.Pages;

/// <summary>
/// Base class for Razor Pages that want to use SEO optimizations from Google Search Console
/// </summary>
public class SeoOptimizedPageModel : PageModel
{
    protected readonly IGoogleSearchConsoleService GscService;
    protected readonly ILogger<SeoOptimizedPageModel> Logger;

    public SeoOptimizedPageModel(
        IGoogleSearchConsoleService gscService,
        ILogger<SeoOptimizedPageModel> logger)
    {
        GscService = gscService;
        Logger = logger;
    }

    /// <summary>
    /// Set SEO meta tags based on a specific Google Search Console query
    /// Call this in your OnGet method to optimize for a particular search term
    /// </summary>
    protected async Task SetSeoMetaTagsForQueryAsync(string query, string? canonicalUrl = null)
    {
        try
        {
            var (title, description, keywords) = await GscService.GetMetaTagsForQueryAsync(query);
            
            ViewData["SeoTitle"] = title;
            ViewData["SeoDescription"] = description;
            ViewData["SeoKeywords"] = keywords;
            
            if (string.IsNullOrEmpty(canonicalUrl))
            {
                canonicalUrl = $"https://{Request.Host}{Request.Path}{Request.QueryString}";
            }
            ViewData["CanonicalUrl"] = canonicalUrl;
            
            Logger.LogInformation($"SEO meta tags set for query: {query}");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, $"Failed to set SEO meta tags for query: {query}");
            // Don't fail page load if SEO fails
        }
    }

    /// <summary>
    /// Set SEO meta tags manually (for custom content)
    /// </summary>
    protected void SetSeoMetaTags(string title, string description, string keywords, string? canonicalUrl = null)
    {
        ViewData["SeoTitle"] = title;
        ViewData["SeoDescription"] = description;
        ViewData["SeoKeywords"] = keywords;
        
        if (string.IsNullOrEmpty(canonicalUrl))
        {
            canonicalUrl = $"https://{Request.Host}{Request.Path}{Request.QueryString}";
        }
        ViewData["CanonicalUrl"] = canonicalUrl;
    }

    /// <summary>
    /// Get top performing queries for featured content
    /// </summary>
    protected async Task<List<Models.QueryPerformance>> GetTopQueriesAsync(int count = 10)
    {
        try
        {
            return await GscService.GetTopQueriesAsync(count);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to retrieve top queries");
            return [];
        }
    }

    /// <summary>
    /// Get high-potential queries for optimization opportunities
    /// </summary>
    protected async Task<List<Models.QueryPerformance>> GetHighPotentialQueriesAsync(int count = 10)
    {
        try
        {
            return await GscService.GetHighPotentialQueriesAsync(count);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to retrieve high-potential queries");
            return [];
        }
    }
}
