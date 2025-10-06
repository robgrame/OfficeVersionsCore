using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OfficeVersionsCore.Models;

namespace OfficeVersionsCore.Services.BackgroundTasks
{
    public class Office365VersionScraper : BackgroundService
    {
        private readonly ILogger<Office365VersionScraper> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly IWebHostEnvironment _environment;
        private readonly string _rootUrl = "https://docs.microsoft.com/en-us/officeupdates/";
        private readonly string _rootPage = "https://learn.microsoft.com/en-us/officeupdates/update-history-microsoft365-apps-by-date";
        
        private readonly string _storageContainerName;
        private readonly bool _useAzurite;
        
        private readonly TimeSpan _interval;

        public Office365VersionScraper(
            ILogger<Office365VersionScraper> logger,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            BlobServiceClient blobServiceClient,
            IWebHostEnvironment environment)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClient = httpClientFactory.CreateClient("Office365Scraper");
            _httpClient.Timeout = TimeSpan.FromMinutes(2);
            _blobServiceClient = blobServiceClient;
            _environment = environment;

            // Get configuration values
            _storageContainerName = _configuration["SEC_STOR_StorageCon"] ?? "jsonrepository";
            _useAzurite = _environment.IsDevelopment() && 
                         _configuration.GetValue<bool>("AzuriteStorage:UseAzurite", false);
            
            // Default to 5 minutes if not configured, can be set in minutes in appsettings.json
            int intervalMinutes;
            if (!int.TryParse(_configuration["Office365Scraper:intervalMinutes"], out intervalMinutes))
            {
                intervalMinutes = 5;
            }
            _interval = TimeSpan.FromMinutes(intervalMinutes);

            if (_useAzurite)
            {
                _logger.LogInformation("Office 365 Version Scraper initialized with interval: {Interval} minutes. Using Azurite for local storage.", 
                    _interval.TotalMinutes);
            }
            else
            {
                _logger.LogInformation("Office 365 Version Scraper initialized with interval: {Interval} minutes. Using managed identity for storage access.", 
                    _interval.TotalMinutes);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Office 365 Version Scraper service starting");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Starting Office 365 version scraping at: {Time}", DateTimeOffset.Now);
                try
                {
                    await ScrapeAndUploadVersionsDataAsync(stoppingToken);
                    _logger.LogInformation("Office 365 version scraping completed successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during Office 365 version scraping");
                }

