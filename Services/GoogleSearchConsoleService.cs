using OfficeVersionsCore.Models;
using System.Globalization;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.IO;

namespace OfficeVersionsCore.Services;

/// <summary>
/// Service to manage Google Search Console data
/// Handles CSV import, caching, and query optimization recommendations
/// </summary>
public interface IGoogleSearchConsoleService
{
    /// <summary>
    /// Parse Google Search Console CSV export
    /// Expected CSV format: Query, Clicks, Impressions, CTR, Position
    /// </summary>
    Task<GoogleSearchConsoleData> ParseCsvAsync(Stream csvStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load GSC data from a file path
    /// </summary>
    Task<GoogleSearchConsoleData> LoadFromFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get cached GSC data
    /// </summary>
    Task<GoogleSearchConsoleData?> GetCachedDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get top performing queries for meta tag optimization
    /// </summary>
    Task<List<QueryPerformance>> GetTopQueriesAsync(int count = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get high-potential queries (low CTR, high impressions) for optimization
    /// </summary>
    Task<List<QueryPerformance>> GetHighPotentialQueriesAsync(int count = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find a specific query by name
    /// </summary>
    Task<QueryPerformance?> FindQueryAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get suggested meta tags for a specific query
    /// </summary>
    Task<(string Title, string Description, string Keywords)> GetMetaTagsForQueryAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of IGoogleSearchConsoleService
/// </summary>
public class GoogleSearchConsoleService : IGoogleSearchConsoleService
{
    private readonly ILogger<GoogleSearchConsoleService> _logger;
    private readonly ICacheService _cacheService;
    private GoogleSearchConsoleData? _cachedData;
    private readonly string _cacheKey = "gsc_data";
    private const int CacheTTLMinutes = 60;

    public GoogleSearchConsoleService(
        ILogger<GoogleSearchConsoleService> logger,
        ICacheService cacheService)
    {
        _logger = logger;
        _cacheService = cacheService;
    }

    public async Task<GoogleSearchConsoleData> ParseCsvAsync(Stream csvStream, CancellationToken cancellationToken = default)
    {
        var data = new GoogleSearchConsoleData();

        try
        {
            using var reader = new StreamReader(csvStream);
            var csvContent = await reader.ReadToEndAsync(cancellationToken);
            
            // Parse the entire CSV content at once to handle multi-line quoted fields
            var records = ParseCsvContent(csvContent);

            foreach (var record in records)
            {
                var query = ParseCsvRecord(record);
                if (query != null)
                {
                    data.AllQueries.Add(query);
                }
            }

            _logger.LogInformation($"Successfully parsed {data.AllQueries.Count} queries from GSC CSV");

            // Cache the data
            _cachedData = data;
            await _cacheService.SetStringAsync(
                _cacheKey,
                JsonSerializer.Serialize(data),
                TimeSpan.FromMinutes(CacheTTLMinutes),
                cancellationToken);

            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing GSC CSV");
            throw;
        }
    }

    public async Task<GoogleSearchConsoleData> LoadFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            return await ParseCsvAsync(fileStream, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error loading GSC data from file: {filePath}");
            throw;
        }
    }

    public async Task<GoogleSearchConsoleData?> GetCachedDataAsync(CancellationToken cancellationToken = default)
    {
        // Return in-memory cache if available
        if (_cachedData != null)
            return _cachedData;

        // Try to load from distributed cache
        try
        {
            var cachedJson = await _cacheService.GetStringAsync(_cacheKey, cancellationToken);
            if (!string.IsNullOrEmpty(cachedJson))
            {
                _cachedData = JsonSerializer.Deserialize<GoogleSearchConsoleData>(cachedJson);
                return _cachedData;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrieving GSC data from cache");
        }

        return null;
    }

    public async Task<List<QueryPerformance>> GetTopQueriesAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        var data = await GetCachedDataAsync(cancellationToken);
        return data?.GetTopQueries(count) ?? [];
    }

    public async Task<List<QueryPerformance>> GetHighPotentialQueriesAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        var data = await GetCachedDataAsync(cancellationToken);
        return data?.GetHighPotentialQueries(count) ?? [];
    }

    public async Task<QueryPerformance?> FindQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        var data = await GetCachedDataAsync(cancellationToken);
        if (data == null)
            return null;

        var normalized = query.ToLower().Trim();
        return data.AllQueries.FirstOrDefault(q => q.Query.ToLower() == normalized);
    }

    public async Task<(string Title, string Description, string Keywords)> GetMetaTagsForQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        var queryData = await FindQueryAsync(query, cancellationToken);

        if (queryData != null)
        {
            return (
                queryData.GetMetaTitle(),
                queryData.GetMetaDescription(),
                queryData.GetMetaKeywords()
            );
        }

        // Fallback if query not found
        return (
            $"{ToPascalCase(query)} - Office 365 & Windows Versions",
            $"Find information about {query}. Latest version numbers and release details.",
            query
        );
    }

