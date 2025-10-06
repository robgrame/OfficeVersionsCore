using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace OfficeVersionsCore.Controllers
{
    /// <summary>
    /// Controller for generating and serving sitemap.xml dynamically
    /// </summary>
    [Route("[controller]")]
    [ApiController]
    public class SitemapController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;

        public SitemapController(IConfiguration configuration, IHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
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
    }
}