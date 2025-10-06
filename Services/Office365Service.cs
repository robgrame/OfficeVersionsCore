using OfficeVersionsCore.Models;
using System.Text.Json;
using System.Diagnostics; // added for Stopwatch
using System.Collections.Concurrent;
using Azure.Storage.Blobs;
using Azure.Identity;
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
        private readonly BlobServiceClient _blobServiceClient;
        private readonly IWebHostEnvironment _environment;

        // Cache for aggregated latest versions data with expiration (original behavior)
        private Office365VersionsData? _cachedData;
        private DateTime _cacheExpiry = DateTime.MinValue;
        private readonly TimeSpan _cacheTime = TimeSpan.FromHours(1); // Cache for 1 hour

        // Per-endpoint cache for channel release history
        private readonly ConcurrentDictionary<string, (Office365VersionsData Data, DateTime Expiry)> _endpointCache = new();

        // Storage access settings
        private readonly bool _usePrivateStorage;
        private readonly string _containerName;
        private readonly bool _useAzurite;

        public Office365Service(
            HttpClient httpClient, 
            IConfiguration configuration, 
            ILogger<Office365Service> logger,
            BlobServiceClient blobServiceClient,
            IWebHostEnvironment environment)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _blobServiceClient = blobServiceClient;
            _environment = environment;
            
            // Check if we should use private storage access with Azure Identity
            _usePrivateStorage = _configuration.GetValue<bool>("Office365:UsePrivateStorage", false);
            _containerName = _configuration["SEC_STOR_StorageCon"] ?? "jsonrepository";
            _useAzurite = _environment.IsDevelopment() && 
                          _configuration.GetValue<bool>("AzuriteStorage:UseAzurite", false);
            
            if (_useAzurite)
            {
                _logger.LogInformation("Office365Service initialized with Azurite local storage. Container: {Container}", 
                    _containerName);
            }
            else if (_usePrivateStorage)
            {
                _logger.LogInformation("Office365Service initialized with managed identity for storage access. Container: {Container}", 
                    _containerName);
            }
            else
            {
                _logger.LogInformation("Office365Service initialized with HTTP access. Container: {Container}",
                    _containerName);
            }
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
            
            // Determine storage access method based on configuration
            if (_useAzurite)
            {
                _logger.LogInformation("Using Azurite for local storage. Blob: {BlobName}", blobName);
                jsonContent = await FetchJsonFromStorageAsync(blobName);
            }
            else if (_usePrivateStorage)
            {
                _logger.LogInformation("Using managed identity to access storage. Blob: {BlobName}", blobName);
                jsonContent = await FetchJsonFromStorageAsync(blobName);
            }
            else
            {
                _logger.LogInformation("Starting fetch for Office 365 versions via HTTP. URL={Url} Timeout={Timeout}s", 
                    dataUrl, _httpClient.Timeout.TotalSeconds);

                var swRequest = Stopwatch.StartNew();
                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.GetAsync(dataUrl, HttpCompletionOption.ResponseHeadersRead);
                }
                catch (TaskCanceledException tce) when (!tce.CancellationToken.IsCancellationRequested)
                {
                    _logger.LogError(tce, "HTTP request to {Url} timed out after ~{Timeout}s (TaskCanceledException)", 
                        dataUrl, _httpClient.Timeout.TotalSeconds);
                    throw; // escalate
                }

                swRequest.Stop();
                _logger.LogInformation("Received HTTP headers from {Url} in {Elapsed} ms StatusCode={StatusCode} Version={Version}", 
                    dataUrl, swRequest.ElapsedMilliseconds, (int)response.StatusCode, response.Version);

                response.EnsureSuccessStatusCode();

                var swRead = Stopwatch.StartNew();
                jsonContent = await response.Content.ReadAsStringAsync();
                swRead.Stop();
                _logger.LogInformation("Read response body ({Length} chars) in {Elapsed} ms (total so far {Total} ms)", 
                    jsonContent.Length, swRead.ElapsedMilliseconds, swTotal.ElapsedMilliseconds);
            }

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
        /// Unified method to fetch JSON from storage (works with both Azurite and Azure Storage)
        /// </summary>
        private async Task<string> FetchJsonFromStorageAsync(string blobName)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Starting blob fetch for blob {BlobName} from container {Container}", 
                blobName, _containerName);
            
            try
            {
                // Get container client using the injected BlobServiceClient
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                
                // Create container if it doesn't exist (mainly for Azurite)
                await containerClient.CreateIfNotExistsAsync();
                
                // Get blob client and download content
                var blobClient = containerClient.GetBlobClient(blobName);
                
                // Check if blob exists
                if (!await blobClient.ExistsAsync())
                {
                    // If using Azurite, create an empty JSON file structure
                    if (_useAzurite)
                    {
                        _logger.LogInformation("Blob {BlobName} does not exist in Azurite. Creating a default placeholder...", blobName);
                        
                        // Create default content
                        var defaultData = new Office365VersionsData
                        {
                            DataForNerds = new DataForNerds
                            {
                                LastUpdatedUTC = $"{DateTime.UtcNow.ToLongDateString()} | {DateTime.UtcNow.ToLongTimeString()} UTC",
                                SourcePage = new List<string> { "Local Development" },
                                TimeElapsedMs = 0
                            },
                            Data = new List<Office365Version>
                            {
                                new Office365Version
                                {
                                    Channel = "Current Channel",
                                    Version = "2304",
                                    Build = "16501.20040",
                                    LatestReleaseDate = "2023 May 09",
                                    FirstAvailabilityDate = "2023 Apr 26",
                                    EndOfService = "2023 Oct 11"
                                }
                            }
                        };
                        
                        // Serialize and upload
                        var jsonContent = JsonSerializer.Serialize(defaultData, new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        });
                        
                        // Upload the content
                        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(jsonContent);
                        using var ms = new MemoryStream(bytes);
                        await blobClient.UploadAsync(ms, overwrite: true);
                        
                        _logger.LogInformation("Created default placeholder for {BlobName}", blobName);
                        
                        return jsonContent;
                    }
                    else
                    {
                        _logger.LogError("Blob {BlobName} not found in container {Container}", blobName, _containerName);
                        throw new FileNotFoundException($"Blob {blobName} not found in container {_containerName}");
                    }
                }
                
                // Download blob content
                using var memoryStream = new MemoryStream();
                await blobClient.DownloadToAsync(memoryStream);
                memoryStream.Position = 0;
                
                using var streamReader = new StreamReader(memoryStream, Encoding.UTF8);
                var content = await streamReader.ReadToEndAsync();
                
                stopwatch.Stop();
                _logger.LogInformation("Successfully downloaded blob {BlobName} from {Container} in {Elapsed}ms. Content length: {Length}", 
                    blobName, _containerName, stopwatch.ElapsedMilliseconds, content.Length);
                
                return content;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error accessing blob {BlobName} from {Container} after {Elapsed}ms", 
                    blobName, _containerName, stopwatch.ElapsedMilliseconds);
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