    /// <summary>
    /// Parse CSV content handling multi-line quoted fields
    /// </summary>
    private List<List<string>> ParseCsvContent(string csvContent)
    {
        var records = new List<List<string>>();
        var lines = csvContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        var currentRecord = new List<string>();
        var currentField = new System.Text.StringBuilder();
        bool insideQuotes = false;
        bool isFirstLine = true;

        foreach (var line in lines)
        {
            // Skip empty lines
            if (string.IsNullOrWhiteSpace(line) && !insideQuotes)
                continue;

            // Skip header row
            if (isFirstLine && line.StartsWith("Query"))
            {
                isFirstLine = false;
                continue;
            }
            isFirstLine = false;

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    insideQuotes = !insideQuotes;
                }
                else if (c == ',' && !insideQuotes)
                {
                    currentRecord.Add(currentField.ToString().Trim());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }

            // If we're not inside quotes, the line is complete
            if (!insideQuotes)
            {
                if (currentField.Length > 0 || currentRecord.Count > 0)
                {
                    currentRecord.Add(currentField.ToString().Trim());
                    if (currentRecord.Count >= 5) // Valid record has at least 5 fields
                    {
                        records.Add(new List<string>(currentRecord));
                    }
                    currentRecord.Clear();
                    currentField.Clear();
                }
            }
            else
            {
                // Still inside quotes, add newline continuation
                currentField.Append("\n");
            }
        }

        // Handle any remaining data
        if (currentField.Length > 0)
        {
            currentRecord.Add(currentField.ToString().Trim());
        }
        if (currentRecord.Count >= 5)
        {
            records.Add(currentRecord);
        }

        return records;
    }

    /// <summary>
    /// Parse a CSV record (list of fields) into QueryPerformance object
    /// Expected format: ["Query", "Clicks", "Impressions", "CTR", "Position"]
    /// </summary>
    private QueryPerformance? ParseCsvRecord(List<string> fields)
    {
        try
        {
            if (fields.Count < 5)
            {
                _logger.LogDebug($"Invalid CSV record (insufficient fields): {string.Join(",", fields)}");
                return null;
            }

            var query = fields[0].Replace("\"", "").Trim();
            var clicks = int.TryParse(fields[1], out var c) ? c : 0;
            var impressions = int.TryParse(fields[2], out var i) ? i : 0;

            // Handle CTR - might be in percentage format (3.46%) or decimal (0.0346)
            var ctrString = fields[3].Replace("%", "").Replace("\"", "").Trim();
            decimal ctr = decimal.TryParse(ctrString, NumberStyles.Float, CultureInfo.InvariantCulture, out var ctrVal) ? ctrVal : 0;

            // If CTR looks like decimal (< 1), convert to percentage
            if (ctr < 1 && ctr > 0)
                ctr *= 100;

            var positionString = fields[4].Replace("\"", "").Trim();
            decimal position = decimal.TryParse(positionString, NumberStyles.Float, CultureInfo.InvariantCulture, out var posVal) ? posVal : 0;

            // Validate query
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            {
                _logger.LogDebug($"Invalid query (too short or empty): '{query}'");
                return null;
            }

            return new QueryPerformance
            {
                Query = query,
                Clicks = clicks,
                Impressions = impressions,
                CTR = ctr,
                Position = position
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Error parsing CSV record: {string.Join(",", fields)}");
            return null;
        }
    }

    /// <summary>
    /// Parse a single CSV line into QueryPerformance object
    /// Expected format: "Query","Clicks","Impressions","CTR","Position"
    /// </summary>
    private QueryPerformance? ParseCsvLine(string line)
    {
        try
        {
            // Handle CSV with quoted fields
            var fields = ParseCsvFields(line);

            if (fields.Count < 5)
            {
                _logger.LogWarning($"Invalid CSV line (insufficient fields): {line}");
                return null;
            }

            var query = fields[0].Trim();
            var clicks = int.TryParse(fields[1], out var c) ? c : 0;
            var impressions = int.TryParse(fields[2], out var i) ? i : 0;

            // Handle CTR - might be in percentage format (3.46%) or decimal (0.0346)
            var ctrString = fields[3].Replace("%", "").Trim();
            decimal ctr = decimal.TryParse(ctrString, NumberStyles.Float, CultureInfo.InvariantCulture, out var ctrVal) ? ctrVal : 0;

            // If CTR looks like decimal (< 1), convert to percentage
            if (ctr < 1)
                ctr *= 100;

            var positionString = fields[4].Trim();
            decimal position = decimal.TryParse(positionString, NumberStyles.Float, CultureInfo.InvariantCulture, out var posVal) ? posVal : 0;

            if (string.IsNullOrWhiteSpace(query))
                return null;

            return new QueryPerformance
            {
                Query = query,
                Clicks = clicks,
                Impressions = impressions,
                CTR = ctr,
                Position = position
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Error parsing CSV line: {line}");
            return null;
        }
    }

    /// <summary>
    /// Parse CSV fields handling quoted values with commas inside
    /// </summary>
    private List<string> ParseCsvFields(string line)
    {
        var fields = new List<string>();
        var currentField = new System.Text.StringBuilder();
        bool insideQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                insideQuotes = !insideQuotes;
                continue;
            }

            if (c == ',' && !insideQuotes)
            {
                fields.Add(currentField.ToString());
                currentField.Clear();
                continue;
            }

            currentField.Append(c);
        }

        // Add last field
        if (currentField.Length > 0)
            fields.Add(currentField.ToString());

        return fields;
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
