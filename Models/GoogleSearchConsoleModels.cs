namespace OfficeVersionsCore.Models;

/// <summary>
/// Represents a single query's performance data from Google Search Console
/// </summary>
public class QueryPerformance
{
    /// <summary>
    /// The search query (e.g., "office 365 versions", "windows 11 release")
    /// </summary>
    public required string Query { get; set; }

    /// <summary>
    /// Number of times this query led to a click on the site
    /// </summary>
    public int Clicks { get; set; }

    /// <summary>
    /// Number of times this query appeared in search results
    /// </summary>
    public int Impressions { get; set; }

    /// <summary>
    /// Click-through rate as a percentage (e.g., 3.46 for 3.46%)
    /// </summary>
    public decimal CTR { get; set; }

    /// <summary>
    /// Average position in search results (e.g., 4.64)
    /// </summary>
    public decimal Position { get; set; }

    /// <summary>
    /// Extract the main topic from the query for meta tag generation
    /// Examples:
    /// - "office 365 versions" ? "Office 365 Versions"
    /// - "windows 11 22h2" ? "Windows 11 22H2"
    /// </summary>
    public string GetMetaTitle()
    {
        return $"{ToPascalCase(Query)} - Office 365 & Windows Versions";
    }

    /// <summary>
    /// Generate a compelling meta description based on query performance
    /// </summary>
    public string GetMetaDescription()
    {
        var metric = Impressions > 0 ? $"Tracked by {Impressions:N0} searches" : "Latest version information";
        return $"{ToPascalCase(Query)}. {metric}. Find the latest releases and version numbers.";
    }

    /// <summary>
    /// Determine if this query deserves optimization priority
    /// Priority is high if:
    /// - Low CTR (opportunity to improve)
    /// - High position (already visible)
    /// - High impressions (has traffic)
    /// </summary>
    public int GetOptimizationPriority()
    {
        if (Position <= 3 && Clicks >= 10)
            return 1; // Top tier - already performing well

        if (Position <= 5 && CTR < 5)
            return 2; // Medium - room for improvement

        if (Impressions >= 100 && CTR < 2)
            return 3; // High opportunity - many impressions, low CTR

        if (Impressions > 0)
            return 4; // Low priority - some visibility

        return 5; // No visibility yet
    }

    /// <summary>
    /// Generate comma-separated keywords for meta tags
    /// </summary>
    public string GetMetaKeywords()
    {
        var words = Query.Split(' ');
        var keywords = new List<string> { Query };

        // Add variations
        if (words.Length > 1)
        {
            keywords.AddRange(words);
        }

        return string.Join(", ", keywords.Distinct().Take(5));
    }

    /// <summary>
    /// Determine SEO potential score (0-100)
    /// Based on: position (lower is better), CTR (higher is better), impressions (higher is better)
    /// </summary>
    public int GetSEOScore()
    {
        var positionScore = Math.Max(0, 100 - (int)(Position * 10)); // Better position = higher score
        var ctrScore = (int)(CTR * 5); // Better CTR = higher score
        var impressionScore = Math.Min(100, Impressions / 10); // More impressions = higher score

        return (positionScore + ctrScore + impressionScore) / 3;
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var words = input.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
            {
                words[i] = char.ToUpper(words[i][0]) + (words[i].Length > 1 ? words[i].Substring(1) : "");
            }
        }

        return string.Join(" ", words);
    }
}

/// <summary>
/// Aggregated Google Search Console data
/// </summary>
public class GoogleSearchConsoleData
{
    /// <summary>
    /// All queries from GSC export
    /// </summary>
    public List<QueryPerformance> AllQueries { get; set; } = [];

    /// <summary>
    /// Total clicks across all queries
    /// </summary>
    public int TotalClicks => AllQueries.Sum(q => q.Clicks);

    /// <summary>
    /// Total impressions across all queries
    /// </summary>
    public int TotalImpressions => AllQueries.Sum(q => q.Impressions);

    /// <summary>
    /// Average CTR across all queries
    /// </summary>
    public decimal AverageCTR => AllQueries.Count > 0
        ? AllQueries.Average(q => q.CTR)
        : 0;

    /// <summary>
    /// Average position across all queries
    /// </summary>
    public decimal AveragePosition => AllQueries.Count > 0
        ? AllQueries.Average(q => q.Position)
        : 0;

    /// <summary>
    /// Queries with zero CTR (optimization opportunities)
    /// </summary>
    public List<QueryPerformance> ZeroCTRQueries => AllQueries.Where(q => q.CTR == 0).ToList();

    /// <summary>
    /// Top performing queries (sorted by clicks)
    /// </summary>
    public List<QueryPerformance> GetTopQueries(int count = 10)
    {
        return AllQueries.OrderByDescending(q => q.Clicks).Take(count).ToList();
    }

    /// <summary>
    /// Queries with highest optimization potential
    /// (Low CTR, high impressions)
    /// </summary>
    public List<QueryPerformance> GetHighPotentialQueries(int count = 10)
    {
        return AllQueries
            .Where(q => q.Impressions >= 100 && q.CTR < 5)
            .OrderByDescending(q => q.Impressions)
            .Take(count)
            .ToList();
    }
}
