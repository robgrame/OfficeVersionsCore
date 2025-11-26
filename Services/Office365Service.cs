using OfficeVersionsCore.Models;
using System.Text.Json;
using System.Diagnostics; // added for Stopwatch
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Microsoft.Extensions.Hosting;

namespace OfficeVersionsCore.Services
{
    /// <summary>
    /// Interface for Office 365 versions service
    /// </summary>
    public interface IOffice365Service
    {
        Task<Office365VersionsData?> GetLatestVersionsAsync();
        Task<List<Office365Version>> GetVersionsByChannelAsync(string channel);
        Task<Office365Version?> GetLatestVersionForChannelAsync(string channel);
        // Channel-specific full release history endpoints
        Task<Office365VersionsData?> GetCurrentChannelReleasesAsync();
        Task<Office365VersionsData?> GetMonthlyEnterpriseChannelReleasesAsync();
        Task<Office365VersionsData?> GetSemiAnnualChannelReleasesAsync();
    }

    /// <summary>
    /// Service for retrieving Office 365 version data
    /// </summary>
    public class Office365Service : IOffice365Service
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<Office365Service> _logger;
        private readonly IStorageService _storageService;
        private readonly string _office365StoragePath;

        // Cache for aggregated latest versions data with expiration (original behavior)
        private Office365VersionsData? _cachedData;
        private DateTime _cacheExpiry = DateTime.MinValue;
        private readonly TimeSpan _cacheTime = TimeSpan.FromHours(1); // Cache for 1 hour

        // Per-endpoint cache for channel release history
        private readonly ConcurrentDictionary<string, (Office365VersionsData Data, DateTime Expiry)> _endpointCache = new();

        public Office365Service(
            HttpClient httpClient, 
            IConfiguration configuration, 
            ILogger<Office365Service> logger,
            IStorageService storageService)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _storageService = storageService;
            
            // Storage path from configuration (Office365:StoragePath), default to "officeversions"
            _office365StoragePath = _configuration["Office365:StoragePath"] ?? "officeversions";
            
