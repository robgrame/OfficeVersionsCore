using Microsoft.AspNetCore.Mvc;
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
        [Produces("application/xml")]
        public IActionResult GetSitemap()
        {
            try
            {
                // Get base URL from configuration, with fallback to Request.Scheme + Host
                string baseUrl = _configuration["SiteUrl"] ?? $"{Request.Scheme}://{Request.Host}";
                
                // Ensure baseUrl doesn't end with a slash
                if (baseUrl.EndsWith("/"))
                    baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);

                // Create sitemap root element
                var sitemap = new XElement("urlset",
                    new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                    new XAttribute(XNamespace.Xmlns + "xhtml", "http://www.w3.org/1999/xhtml"),
                    new XAttribute(XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance") + "schemaLocation", "http://www.sitemaps.org/schemas/sitemap/0.9 http://www.sitemaps.org/schemas/sitemap/0.9/sitemap.xsd"),
                    new XAttribute("xmlns", "http://www.sitemaps.org/schemas/sitemap/0.9"));

                // Add core pages
                AddUrlToSitemap(sitemap, baseUrl, "/", changeFreq: "daily", priority: "1.0");
                AddUrlToSitemap(sitemap, baseUrl, "/Current", changeFreq: "daily", priority: "0.9");
                AddUrlToSitemap(sitemap, baseUrl, "/Monthly", changeFreq: "daily", priority: "0.9");
                AddUrlToSitemap(sitemap, baseUrl, "/SemiAnnual", changeFreq: "weekly", priority: "0.8");
                AddUrlToSitemap(sitemap, baseUrl, "/SemiAnnualPreview", changeFreq: "weekly", priority: "0.8");
                AddUrlToSitemap(sitemap, baseUrl, "/AllChannels", changeFreq: "daily", priority: "0.7");
                AddUrlToSitemap(sitemap, baseUrl, "/AllReleases", changeFreq: "daily", priority: "0.7");
                AddUrlToSitemap(sitemap, baseUrl, "/About", changeFreq: "monthly", priority: "0.5");
                AddUrlToSitemap(sitemap, baseUrl, "/Contact", changeFreq: "monthly", priority: "0.5");
                AddUrlToSitemap(sitemap, baseUrl, "/Privacy", changeFreq: "yearly", priority: "0.3");

                // Create XML document with declaration
                var doc = new XDocument(
                    new XDeclaration("1.0", "UTF-8", null),
                    sitemap);

                // Return XML content
                return Content(doc.ToString(), "application/xml");
            }
            catch (Exception ex)
            {
                // Log the error
                Console.Error.WriteLine($"Error generating sitemap: {ex.Message}");
                
                // Return 500 error
                return StatusCode(500, "Error generating sitemap");
            }
        }

        private void AddUrlToSitemap(XElement sitemap, string baseUrl, string path, string changeFreq = "monthly", string priority = "0.5")
        {
            var fullUrl = $"{baseUrl}{path}";
            
            sitemap.Add(
                new XElement("url",
                    new XElement("loc", fullUrl),
                    new XElement("lastmod", DateTime.UtcNow.ToString("yyyy-MM-dd")),
                    new XElement("changefreq", changeFreq),
                    new XElement("priority", priority)
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