                _logger.LogInformation("Next Office 365 version scraping scheduled for: {Time}", DateTimeOffset.Now.Add(_interval));
                await Task.Delay(_interval, stoppingToken);
            }
        }

        private async Task ScrapeAndUploadVersionsDataAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Fetching Office 365 version data from {Url}", _rootPage);
            
            // Fetch the root page with version data
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await _httpClient.GetAsync(_rootPage, stoppingToken);
            response.EnsureSuccessStatusCode();
            
            var pageContent = await response.Content.ReadAsStringAsync(stoppingToken);
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            _logger.LogInformation("Retrieved Office 365 versions page in {Elapsed}ms", elapsedMs);

            // Parse the HTML content
            var tablesRegex = new Regex(@"(?msi)<table>(?:.*?)<tbody>(.*?)<\/tbody>");
            var tables = tablesRegex.Matches(pageContent);

            // Log the number of tables found for debugging
            _logger.LogInformation("Found {TableCount} tables in the page content", tables.Count);

            if (tables.Count < 2)
            {
                // Log the HTML structure for debugging
                _logger.LogError("Failed to find expected tables in the page content. First 500 chars of page content: {PageContent}", 
                    pageContent.Length > 500 ? pageContent.Substring(0, 500) : pageContent);
                    
                // Try a different regex pattern if the page structure might have changed
                tablesRegex = new Regex(@"(?msi)<table\b[^>]*>(.*?)<\/table>");
                tables = tablesRegex.Matches(pageContent);
                _logger.LogInformation("Second attempt: Found {TableCount} tables with alternate pattern", tables.Count);
                
                if (tables.Count < 2)
                {
                    _logger.LogError("Unable to find tables even with alternate pattern. Aborting.");
                    return;
                }
            }

            try
            {
                // Process latest versions table
                var latestVersionsTable = tables[0].Groups[1].Value;
                var latestVersions = ProcessLatestVersionsTable(latestVersionsTable, elapsedMs);
                await UploadJsonDataAsync(latestVersions, "m365LatestVersions.json", stoppingToken);
        
                // Process version history table
                _logger.LogInformation("Starting to process version history table");
                var versionHistoryTable = tables[1].Groups[1].Value;
                
                // Log a small sample of the table content to debug
                _logger.LogDebug("Version history table content sample (first 200 chars): {TableSample}", 
                    versionHistoryTable.Length > 200 ? versionHistoryTable.Substring(0, 200) : versionHistoryTable);
                
                var allReleases = ProcessVersionHistoryTable(versionHistoryTable, elapsedMs);
                _logger.LogInformation("Processed version history table, found {ReleaseCount} releases", 
                    allReleases?.Data?.Count ?? 0);
                
                // Upload all releases JSON
                await UploadJsonDataAsync(allReleases, "m365releases.json", stoppingToken);
                
                // Filter by channel and create separate JSON files
                var currentChannel = FilterByChannel(allReleases, "Current Channel");
                var monthlyEnterprise = FilterByChannel(allReleases, "Monthly Enterprise Channel");
                var semiAnnualChannel = FilterByChannel(allReleases, "Semi-Annual Enterprise Channel");
                var semiAnnualPreview = FilterByChannel(allReleases, "Semi-Annual Enterprise Preview");
                
                // Log number of releases per channel
                _logger.LogInformation("Found releases by channel: Current={Current}, Monthly={Monthly}, SAC={SAC}, SACPreview={SACPreview}",
                    currentChannel.Data.Count, monthlyEnterprise.Data.Count, 
                    semiAnnualChannel.Data.Count, semiAnnualPreview.Data.Count);
                
                // Upload channel-specific JSON files
                await UploadJsonDataAsync(currentChannel, "m365CurrentReleases.json", stoppingToken);
                await UploadJsonDataAsync(monthlyEnterprise, "m365MonthlyReleases.json", stoppingToken);
                await UploadJsonDataAsync(semiAnnualChannel, "m365SACReleases.json", stoppingToken);
                await UploadJsonDataAsync(semiAnnualPreview, "m365SACPreviewReleases.json", stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Office 365 versions data");
                throw;
            }
        }

        private Office365VersionsData FilterByChannel(Office365VersionsData allReleases, string channelName)
        {
            // Use ISO 8601 format for timestamp for better JavaScript compatibility
            var timestamp = DateTime.UtcNow.ToString("o");
            
            var metadata = new DataForNerds
            {
                LastUpdatedUTC = timestamp,
                SourcePage = allReleases.DataForNerds.SourcePage,
                TimeElapsedMs = allReleases.DataForNerds.TimeElapsedMs
            };
            
            var filteredData = allReleases.Data
                .Where(r => r.Channel == channelName)
                .OrderByDescending(r => DateTime.Parse(r.ReleaseDate))
                .ToList();
            
            return new Office365VersionsData
            {
                DataForNerds = metadata,
                Data = filteredData
            };
        }

        private Office365VersionsData ProcessLatestVersionsTable(string tableContent, long elapsedMs)
        {
            var rowsRegex = new Regex(@"(?msi)<tr>(.*?)<\/tr>");
            var rows = rowsRegex.Matches(tableContent);
            
            var latestVersions = new List<Office365Version>();

            foreach (Match row in rows)
            {
                var cellDataRegex = new Regex(@"(?<=<td.*?>)[^<]+(?=<)");
                var cellData = cellDataRegex.Matches(row.Groups[1].Value);
                
                if (cellData.Count >= 11)
                {
                    var channel = cellData[0].Value;
                    var version = cellData[2].Value;
                    var build = cellData[4].Value;
                    var latestReleaseDate = ParseDate(cellData[6].Value);
                    var firstAvailabilityDate = ParseDate(cellData[8].Value);
                    var endOfService = ParseDateOrString(cellData[10].Value);

                    latestVersions.Add(new Office365Version
                    {
                        Channel = channel,
                        Version = version,
                        Build = build,
                        LatestReleaseDate = latestReleaseDate,
                        FirstAvailabilityDate = firstAvailabilityDate,
                        EndOfService = endOfService
                    });
                }
            }

            // Use ISO 8601 format for timestamp for better JavaScript compatibility
            var timestamp = DateTime.UtcNow.ToString("o");

            // Match PowerShell structure
            var result = new Office365VersionsData
            {
                DataForNerds = new DataForNerds
                {
                    LastUpdatedUTC = timestamp,
                    SourcePage = new List<string> { _rootPage },
                    TimeElapsedMs = (int)elapsedMs
                },
                Data = latestVersions
            };

            return result;
        }

        private Office365VersionsData ProcessVersionHistoryTable(string tableContent, long elapsedMs)
        {
            var rowsRegex = new Regex(@"(?msi)<tr>(.*?)<\/tr>");
            var rows = rowsRegex.Matches(tableContent);
            
            var allReleases = new List<Office365Version>();
            string? lastReleaseYear = null;
            
            foreach (Match row in rows)
            {
                var cellsRegex = new Regex(@"(?msi)<td(?:[^>]*)>(.*?)<\/td>");
                var cells = cellsRegex.Matches(row.Groups[1].Value);
                
                if (cells.Count >= 6)
                {
                    // Extract release year and date
                    var releaseYear = cells[0].Groups[1].Value.Trim();
                    if (string.IsNullOrEmpty(releaseYear))
                    {
                        releaseYear = lastReleaseYear;
                    }
                    else
                    {
                        lastReleaseYear = releaseYear;
                    }

                    var releaseDate = cells[1].Groups[1].Value.Replace("<br>", "").Trim();
                    var fullReleaseDate = ParseDate($"{releaseDate} {releaseYear}");
                    
                    // Extract links and version info from each channel cell
                    ProcessChannelCell(cells[2].Groups[1].Value, "Current Channel", fullReleaseDate, allReleases);
                    ProcessChannelCell(cells[3].Groups[1].Value, "Monthly Enterprise Channel", fullReleaseDate, allReleases);
                    ProcessChannelCell(cells[4].Groups[1].Value, "Semi-Annual Enterprise Preview", fullReleaseDate, allReleases);
                    ProcessChannelCell(cells[5].Groups[1].Value, "Semi-Annual Enterprise Channel", fullReleaseDate, allReleases);
                }
            }

            // Sort all releases by release date (descending)
            allReleases = allReleases
                .OrderByDescending(r => DateTime.Parse(r.ReleaseDate))
                .ToList();
            
            // Use ISO 8601 format for timestamp for better JavaScript compatibility
            var timestamp = DateTime.UtcNow.ToString("o");
            
            // Create result data objects with the same metadata structure as the PowerShell script
            var metadata = new DataForNerds
            {
                LastUpdatedUTC = timestamp,
                SourcePage = new List<string> { _rootPage },
                TimeElapsedMs = (int)elapsedMs
            };
            
            var allReleasesData = new Office365VersionsData
            {
                DataForNerds = metadata,
                Data = allReleases
            };
            
            return allReleasesData;
        }

        private void ProcessChannelCell(string cellContent, string channelName, string releaseDate, List<Office365Version> releases)
        {
            var linkRegex = new Regex(@"<a(?:[^>])*href=""([^""]+)""[^>]*>(.*?)<\/a>");
            var versionBuildRegex = new Regex(@"(?msi)Version (.*?) \(Build {1,}(.*?)\)");
            
            var links = linkRegex.Matches(cellContent);
            
            foreach (Match link in links)
            {
                if (link.Groups.Count >= 3)
                {
                    var url = link.Groups[1].Value;
                    var linkText = link.Groups[2].Value;
                    var versionBuild = versionBuildRegex.Match(linkText);
                    
                    if (versionBuild.Success)
                    {
                        var version = versionBuild.Groups[1].Value;
                        var build = versionBuild.Groups[2].Value;
                        
                        // Clean up URL by removing any HTML entity encoding
                        url = url.Replace("&amp;", "&")
                                 .Replace("\\u0022", "\"")
                                 .Replace("\\u003C", "<")
                                 .Replace("\\u003E", ">");
                        
                        // Ensure URL is properly formatted
                        var fullUrl = !url.StartsWith("http") ? _rootUrl + url : url;
                        
                        releases.Add(new Office365Version
                        {
                            ReleaseDate = releaseDate,
                            Channel = channelName,
                            Build = build,
                            Version = version,
                            FullBuild = $"16.0.{build}",
                            Url = fullUrl
                        });
                    }
                }
            }
        }

        private string ParseDate(string dateString)
        {
            try
            {
                return DateTime.Parse(dateString).ToString("yyyy MMM dd");
            }
            catch
            {
                _logger.LogWarning("Failed to parse date: {DateString}", dateString);
                return dateString;
            }
        }
        
        private string ParseDateOrString(string dateString)
        {
            try
            {
                return DateTime.Parse(dateString).ToString("yyyy MMM dd");
            }
            catch
            {
                return dateString; // Return as-is if not a valid date
            }
        }

        private async Task UploadJsonDataAsync(Office365VersionsData data, string fileName, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Preparing to upload {FileName}", fileName);
                
                // Convert data to JSON - match PowerShell indentation and casing
                var jsonData = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                // Create temporary file
                var tempFilePath = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempFilePath, jsonData, cancellationToken);
                
                _logger.LogInformation("JSON data written to temporary file: {TempFile}", tempFilePath);
                
                try
                {
                    // Use the injected BlobServiceClient with managed identity or Azurite
                    var containerClient = _blobServiceClient.GetBlobContainerClient(_storageContainerName);
                    
                    // Ensure container exists - especially important for Azurite
                    await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                    
                    // Upload the file - Using 'using' statement to ensure FileStream is properly disposed
                    var blobClient = containerClient.GetBlobClient(fileName);
                    using (var fileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read))
                    {
                        await blobClient.UploadAsync(fileStream, overwrite: true, cancellationToken);
                    }
                    
                    if (_useAzurite)
                    {
                        _logger.LogInformation("Successfully uploaded {FileName} to Azurite local storage", fileName);
                    }
                    else
                    {
                        _logger.LogInformation("Successfully uploaded {FileName} to blob storage using managed identity", fileName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error uploading to blob storage. Trying to save locally as fallback.");
                    
                    // Write to a local file as fallback
                    var localFilePath = Path.Combine(Path.GetTempPath(), fileName);
                    await File.WriteAllTextAsync(localFilePath, jsonData, cancellationToken);
                    _logger.LogWarning("As a fallback, data has been written to local file: {LocalFilePath}", localFilePath);
                }
                finally
                {
                    try
                    {
                        // Clean up temporary file
                        File.Delete(tempFilePath);
                        _logger.LogInformation("Deleted temporary file: {TempFile}", tempFilePath);
                    }
                    catch (IOException ioEx)
                    {
                        _logger.LogWarning(ioEx, "Could not delete temporary file immediately: {TempFile}. File will be deleted later by the OS.", tempFilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in JSON upload process for {FileName}", fileName);
                throw;
            }
        }
    }
}