            _logger.LogInformation("Office365Service initialized with local storage path: {Path}", _office365StoragePath);
        }

        /// <summary>
        /// Retrieves the aggregated latest Office 365 versions data (one entry per channel) from external source or cache.
        /// This should only be used for obtaining the most recent version per channel, NOT full release history.
        /// </summary>
        public async Task<Office365VersionsData?> GetLatestVersionsAsync()
        {
            try
            {
                // Check if cached data is still valid
                if (_cachedData != null && DateTime.UtcNow < _cacheExpiry)
                {
                    _logger.LogInformation("Returning cached aggregated Office 365 latest versions data (expires in {Remaining}s)", 
                        (_cacheExpiry - DateTime.UtcNow).TotalSeconds.ToString("F0"));
                    return _cachedData;
                }

                // Get data source URL from configuration (aggregated latest file)
                var dataUrl = _configuration["Office365:DataSourceUrl"]
                    ?? "https://officeversionscorestrg.blob.core.windows.net/jsonrepository/m365LatestVersions.json";
                var blobName = GetBlobNameFromUrl(dataUrl);

                var data = await FetchVersionsDataInternalAsync(dataUrl, blobName, cacheAggregated: true, cacheKey: null);
                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching aggregated Office 365 versions data. Returning cached (even if expired) = {HasCache}", _cachedData != null);

                // Return cached data if available, even if expired
                if (_cachedData != null)
                {
                    _logger.LogWarning("Returning expired cached aggregated data due to fetch error (expired {SecondsAgo}s ago)", 
                        (DateTime.UtcNow - _cacheExpiry).TotalSeconds.ToString("F0"));
                    return _cachedData;
                }

                return null;
            }
        }

        // ================= Channel specific full history endpoints =================

        public Task<Office365VersionsData?> GetCurrentChannelReleasesAsync() =>
            FetchChannelHistoryAsync("Current Channel", 
                _configuration["Office365:CurrentChannelUrl"] ?? "https://officeversionscorestrg.blob.core.windows.net/jsonrepository/m365CurrentReleases.json");

        public Task<Office365VersionsData?> GetMonthlyEnterpriseChannelReleasesAsync() =>
            FetchChannelHistoryAsync("Monthly Enterprise Channel", 
                _configuration["Office365:MonthlyEnterpriseChannelUrl"] ?? "https://officeversionscorestrg.blob.core.windows.net/jsonrepository/m365MonthlyReleases.json");

        public Task<Office365VersionsData?> GetSemiAnnualChannelReleasesAsync() =>
            FetchChannelHistoryAsync("Semi Annual Channel", 
                _configuration["Office365:SemiAnnualChannelUrl"] ?? "https://officeversionscorestrg.blob.core.windows.net/jsonrepository/m365SACReleases.json");

        private async Task<Office365VersionsData?> FetchChannelHistoryAsync(string channelName, string url)
        {
            var cacheKey = $"history::{channelName}"; // logical cache key per channel
            var blobName = GetBlobNameFromUrl(url);
            
            try
            {
                var data = await FetchVersionsDataInternalAsync(url, blobName, cacheAggregated: false, cacheKey: cacheKey);
                if (data == null)
                {
                    _logger.LogWarning("Channel history fetch returned null for {Channel}", channelName);
                }
                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching history for channel {Channel} from {Url}", channelName, url);
                // Attempt to return expired cache if present
                if (_endpointCache.TryGetValue(cacheKey, out var existing))
                {
                    _logger.LogWarning("Returning expired cached history for {Channel}", channelName);
                    return existing.Data;
                }
                return null;
            }
        }

        /// <summary>
        /// Get blob name from URL (e.g. "m365LatestVersions.json" from a full URL)
        /// </summary>
        private string GetBlobNameFromUrl(string url)
        {
            try
            {
                return Path.GetFileName(new Uri(url).LocalPath);
            }
            catch
            {
                return url.Split('/').Last();
            }
        }

        /// <summary>
        /// Internal unified fetch pipeline with detailed logging and caching.
        /// </summary>
        private async Task<Office365VersionsData?> FetchVersionsDataInternalAsync(
            string dataUrl, string blobName, bool cacheAggregated, string? cacheKey)
        {
            // For aggregated latest versions we use legacy single fields; for channel histories we cache per cacheKey
            if (!cacheAggregated && cacheKey == null)
                throw new ArgumentException("cacheKey must be provided when cacheAggregated is false", nameof(cacheKey));

            // Check endpoint-specific cache first (for channel histories)
            if (!cacheAggregated && cacheKey != null &&
                _endpointCache.TryGetValue(cacheKey, out var entry) && DateTime.UtcNow < entry.Expiry)
            {
                _logger.LogInformation("Returning cached channel history for {CacheKey} (expires in {Remaining}s)", 
                    cacheKey, (entry.Expiry - DateTime.UtcNow).TotalSeconds.ToString("F0"));
                return entry.Data;
            }

            // Aggregated cache check
            if (cacheAggregated && _cachedData != null && DateTime.UtcNow < _cacheExpiry)
            {
                return _cachedData; // earlier log already handled by caller
            }

            var swTotal = Stopwatch.StartNew();
            
            string jsonContent;
            
            _logger.LogInformation("Fetching Office 365 versions from local storage. File: {BlobName}", blobName);
            jsonContent = await FetchJsonFromStorageAsync(blobName);

            var swDeserialize = Stopwatch.StartNew();
            Office365VersionsData? data = null;
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                data = JsonSerializer.Deserialize<Office365VersionsData>(jsonContent, options);
            }
            catch (JsonException jex)
            {
                _logger.LogError(jex, "JSON deserialization failed. First 500 chars: {Preview}", 
                    jsonContent.Length > 500 ? jsonContent[..500] : jsonContent);
                throw;
            }
            swDeserialize.Stop();
            _logger.LogInformation("Deserialized JSON in {Elapsed} ms (total {Total} ms)", 
                swDeserialize.ElapsedMilliseconds, swTotal.ElapsedMilliseconds);

            // Attempt to populate LastUpdatedUTC if missing (some legacy JSON structures differ)
            if (cacheAggregated && data != null && (data.DataForNerds == null || string.IsNullOrWhiteSpace(data.DataForNerds.LastUpdatedUTC)))
            {
                try
                {
                    using var doc = JsonDocument.Parse(jsonContent);
                    string? extracted = null;
                    // Root level LastUpdatedUTC
                    if (doc.RootElement.TryGetProperty("LastUpdatedUTC", out var rootTs) && rootTs.ValueKind == JsonValueKind.String)
                    {
                        extracted = rootTs.GetString();
                    }
                    // Nested Office365Versions.LastUpdatedUTC
                    else if (doc.RootElement.TryGetProperty("Office365Versions", out var ov) && ov.ValueKind == JsonValueKind.Object && 
                            ov.TryGetProperty("LastUpdatedUTC", out var ovTs) && ovTs.ValueKind == JsonValueKind.String)
                    {
                        extracted = ovTs.GetString();
                    }

                    if (!string.IsNullOrWhiteSpace(extracted))
                    {
                        data.DataForNerds ??= new DataForNerds();
                        data.DataForNerds.LastUpdatedUTC = extracted!;
                        _logger.LogInformation("Populated LastUpdatedUTC from fallback JSON structure: {Timestamp}", extracted);
                    }
                    else
                    {
                        _logger.LogDebug("Could not locate LastUpdatedUTC in fallback JSON parsing.");
                    }
                }
                catch (Exception exTs)
                {
                    _logger.LogDebug(exTs, "Failed fallback extraction of LastUpdatedUTC.");
                }
            }

            if (data != null)
            {
                if (cacheAggregated)
                {
                    _cachedData = data;
                    _cacheExpiry = DateTime.UtcNow.Add(_cacheTime);
                    _logger.LogInformation("Caching aggregated data ({Count} entries). Cache valid until {Expiry:O}. Total fetch {Total} ms", 
                        data.Data?.Count ?? 0, _cacheExpiry, swTotal.ElapsedMilliseconds);
                }
                else if (cacheKey != null)
                {
                    var expiry = DateTime.UtcNow.Add(_cacheTime);
                    _endpointCache[cacheKey] = (data, expiry);
                    _logger.LogInformation("Caching channel history for {CacheKey} ({Count} entries). Cache valid until {Expiry:O}. Total fetch {Total} ms", 
                        cacheKey, data.Data?.Count ?? 0, expiry, swTotal.ElapsedMilliseconds);
                }
            }
            else
            {
                _logger.LogWarning("Deserialized data object is null (total {Total} ms) for {Url}", swTotal.ElapsedMilliseconds, dataUrl);
            }

            return data;
        }

        /// <summary>
        /// Unified method to fetch JSON from local storage
        /// </summary>
        private async Task<string> FetchJsonFromStorageAsync(string blobName)
        {
            var stopwatch = Stopwatch.StartNew();
            var fileName = $"{_office365StoragePath}/{blobName}";
            _logger.LogInformation("Starting fetch for file {FileName} from local storage", fileName);
            
            try
            {
                if (await _storageService.ExistsAsync(fileName))
                {
                    var content = await _storageService.ReadAsync(fileName);
                    stopwatch.Stop();
                    _logger.LogInformation("Successfully read file {FileName} from local storage in {Elapsed}ms. Content length: {Length}", 
                        fileName, stopwatch.ElapsedMilliseconds, content.Length);
                    return content;
                }
                else
                {
                    stopwatch.Stop();
                    _logger.LogError("File {FileName} not found in local storage", fileName);
                    throw new FileNotFoundException($"File {fileName} not found in local storage");
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error reading file {FileName} from local storage after {Elapsed}ms", 
                    fileName, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        /// <summary>
        /// Gets all versions for a specific channel using the dedicated channel history JSON.
        /// </summary>
        public async Task<List<Office365Version>> GetVersionsByChannelAsync(string channel)
        {
            Office365VersionsData? history = null;
            switch (channel.Trim().ToLowerInvariant())
            {
                case "current channel":
                    history = await GetCurrentChannelReleasesAsync();
                    break;
                case "monthly enterprise channel":
                    history = await GetMonthlyEnterpriseChannelReleasesAsync();
                    break;
                case "semi annual channel":
                case "semi-annual channel":
                case "semi annual enterprise channel":
                case "semi-annual enterprise channel":
                    history = await GetSemiAnnualChannelReleasesAsync();
                    break;
                default:
                    _logger.LogWarning("Unknown channel '{Channel}' requested. Returning empty list.", channel);
                    return new List<Office365Version>();
            }

            if (history?.Data == null)
            {
                _logger.LogWarning("No history data available when querying channel {Channel}", channel);
                return new List<Office365Version>();
            }

            var list = history.Data
                .OrderByDescending(v => v.LatestReleaseDate)
                .ToList();

            _logger.LogInformation("Found {Count} versions for channel {Channel} from history endpoint", list.Count, channel);
            return list;
        }

        /// <summary>
        /// Gets the latest version for a specific channel (uses aggregated latest versions file for efficiency).
        /// </summary>
        public async Task<Office365Version?> GetLatestVersionForChannelAsync(string channel)
        {
            var aggregated = await GetLatestVersionsAsync();
            var latest = aggregated?.Data?
                .Where(v => v.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => v.LatestReleaseDate)
                .FirstOrDefault();

            if (latest == null)
            {
                _logger.LogWarning("No latest version found for channel {Channel}", channel);
            }
            else
            {
                _logger.LogInformation("Latest version for channel {Channel} is {Version} (Build {Build})", 
                    channel, latest.Version, latest.Build);
            }
            return latest;
        }
    }
}