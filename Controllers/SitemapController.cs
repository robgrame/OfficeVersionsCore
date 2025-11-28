using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using OfficeVersionsCore.Services;

namespace OfficeVersionsCore.Controllers
{
    /// <summary>
    /// Controller for generating and serving sitemap.xml dynamically
    /// Optimized with Google Search Console data for better ranking prioritization
    /// </summary>
    [Route("[controller]")]
    [ApiController]
    [EnableRateLimiting("api")]  // Standard rate limiting for sitemap
    public class SitemapController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;
        private readonly IGoogleSearchConsoleService _gscService;
        private readonly ILogger<SitemapController> _logger;

        public SitemapController(
            IConfiguration configuration, 
            IHostEnvironment environment,
            IGoogleSearchConsoleService gscService,
            ILogger<SitemapController> logger)
        {
            _configuration = configuration;
            _environment = environment;
            _gscService = gscService;
            _logger = logger;
        }

        /// <summary>
        /// Generates a sitemap.xml file dynamically
        /// </summary>
        /// <returns>XML sitemap content</returns>
        [HttpGet]
        [Route("")]
        [Route("xml")]
        public ContentResult GetSitemap()
        {
            try
            {
                // Get base URL from configuration, with fallback to Request.Scheme + Host
                string baseUrl = _configuration["SiteUrl"] ?? $"{Request.Scheme}://{Request.Host}";
                
                // Ensure baseUrl doesn't end with a slash
                if (baseUrl.EndsWith("/"))
                    baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);

                // Define XML namespaces
                XNamespace xmlns = "http://www.sitemaps.org/schemas/sitemap/0.9";
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

                // Create sitemap root element with proper namespace handling
                var sitemap = new XElement(xmlns + "urlset",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi.NamespaceName),
                    new XAttribute(xsi + "schemaLocation", "http://www.sitemaps.org/schemas/sitemap/0.9 http://www.sitemaps.org/schemas/sitemap/0.9/sitemap.xsd"));

                // Homepage - highest priority, daily updates
                AddUrlToSitemap(sitemap, baseUrl, "/", changeFreq: "daily", priority: "1.0");
                
                // Office 365 Channel Pages - high priority, frequent updates
                AddUrlToSitemap(sitemap, baseUrl, "/Current", changeFreq: "daily", priority: "0.9");
                AddUrlToSitemap(sitemap, baseUrl, "/Monthly", changeFreq: "daily", priority: "0.9");
                AddUrlToSitemap(sitemap, baseUrl, "/SemiAnnual", changeFreq: "weekly", priority: "0.8");
                AddUrlToSitemap(sitemap, baseUrl, "/SemiAnnualPreview", changeFreq: "weekly", priority: "0.8");
                
                // Office 365 Overview Pages
                AddUrlToSitemap(sitemap, baseUrl, "/AllChannels", changeFreq: "daily", priority: "0.7");
                AddUrlToSitemap(sitemap, baseUrl, "/AllReleases", changeFreq: "daily", priority: "0.7");
                
                // Windows Pages - high priority for Windows users
                AddUrlToSitemap(sitemap, baseUrl, "/Windows/Index", changeFreq: "daily", priority: "0.85");
                AddUrlToSitemap(sitemap, baseUrl, "/Windows/Releases", changeFreq: "daily", priority: "0.8");
                AddUrlToSitemap(sitemap, baseUrl, "/Windows/Releases11", changeFreq: "daily", priority: "0.85");
                AddUrlToSitemap(sitemap, baseUrl, "/Windows/Releases10", changeFreq: "daily", priority: "0.8");
                
                // Informational Pages
                AddUrlToSitemap(sitemap, baseUrl, "/About", changeFreq: "monthly", priority: "0.5");
                AddUrlToSitemap(sitemap, baseUrl, "/Contact", changeFreq: "monthly", priority: "0.5");
                AddUrlToSitemap(sitemap, baseUrl, "/Privacy", changeFreq: "yearly", priority: "0.3");
                
                // Development-only pages
                if (!_environment.IsProduction())
                {
                    AddUrlToSitemap(sitemap, baseUrl, "/CookieTest", changeFreq: "never", priority: "0.1");
                }
                
                // API Documentation (Swagger)
                AddUrlToSitemap(sitemap, baseUrl, "/swagger", changeFreq: "monthly", priority: "0.6");

                _logger.LogInformation($"Generated sitemap with {sitemap.Elements("url").Count()} URLs");

                // Create XML document with declaration
                var doc = new XDocument(
                    new XDeclaration("1.0", "UTF-8", null),
                    sitemap);

                // Set X-Robots-Tag header
                Response.Headers.Append("X-Robots-Tag", "all");

                // Convert to string with declaration
                var xmlString = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n{sitemap.ToString()}";

                // Return XML content with proper content type
                return new ContentResult
                {
                    Content = xmlString,
                    ContentType = "application/xml",
                    StatusCode = 200
                };
            }
            catch (Exception ex)
            {
                // Log the error with full exception details
                _logger.LogError(ex, "Error generating sitemap: {ErrorMessage}. StackTrace: {StackTrace}", 
                    ex.Message, ex.StackTrace);
                
                // Return 500 error as ContentResult
                return new ContentResult
                {
                    Content = $"Error generating sitemap: {ex.Message}",
                    ContentType = "text/plain",
                    StatusCode = 500
                };
            }
        }

        private void AddUrlToSitemap(XElement sitemap, string baseUrl, string path, string changeFreq = "monthly", string priority = "0.5")
        {
            var fullUrl = $"{baseUrl}{path}";
            
            // Get the namespace from the parent sitemap element
            XNamespace xmlns = sitemap.Name.Namespace;
            
            sitemap.Add(
                new XElement(xmlns + "url",
                    new XElement(xmlns + "loc", fullUrl),
                    new XElement(xmlns + "lastmod", DateTime.UtcNow.ToString("yyyy-MM-dd")),
                    new XElement(xmlns + "changefreq", changeFreq),
                    new XElement(xmlns + "priority", priority)
                )
            );
        }

        /// <summary>
        /// Calculate sitemap priority based on Google Search Console data
        /// Higher clicks and lower position = higher priority
        /// </summary>
        private async Task<string> CalculatePriorityFromGscAsync(string pageName)
        {
            try
            {
                var gscData = await _gscService.GetCachedDataAsync();
                if (gscData == null)
                    return "0.5"; // Default priority

                // Find queries related to the page name
                var relevantQueries = gscData.AllQueries
                    .Where(q => q.Query.ToLower().Contains(pageName.ToLower()))
                    .ToList();

                if (!relevantQueries.Any())
                    return "0.5"; // Default if no relevant queries

                // Calculate priority based on:
                // - Average clicks (higher = higher priority)
                // - Average position (lower = higher priority)
                var avgClicks = relevantQueries.Average(q => q.Clicks);
                var avgPosition = (double)relevantQueries.Average(q => q.Position);

                // Priority formula: (clicks / 100) * (10 / position)
                // Normalized to 0.1-1.0
                var priority = Math.Min(1.0, Math.Max(0.1, (avgClicks / 100) * (10 / avgPosition)));
                
                _logger.LogInformation($"Calculated priority for '{pageName}': {priority:F2}");
                return Math.Round(priority, 2).ToString("F2");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Error calculating priority from GSC for '{pageName}'");
                return "0.5"; // Default priority on error
            }
        }
    }
}