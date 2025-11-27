using OfficeVersionsCore.Models;

namespace OfficeVersionsCore.Infrastructure;

/// <summary>
/// Helper methods for SEO meta tag generation
/// </summary>
public static class SeoMetaTagsHelper
{
    /// <summary>
    /// Generate a complete set of meta tags for a page
    /// </summary>
    public static string GenerateMetaTags(
        string title,
        string description,
        string? keywords = null,
        string? canonicalUrl = null,
        string? ogImage = null)
    {
        var tags = new System.Text.StringBuilder();

        // Title tag
        tags.AppendLine($"<title>{EscapeHtml(title)} - Office & Windows Versions</title>");

        // Meta description
        tags.AppendLine($"<meta name=\"description\" content=\"{EscapeHtml(description)}\" />");

        // Meta keywords (if provided)
        if (!string.IsNullOrEmpty(keywords))
        {
            tags.AppendLine($"<meta name=\"keywords\" content=\"{EscapeHtml(keywords)}\" />");
        }

        // Canonical URL (if provided)
        if (!string.IsNullOrEmpty(canonicalUrl))
        {
            tags.AppendLine($"<link rel=\"canonical\" href=\"{EscapeHtml(canonicalUrl)}\" />");
        }

        // Open Graph tags for social sharing
        tags.AppendLine($"<meta property=\"og:title\" content=\"{EscapeHtml(title)}\" />");
        tags.AppendLine($"<meta property=\"og:description\" content=\"{EscapeHtml(description)}\" />");
        tags.AppendLine($"<meta property=\"og:type\" content=\"website\" />");

        if (!string.IsNullOrEmpty(ogImage))
        {
            tags.AppendLine($"<meta property=\"og:image\" content=\"{EscapeHtml(ogImage)}\" />");
        }

        // Twitter Card tags
        tags.AppendLine($"<meta name=\"twitter:card\" content=\"summary\" />");
        tags.AppendLine($"<meta name=\"twitter:title\" content=\"{EscapeHtml(title)}\" />");
        tags.AppendLine($"<meta name=\"twitter:description\" content=\"{EscapeHtml(description)}\" />");

        return tags.ToString();
    }

    /// <summary>
    /// Generate schema.org markup for software/application
    /// </summary>
    public static string GenerateSoftwareApplicationSchema(
        string name,
        string description,
        string? url = null,
        string? imageUrl = null)
    {
        var json = new System.Text.StringBuilder();
        json.AppendLine("<script type=\"application/ld+json\">");
        json.AppendLine("{");
        json.AppendLine("  \"@context\": \"https://schema.org\",");
        json.AppendLine("  \"@type\": \"SoftwareApplication\",");
        json.AppendLine($"  \"name\": \"{EscapeJson(name)}\",");
        json.AppendLine($"  \"description\": \"{EscapeJson(description)}\",");
        json.AppendLine("  \"applicationCategory\": \"BusinessApplication\",");

        if (!string.IsNullOrEmpty(url))
        {
            json.AppendLine($"  \"url\": \"{EscapeJson(url)}\",");
        }

        if (!string.IsNullOrEmpty(imageUrl))
        {
            json.AppendLine($"  \"image\": \"{EscapeJson(imageUrl)}\",");
        }

        json.AppendLine("  \"offers\": {");
        json.AppendLine("    \"@type\": \"Offer\",");
        json.AppendLine("    \"price\": \"0\",");
        json.AppendLine("    \"priceCurrency\": \"USD\"");
        json.AppendLine("  }");
        json.AppendLine("}");
        json.AppendLine("</script>");

        return json.ToString();
    }

    /// <summary>
    /// Generate schema.org FAQPage markup
    /// </summary>
    public static string GenerateFAQSchema(List<(string question, string answer)> faqItems)
    {
        if (!faqItems.Any())
            return string.Empty;

        var json = new System.Text.StringBuilder();
        json.AppendLine("<script type=\"application/ld+json\">");
        json.AppendLine("{");
        json.AppendLine("  \"@context\": \"https://schema.org\",");
        json.AppendLine("  \"@type\": \"FAQPage\",");
        json.AppendLine("  \"mainEntity\": [");

        for (int i = 0; i < faqItems.Count; i++)
        {
            json.AppendLine("    {");
            json.AppendLine("      \"@type\": \"Question\",");
            json.AppendLine($"      \"name\": \"{EscapeJson(faqItems[i].question)}\",");
            json.AppendLine("      \"acceptedAnswer\": {");
            json.AppendLine("        \"@type\": \"Answer\",");
            json.AppendLine($"        \"text\": \"{EscapeJson(faqItems[i].answer)}\"");
            json.AppendLine("      }");
            json.AppendLine("    }" + (i < faqItems.Count - 1 ? "," : ""));
        }

        json.AppendLine("  ]");
        json.AppendLine("}");
        json.AppendLine("</script>");

        return json.ToString();
    }

    /// <summary>
    /// Generate schema.org BreadcrumbList markup
    /// </summary>
    public static string GenerateBreadcrumbSchema(List<(string name, string url)> breadcrumbs)
    {
        if (!breadcrumbs.Any())
            return string.Empty;

        var json = new System.Text.StringBuilder();
        json.AppendLine("<script type=\"application/ld+json\">");
        json.AppendLine("{");
        json.AppendLine("  \"@context\": \"https://schema.org\",");
        json.AppendLine("  \"@type\": \"BreadcrumbList\",");
        json.AppendLine("  \"itemListElement\": [");

        for (int i = 0; i < breadcrumbs.Count; i++)
        {
            json.AppendLine("    {");
            json.AppendLine("      \"@type\": \"ListItem\",");
            json.AppendLine($"      \"position\": {i + 1},");
            json.AppendLine($"      \"name\": \"{EscapeJson(breadcrumbs[i].name)}\",");
            json.AppendLine($"      \"item\": \"{EscapeJson(breadcrumbs[i].url)}\"");
            json.AppendLine("    }" + (i < breadcrumbs.Count - 1 ? "," : ""));
        }

        json.AppendLine("  ]");
        json.AppendLine("}");
        json.AppendLine("</script>");

        return json.ToString();
    }

    /// <summary>
    /// Escape HTML special characters
    /// </summary>
    private static string EscapeHtml(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }

    /// <summary>
    /// Escape JSON special characters
    /// </summary>
    private static string EscapeJson(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
