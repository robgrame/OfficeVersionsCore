using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeVersionsCore.Models;

namespace OfficeVersionsCore.Services
{
    /// <summary>
    /// Interface for Windows versions service
    /// </summary>
    public interface IWindowsVersionsService
    {
        Task<ApiResponse<List<WindowsVersion>>> GetWindowsVersionsAsync(WindowsEdition edition);
        Task<ApiResponse<List<WindowsUpdate>>> GetWindowsUpdatesAsync(WindowsEdition edition);
        Task<ApiResponse<WindowsReleaseSummary>> GetReleaseSummaryAsync(WindowsEdition edition);
        Task<ApiResponse<WindowsVersion?>> GetLatestVersionAsync(WindowsEdition edition);
        Task<ApiResponse<List<WindowsUpdate>>> GetRecentUpdatesAsync(WindowsEdition edition, int count = 10);
        Task<ApiResponse<List<WindowsFeatureUpdate>>> GetFeatureUpdatesAsync(WindowsEdition edition);
        Task<ApiResponse<VersionComparison>> CompareVersionsAsync(string version1, string version2);
        Task<bool> RefreshDataAsync();
        Task<DateTime?> GetLastUpdateTimeAsync();
        string GenerateProductName(WindowsEdition edition, string version, ServicingType servicingType);
    }

    /// <summary>
    /// Service for tracking Windows versions and updates
    /// </summary>
    public class WindowsVersionsService : IWindowsVersionsService
    {
        private readonly HttpClient _httpClient;
        private readonly IStorageService _storageService;
        private readonly ILogger<WindowsVersionsService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _windowsStoragePath;

        // Microsoft documentation URLs
        private const string Windows10UpdateHistoryUrl = "https://support.microsoft.com/en-us/topic/windows-10-update-history-8127c2c6-6edf-4fdf-8b9f-0f7be1ef3562";
        private const string Windows11UpdateHistoryUrl = "https://support.microsoft.com/en-us/topic/windows-11-version-24h2-update-history-0929c747-1815-4543-8461-0160d16f15e5";
        private const string Windows10ReleaseInfoUrl = "https://learn.microsoft.com/en-us/windows/release-health/release-information";
        private const string Windows11ReleaseInfoUrl = "https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information";

        public WindowsVersionsService(
            HttpClient httpClient,
            IStorageService storageService,
            ILogger<WindowsVersionsService> logger,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _storageService = storageService;
            _logger = logger;
            _configuration = configuration;

            // Storage path from configuration (WindowsVersions:StoragePath), default to "windowsversions"
            _windowsStoragePath = _configuration["WindowsVersions:StoragePath"] ?? "windowsversions";

            // Ensure constructor sets a UA header to avoid blocked requests
            try
            {
                if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
                {
                    _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
                }
            }
            catch { }
        }

        public async Task<ApiResponse<List<WindowsVersion>>> GetWindowsVersionsAsync(WindowsEdition edition)
        {
            try
            {
                _logger.LogInformation("Getting Windows versions for {Edition}", edition);

                var versions = await LoadVersionsFromStorageAsync(edition);
                if (versions?.Any() != true)
                {
                    _logger.LogWarning("No versions found in storage for {Edition}, attempting to scrape", edition);
                    await RefreshDataAsync();
                    versions = await LoadVersionsFromStorageAsync(edition);
                }

                return new ApiResponse<List<WindowsVersion>>
                {
                    Success = true,
                    Data = versions ?? new List<WindowsVersion>(),
                    Message = $"Retrieved {versions?.Count ?? 0} versions for {edition}",
                    Source = "Azure Storage"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Windows versions for {Edition}", edition);
                return new ApiResponse<List<WindowsVersion>>
                {
                    Success = false,
                    Message = $"Error retrieving Windows versions: {ex.Message}",
                    Data = new List<WindowsVersion>()
                };
            }
        }

        public async Task<ApiResponse<List<WindowsUpdate>>> GetWindowsUpdatesAsync(WindowsEdition edition)
        {
            try
            {
                _logger.LogInformation("Getting Windows updates for {Edition}", edition);

                var updates = await LoadUpdatesFromStorageAsync(edition);
                if (updates?.Any() != true)
                {
                    _logger.LogWarning("No updates found in storage for {Edition}, attempting to scrape", edition);
                    await RefreshDataAsync();
                    updates = await LoadUpdatesFromStorageAsync(edition);
                }

                return new ApiResponse<List<WindowsUpdate>>
                {
                    Success = true,
                    Data = updates ?? new List<WindowsUpdate>(),
                    Message = $"Retrieved {updates?.Count ?? 0} updates for {edition}",
                    Source = "Azure Storage"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Windows updates for {Edition}", edition);
                return new ApiResponse<List<WindowsUpdate>>
                {
                    Success = false,
                    Message = $"Error retrieving Windows updates: {ex.Message}",
                    Data = new List<WindowsUpdate>()
                };
            }
        }

        public async Task<ApiResponse<WindowsReleaseSummary>> GetReleaseSummaryAsync(WindowsEdition edition)
        {
            try
            {
                var releaseVersions = await LoadReleaseVersionsFromStorageAsync(edition);
                if (releaseVersions == null)
                {
                    // Fallback to legacy flat list then map
                    var versionsResponse = await GetWindowsVersionsAsync(edition);
                    var updatesResponse = await GetWindowsUpdatesAsync(edition);

                    if (!versionsResponse.Success || !updatesResponse.Success)
                    {
                        return new ApiResponse<WindowsReleaseSummary>
                        {
                            Success = false,
                            Message = "Failed to retrieve version or update data",
                            Data = null
                        };
                    }

                    var versions = versionsResponse.Data ?? new List<WindowsVersion>();
                    var updates = updatesResponse.Data ?? new List<WindowsUpdate>();

                    var summaryLegacy = BuildSummaryFromFlatVersions(edition, versions, updates);
                    return new ApiResponse<WindowsReleaseSummary>
                    {
                        Success = true,
                        Data = summaryLegacy,
                        Message = $"Generated summary for {edition}",
                        Source = "Computed"
                    };
                }

                // Load updates
                var updatesResp = await GetWindowsUpdatesAsync(edition);
                var updatesList = updatesResp.Success ? (updatesResp.Data ?? new List<WindowsUpdate>()) : new List<WindowsUpdate>();

                // Flatten to compute recent and stats
                var flatVersions = FlattenReleaseVersions(releaseVersions);

                var summary = new WindowsReleaseSummary
                {
                    Edition = edition,
                    CurrentVersion = flatVersions.Where(v => v.IsCurrentVersion).FirstOrDefault()?.Version ?? "Unknown",
                    LatestBuild = flatVersions.OrderByDescending(v => v.ReleaseDate).FirstOrDefault()?.Build ?? "Unknown",
                    LastUpdateDate = updatesList.Max(u => u.ReleaseDate),
                    TotalUpdates = updatesList.Count,
                    SecurityUpdates = updatesList.Count(u => u.IsSecurityUpdate),
                    RecentVersions = flatVersions.OrderByDescending(v => v.ReleaseDate).Take(5).ToList(),
                    RecentUpdates = updatesList.OrderByDescending(u => u.ReleaseDate).Take(10).ToList(),
                    RegularVersions = releaseVersions.RegularVersions,
                    LtscVersions = releaseVersions.LtscVersions
                };

                return new ApiResponse<WindowsReleaseSummary>
                {
                    Success = true,
                    Data = summary,
                    Message = $"Generated summary for {edition}",
                    Source = "Computed"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating release summary for {Edition}", edition);
                return new ApiResponse<WindowsReleaseSummary>
                {
                    Success = false,
                    Message = $"Error generating release summary: {ex.Message}",
                    Data = null
                };
            }
        }

        private WindowsReleaseSummary BuildSummaryFromFlatVersions(WindowsEdition edition, List<WindowsVersion> versions, List<WindowsUpdate> updates)
        {
            var regularVersions = versions
                .Where(v => v.ServicingType == ServicingType.Regular)
                .OrderByDescending(v => v.ReleaseDate)
                .Select(v => new RegularServicingVersion
                {
                    Version = v.Version,
                    ServicingOption = v.ServiceOption,
                    AvailabilityDate = v.Availability,
                    ReleaseDate = v.ReleaseDate,
                    EndOfServicingConsumer = v.EndOfServicingStandard ?? string.Empty,
                    EndOfServicingEnterprise = v.EndOfServicingEnterprise ?? string.Empty,
                    LatestUpdate = v.LatestUpdate ?? string.Empty,
                    LatestRevisionDate = v.LatestRevisionDate ?? string.Empty,
                    LatestBuild = v.Build,
                    Edition = v.Edition,
                    IsCurrentVersion = v.IsCurrentVersion
                })
                .ToList();

            var ltscVersions = versions
                .Where(v => v.ServicingType == ServicingType.LTSC || v.ServicingType == ServicingType.LTSB)
                .OrderByDescending(v => v.ReleaseDate)
                .Select(v => new LtscServicingVersion
                {
                    Version = v.Version,
                    ServicingOption = v.ServiceOption,
                    AvailabilityDate = v.Availability,
                    ReleaseDate = v.ReleaseDate,
                    MainstreamSupportEndDate = v.MainstreamSupportEndDate ?? string.Empty,
                    ExtendedSupportEndDate = v.ExtendedSupportEndDate ?? string.Empty,
                    LatestUpdate = v.LatestUpdate ?? string.Empty,
                    LatestRevisionDate = v.LatestRevisionDate ?? string.Empty,
                    LatestBuild = v.Build,
                    Edition = v.Edition,
                    IsCurrentVersion = v.IsCurrentVersion
                })
                .ToList();

            return new WindowsReleaseSummary
            {
                Edition = edition,
                CurrentVersion = versions.Where(v => v.IsCurrentVersion).FirstOrDefault()?.Version ?? "Unknown",
                LatestBuild = versions.OrderByDescending(v => v.ReleaseDate).FirstOrDefault()?.Build ?? "Unknown",
                LastUpdateDate = updates.Max(u => u.ReleaseDate),
                TotalUpdates = updates.Count,
                SecurityUpdates = updates.Count(u => u.IsSecurityUpdate),
                RecentVersions = versions.OrderByDescending(v => v.ReleaseDate).Take(5).ToList(),
                RecentUpdates = updates.OrderByDescending(u => u.ReleaseDate).Take(10).ToList(),
                RegularVersions = regularVersions,
                LtscVersions = ltscVersions
            };
        }

        public async Task<ApiResponse<WindowsVersion?>> GetLatestVersionAsync(WindowsEdition edition)
        {
            try
            {
                var versionsResponse = await GetWindowsVersionsAsync(edition);
                if (!versionsResponse.Success)
                {
                    return new ApiResponse<WindowsVersion?>
                    {
                        Success = false,
                        Message = versionsResponse.Message,
                        Data = null
                    };
                }

                var latestVersion = versionsResponse.Data?
                    .OrderByDescending(v => v.ReleaseDate)
                    .FirstOrDefault();

                return new ApiResponse<WindowsVersion?>
                {
                    Success = true,
                    Data = latestVersion,
                    Message = latestVersion != null ? $"Latest version: {latestVersion.Version}" : "No versions found",
                    Source = "Azure Storage"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting latest version for {Edition}", edition);
                return new ApiResponse<WindowsVersion?>
                {
                    Success = false,
                    Message = $"Error retrieving latest version: {ex.Message}",
                    Data = null
                };
            }
        }

        public async Task<ApiResponse<List<WindowsUpdate>>> GetRecentUpdatesAsync(WindowsEdition edition, int count = 10)
        {
            try
            {
                var updatesResponse = await GetWindowsUpdatesAsync(edition);
                if (!updatesResponse.Success)
                {
                    return updatesResponse;
                }

                var recentUpdates = updatesResponse.Data?
                    .OrderByDescending(u => u.ReleaseDate)
                    .Take(count)
                    .ToList() ?? new List<WindowsUpdate>();

                return new ApiResponse<List<WindowsUpdate>>
                {
                    Success = true,
                    Data = recentUpdates,
                    Message = $"Retrieved {recentUpdates.Count} recent updates for {edition}",
                    Source = "Azure Storage"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent updates for {Edition}", edition);
                return new ApiResponse<List<WindowsUpdate>>
                {
                    Success = false,
                    Message = $"Error retrieving recent updates: {ex.Message}",
                    Data = new List<WindowsUpdate>()
                };
            }
        }

        public async Task<ApiResponse<List<WindowsFeatureUpdate>>> GetFeatureUpdatesAsync(WindowsEdition edition)
        {
            try
            {
                var featureUpdates = await LoadFeatureUpdatesFromStorageAsync(edition);
                
                return new ApiResponse<List<WindowsFeatureUpdate>>
                {
                    Success = true,
                    Data = featureUpdates ?? new List<WindowsFeatureUpdate>(),
                    Message = $"Retrieved {featureUpdates?.Count ?? 0} feature updates for {edition}",
                    Source = "Azure Storage"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting feature updates for {Edition}", edition);
                return new ApiResponse<List<WindowsFeatureUpdate>>
                {
                    Success = false,
                    Message = $"Error retrieving feature updates: {ex.Message}",
                    Data = new List<WindowsFeatureUpdate>()
                };
            }
        }

        public async Task<ApiResponse<VersionComparison>> CompareVersionsAsync(string version1, string version2)
        {
            try
            {
                var comparison = new VersionComparison
                {
                    Version1 = version1,
                    Version2 = version2
                };

                // Simple version comparison logic
                if (Version.TryParse(version1, out var v1) && Version.TryParse(version2, out var v2))
                {
                    comparison.Result = v1.CompareTo(v2);
                    comparison.Description = comparison.Result switch
                    {
                        -1 => $"{version1} is older than {version2}",
                        0 => $"{version1} is the same as {version2}",
                        1 => $"{version1} is newer than {version2}",
                        _ => "Unable to compare versions"
                    };
                }
                else
                {
                    comparison.Result = string.Compare(version1, version2, StringComparison.OrdinalIgnoreCase);
                    comparison.Description = "Versions compared as strings";
                }

                return await Task.FromResult(new ApiResponse<VersionComparison>
                {
                    Success = true,
                    Data = comparison,
                    Message = "Version comparison completed",
                    Source = "Computed"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error comparing versions {Version1} and {Version2}", version1, version2);
                return new ApiResponse<VersionComparison>
                {
                    Success = false,
                    Message = $"Error comparing versions: {ex.Message}",
                    Data = null
                };
            }
        }

        public async Task<bool> RefreshDataAsync()
        {
            try
            {
                _logger.LogInformation("Starting Windows data refresh");

                var windows10Success = await ScrapeWindowsDataAsync(WindowsEdition.Windows10);
                var windows11Success = await ScrapeWindowsDataAsync(WindowsEdition.Windows11);

                var success = windows10Success && windows11Success;
                _logger.LogInformation("Windows data refresh completed. Success: {Success}", success);

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing Windows data");
                return false;
            }
        }

        public async Task<DateTime?> GetLastUpdateTimeAsync()
        {
            try
            {
                var fileName = $"{_windowsStoragePath}/last-update.json";
                if (await _storageService.ExistsAsync(fileName))
                {
                    var json = await _storageService.ReadAsync(fileName);
                    var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                    
                    if (metadata?.TryGetValue("lastUpdate", out var lastUpdateObj) == true)
                    {
                        if (DateTime.TryParse(lastUpdateObj?.ToString(), out var lastUpdate))
                        {
                            return lastUpdate;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting last update time");
            }

            return null;
        }

        /// <summary>
        /// Generates a complete product name with servicing type information
        /// </summary>
        /// <param name="edition">Windows edition (10 or 11)</param>
        /// <param name="version">Version number (e.g., "2021", "22H2")</param>
        /// <param name="servicingType">The servicing type (Regular, LTSC, LTSB)</param>
        /// <returns>Formatted product name (e.g., "Windows 10 Enterprise LTSC 2021")</returns>
        public string GenerateProductName(WindowsEdition edition, string version, ServicingType servicingType)
        {
            return ServicingTypeNames.GetFullProductName(edition, version, servicingType);
        }

        private async Task<bool> ScrapeWindowsDataAsync(WindowsEdition edition)
        {
            try
            {
                _logger.LogInformation("Scraping Windows data for {Edition}", edition);

                var updateHistoryUrl = edition == WindowsEdition.Windows10 ? Windows10UpdateHistoryUrl : Windows11UpdateHistoryUrl;
                var releaseInfoUrl = edition == WindowsEdition.Windows10 ? Windows10ReleaseInfoUrl : Windows11ReleaseInfoUrl;

                var updates = await ScrapeUpdateHistoryAsync(updateHistoryUrl, edition);
                updates = UpdateDataCleaner.CleanWindowsUpdates(updates);

                var recentUpdates = updates
                    .OrderByDescending(u => u.ReleaseDate)
                    .Take(10)
                    .ToList();

                for (int i = 0; i < recentUpdates.Count; i++)
                {
                    if (i > 0) await Task.Delay(1000);
                    var updatedItem = await EnrichUpdateFromDetailPageAsync(recentUpdates[i]);
                    int indexInMainCollection = updates.FindIndex(u => u.KBNumber == updatedItem.KBNumber);
                    if (indexInMainCollection >= 0) updates[indexInMainCollection] = updatedItem;
                }

                var versions = await ScrapeReleaseInformationAsync(releaseInfoUrl, edition);

                // Map to channel-specific payload for storage
                var grouped = new WindowsReleaseVersions
                {
                    Edition = edition,
                    RegularVersions = versions
                        .Where(v => v.ServicingType == ServicingType.Regular)
                        .Select(v => new RegularServicingVersion
                        {
                            Version = v.Version,
                            ServicingOption = v.ServiceOption,
                            AvailabilityDate = v.Availability,
                            ReleaseDate = v.ReleaseDate,
                            EndOfServicingConsumer = v.EndOfServicingStandard ?? string.Empty,
                            EndOfServicingEnterprise = v.EndOfServicingEnterprise ?? string.Empty,
                            LatestUpdate = v.LatestUpdate ?? string.Empty,
                            LatestRevisionDate = v.LatestRevisionDate ?? string.Empty,
                            LatestBuild = v.Build,
                            Edition = v.Edition,
                            IsCurrentVersion = v.IsCurrentVersion
                        })
                        .OrderByDescending(v => v.ReleaseDate)
                        .ToList(),
                    LtscVersions = versions
                        .Where(v => v.ServicingType == ServicingType.LTSC || v.ServicingType == ServicingType.LTSB)
                        .Select(v => new LtscServicingVersion
                        {
                            Version = v.Version,
                            ServicingOption = v.ServiceOption,
                            AvailabilityDate = v.Availability,
                            ReleaseDate = v.ReleaseDate,
                            MainstreamSupportEndDate = v.MainstreamSupportEndDate ?? string.Empty,
                            ExtendedSupportEndDate = v.ExtendedSupportEndDate ?? string.Empty,
                            LatestUpdate = v.LatestUpdate ?? string.Empty,
                            LatestRevisionDate = v.LatestRevisionDate ?? string.Empty,
                            LatestBuild = v.Build,
                            Edition = v.Edition,
                            IsCurrentVersion = v.IsCurrentVersion
                        })
                        .OrderByDescending(v => v.ReleaseDate)
                        .ToList()
                };

                await SaveUpdatesToStorageAsync(edition, updates);
                await SaveReleaseVersionsToStorageAsync(edition, grouped);

                _logger.LogInformation("Successfully scraped {UpdateCount} updates and {VersionCount} versions for {Edition}", 
                    updates.Count, versions.Count, edition);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping Windows data for {Edition}", edition);
                return false;
            }
        }

        private async Task<List<WindowsUpdate>> ScrapeUpdateHistoryAsync(string url, WindowsEdition edition)
        {
            var updates = new List<WindowsUpdate>();

            try
            {
                _logger.LogInformation("Starting to scrape update history from {Url}", url);
                var html = await _httpClient.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var versionSections = FindVersionSections(doc);
                
                if (versionSections.Any())
                {
                    foreach (var sectionInfo in versionSections)
                    {
                        var sectionUpdates = ExtractUpdatesFromSection(sectionInfo.Section, sectionInfo.Version, edition);
                        updates.AddRange(sectionUpdates);
                    }
                }
                else
                {
                    updates.AddRange(ExtractUpdatesFromTables(doc, edition));
                    if (!updates.Any()) updates.AddRange(ExtractUpdatesFromContent(doc, edition));
                }

                _logger.LogInformation("Extracted {Count} updates from {Url}", updates.Count, url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping update history from {Url}", url);
            }

            return updates;
        }

        private List<(HtmlNode Section, string Version)> FindVersionSections(HtmlDocument doc)
        {
            var result = new List<(HtmlNode, string)>();
            try
            {
                var versionCategories = doc.DocumentNode.SelectNodes("//div[contains(@class, 'supLeftNavCategoryTitle')]");
                
                if (versionCategories != null)
                {
                    foreach (var categoryTitleDiv in versionCategories)
                    {
                        var versionLink = categoryTitleDiv.SelectSingleNode(".//a[@class='supLeftNavLink']");
                        if (versionLink != null)
                        {
                            string versionTitle = ExtractText(versionLink);
                            string version = ExtractVersionFromHeader(versionTitle);
                            var categoryContainer = categoryTitleDiv.ParentNode;
                            if (categoryContainer != null)
                            {
                                var updatesList = categoryContainer.SelectSingleNode(".//ul[contains(@class, 'supLeftNavArticles')]");
                                if (updatesList != null)
                                {
                                    result.Add((updatesList, version));
                                }
                            }
                        }
                    }
                }

                if (result.Count == 0)
                {
                    // Try to find the entire left navigation section
                    var leftNavSection = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'supLeftNav')]") ??
                                         doc.DocumentNode.SelectSingleNode("//nav");
                    
                    if (leftNavSection != null)
                    {
                        // Find all update article lists
                        var articleLists = leftNavSection.SelectNodes(".//ul[contains(@class, 'supLeftNavArticles')]");
                        
                        if (articleLists != null)
                        {
                            foreach (var articleList in articleLists)
                            {
                                // Try to find the associated category title
                                var categoryTitle = articleList.PreviousSibling;
                                while (categoryTitle != null && !categoryTitle.InnerText.Contains("version", StringComparison.OrdinalIgnoreCase))
                                {
                                    categoryTitle = categoryTitle.PreviousSibling;
                                }
                                
                                string version = string.Empty;
                                if (categoryTitle != null)
                                {
                                    version = ExtractVersionFromHeader(ExtractText(categoryTitle));
                                }
                                
                                result.Add((articleList, version));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding version sections");
            }

            return result;
        }

        private List<WindowsUpdate> ExtractUpdatesFromSection(HtmlNode section, string version, WindowsEdition edition)
        {
            var updates = new List<WindowsUpdate>();
            try
            {
                if (section.Name == "ul" && section.GetAttributeValue("class", string.Empty).Contains("supLeftNavArticles"))
                {
                    updates.AddRange(ExtractUpdatesFromNavLinks(section, version, edition));
                    if (updates.Any()) return updates;
                }

                var tables = section.SelectNodes(".//table");
                if (tables != null)
                {
                    foreach (var table in tables)
                    {
                        updates.AddRange(ExtractUpdatesFromTable(table, version, edition));
                    }
                }

                if (tables == null || !updates.Any())
                {
                    var listItems = section.SelectNodes(".//ul/li") ?? section.SelectNodes(".//ol/li");
                    if (listItems != null)
                    {
                        foreach (var item in listItems)
                        {
                            var update = ExtractUpdateFromListItem(item, version, edition);
                            if (update != null) updates.Add(update);
                        }
                    }
                }

                if (!updates.Any())
                {
                    var paragraphs = section.SelectNodes(".//p");
                    if (paragraphs != null)
                    {
                        foreach (var p in paragraphs)
                        {
                            var text = ExtractText(p);
                            if (text.Contains("KB", StringComparison.OrdinalIgnoreCase))
                            {
                                updates.Add(new WindowsUpdate
                                {
                                    Edition = edition,
                                    Version = version,
                                    KBNumber = ExtractKBNumber(text),
                                    UpdateTitle = text,
                                    ReleaseDate = ExtractDateFromText(text),
                                    IsSecurityUpdate = text.Contains("security", StringComparison.OrdinalIgnoreCase)
                                });
                            }
                        }
                    }
                }

                var headings = section.SelectNodes(".//h4 | .//h5 | .//h6");
                if (headings != null)
                {
                    foreach (var heading in headings)
                    {
                        var text = ExtractText(heading);
                        if (text.Contains("KB", StringComparison.OrdinalIgnoreCase))
                        {
                            var builds = new List<string>();
                            var multipleBuildsMatch = Regex.Match(text, @"OS Builds\s+([\d\.]+)\s+and\s+([\d\.]+)", RegexOptions.IgnoreCase);
                            if (multipleBuildsMatch.Success && multipleBuildsMatch.Groups.Count > 2)
                            {
                                builds.Add(multipleBuildsMatch.Groups[1].Value);
                                builds.Add(multipleBuildsMatch.Groups[2].Value);
                            }
                            else
                            {
                                var buildMatch = Regex.Match(text, @"(?:OS )?[Bb]uild\s+([\d\.]+)", RegexOptions.IgnoreCase);
                                if (buildMatch.Success && buildMatch.Groups.Count > 1) builds.Add(buildMatch.Groups[1].Value);
                            }

                            var description = string.Empty;
                            var nextNode = heading.NextSibling;
                            while (nextNode != null && nextNode.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
                            {
                                description += ExtractText(nextNode) + " ";
                                nextNode = nextNode.NextSibling;
                            }

                            if (builds.Count > 1)
                            {
                                foreach (var build in builds)
                                {
                                    string buildVersion = DetermineVersionFromBuild(build);
                                    if (string.IsNullOrEmpty(buildVersion)) buildVersion = version;
                                    var update = new WindowsUpdate
                                    {
                                        Edition = edition,
                                        Version = buildVersion,
                                        Build = build,
                                        KBNumber = ExtractKBNumber(text),
                                        UpdateTitle = text,
                                        ReleaseDate = ExtractDateFromText(text),
                                        IsSecurityUpdate = text.Contains("security", StringComparison.OrdinalIgnoreCase)
                                    };
                                    if (!string.IsNullOrWhiteSpace(description)) update.Description = description.Trim();
                                    updates.Add(update);
                                }
                            }
                            else
                            {
                                var update = new WindowsUpdate
                                {
                                    Edition = edition,
                                    Version = version,
                                    Build = builds.FirstOrDefault() ?? string.Empty,
                                    KBNumber = ExtractKBNumber(text),
                                    UpdateTitle = text,
                                    ReleaseDate = ExtractDateFromText(text),
                                    IsSecurityUpdate = text.Contains("security", StringComparison.OrdinalIgnoreCase)
                                };
                                if (!string.IsNullOrWhiteSpace(description)) update.Description = description.Trim();
                                updates.Add(update);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting updates from section");
            }
            return updates;
        }

        private List<WindowsUpdate> ExtractUpdatesFromTables(HtmlDocument doc, WindowsEdition edition)
        {
            var updates = new List<WindowsUpdate>();
            try
            {
                var tables = doc.DocumentNode.SelectNodes("//table");
                if (tables != null)
                {
                    foreach (var table in tables)
                    {
                        updates.AddRange(ExtractUpdatesFromTable(table, string.Empty, edition));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting updates from tables");
            }
            return updates;
        }

        private List<WindowsUpdate> ExtractUpdatesFromTable(HtmlNode table, string version, WindowsEdition edition)
        {
            var updates = new List<WindowsUpdate>();
            try
            {
                var rows = table.SelectNodes(".//tr");
                if (rows == null || rows.Count == 0) return updates;

                bool hasHeader = rows[0].SelectNodes(".//th") != null;
                for (int i = hasHeader ? 1 : 0; i < rows.Count; i++)
                {
                    var cells = rows[i].SelectNodes(".//td");
                    if (cells == null || cells.Count < 2) continue;

                    if (cells.Count >= 3)
                    {
                        string kbOrTitle = ExtractText(cells[0]);
                        string buildText = ExtractText(cells[1]);
                        string dateText = ExtractText(cells[2]);

                        string kbNumber = ExtractKBNumber(kbOrTitle);
                        DateTime? releaseDate = ParseDate(dateText);

                        var builds = new List<string>();
                        var multipleBuildsMatch = Regex.Match(buildText, @"([\d\.]+)\s+and\s+([\d\.]+)", RegexOptions.IgnoreCase);
                        if (multipleBuildsMatch.Success && multipleBuildsMatch.Groups.Count > 2)
                        {
                            builds.Add(multipleBuildsMatch.Groups[1].Value);
                            builds.Add(multipleBuildsMatch.Groups[2].Value);
                        }
                        else if (Regex.IsMatch(buildText, @"^\d+(\.\d+)*$"))
                        {
                            builds.Add(buildText);
                        }

                        if (builds.Count > 0)
                        {
                            foreach (var build in builds)
                            {
                                string buildVersion = !string.IsNullOrEmpty(version) ? version : DetermineVersionFromBuild(build);
                                updates.Add(new WindowsUpdate
                                {
                                    Edition = edition,
                                    Version = buildVersion,
                                    Build = build,
                                    KBNumber = kbNumber,
                                    UpdateTitle = kbOrTitle,
                                    ReleaseDate = releaseDate,
                                    IsSecurityUpdate = kbOrTitle.Contains("security", StringComparison.OrdinalIgnoreCase)
                                });
                            }
                        }
                        else
                        {
                            updates.Add(new WindowsUpdate
                            {
                                Edition = edition,
                                Version = version,
                                KBNumber = kbNumber,
                                UpdateTitle = kbOrTitle,
                                ReleaseDate = releaseDate,
                                IsSecurityUpdate = kbOrTitle.Contains("security", StringComparison.OrdinalIgnoreCase)
                            });
                        }
                    }
                    else if (cells.Count == 2)
                    {
                        // Format: KB/Title | Date or Build/Date
                        string firstCol = ExtractText(cells[0]);
                        string secondCol = ExtractText(cells[1]);
                        
                        // Determine which is title and which is date/build
                        string title;
                        string dateOrBuild;
                        DateTime? colDate = null;
                        
                        if (TryParseDate(secondCol, out var parsedDate))
                        {
                            title = firstCol;
                            dateOrBuild = secondCol;
                            colDate = parsedDate;
                        }
                        else if (TryParseDate(firstCol, out parsedDate))
                        {
                            title = secondCol;
                            dateOrBuild = firstCol;
                            colDate = parsedDate;
                        }
                        else
                        {
                            title = firstCol;
                            dateOrBuild = secondCol;
                        }
                        
                        string colKbNumber = ExtractKBNumber(title);
                        
                        // Try to extract build numbers
                        var colBuilds = new List<string>();
                        
                        // Check if second column has build numbers
                        var colMultiBuildsMatch = Regex.Match(dateOrBuild, @"([\d\.]+)\s+and\s+([\d\.]+)", RegexOptions.IgnoreCase);
                        if (colMultiBuildsMatch.Success && colMultiBuildsMatch.Groups.Count > 2)
                        {
                            colBuilds.Add(colMultiBuildsMatch.Groups[1].Value);
                            colBuilds.Add(colMultiBuildsMatch.Groups[2].Value);
                        }
                        else if (Regex.IsMatch(dateOrBuild, @"^\d+(\.\d+)*$"))
                        {
                            // Single build number
                            colBuilds.Add(dateOrBuild);
                        }
                        else
                        {
                            // Check title for build information
                            colMultiBuildsMatch = Regex.Match(title, @"OS Builds\s+([\d\.]+)\s+and\s+([\d\.]+)", RegexOptions.IgnoreCase);
                            if (colMultiBuildsMatch.Success && colMultiBuildsMatch.Groups.Count > 2)
                            {
                                colBuilds.Add(colMultiBuildsMatch.Groups[1].Value);
                                colBuilds.Add(colMultiBuildsMatch.Groups[2].Value);
                            }
                            else
                            {
                                var singleBuildMatch = Regex.Match(title, @"(?:OS )?[Bb]uild\s+([\d\.]+)", RegexOptions.IgnoreCase);
                                if (singleBuildMatch.Success && singleBuildMatch.Groups.Count > 1)
                                {
                                    colBuilds.Add(singleBuildMatch.Groups[1].Value);
                                }
                            }
                        }
                        
                        // Create updates
                        if (colBuilds.Count > 0)
                        {
                            foreach (var build in colBuilds)
                            {
                                string buildVersion = !string.IsNullOrEmpty(version) ? version : DetermineVersionFromBuild(build);
                                
                                updates.Add(new WindowsUpdate
                                {
                                    Edition = edition,
                                    Version = buildVersion,
                                    Build = build,
                                    KBNumber = colKbNumber,
                                    UpdateTitle = title,
                                    ReleaseDate = colDate,
                                    IsSecurityUpdate = title.Contains("security", StringComparison.OrdinalIgnoreCase)
                                });
                            }
                        }
                        else
                        {
                            // No build information
                            updates.Add(new WindowsUpdate
                            {
                                Edition = edition,
                                Version = version,
                                KBNumber = colKbNumber,
                                UpdateTitle = title,
                                ReleaseDate = colDate,
                                IsSecurityUpdate = title.Contains("security", StringComparison.OrdinalIgnoreCase)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting updates from table");
            }
            return updates;
        }

        private List<WindowsUpdate> ExtractUpdatesFromNavLinks(HtmlNode updatesList, string version, WindowsEdition edition)
        {
            var updates = new List<WindowsUpdate>();
            try
            {
                var updateLinks = updatesList.SelectNodes(".//li/a[@class='supLeftNavLink']");
                if (updateLinks != null)
                {
                    foreach (var updateLink in updateLinks)
                    {
                        string updateText = ExtractText(updateLink);
                        if (updateText.Contains("update history", StringComparison.OrdinalIgnoreCase)) continue;
                        string href = updateLink.GetAttributeValue("href", string.Empty);
                        string kbNumber = ExtractKBNumber(updateText);
                        DateTime? releaseDate = ExtractDateFromText(updateText);

                        List<string> builds = new();
                        var multipleBuildsMatch = Regex.Match(updateText, @"OS Builds\s+([\d\.]+)\s+and\s+([\d\.]+)", RegexOptions.IgnoreCase);
                        if (multipleBuildsMatch.Success && multipleBuildsMatch.Groups.Count > 2)
                        {
                            builds.Add(multipleBuildsMatch.Groups[1].Value);
                            builds.Add(multipleBuildsMatch.Groups[2].Value);
                        }
                        else
                        {
                            var singleBuildMatch = Regex.Match(updateText, @"OS Build\s+([\d\.]+)", RegexOptions.IgnoreCase);
                            if (singleBuildMatch.Success && singleBuildMatch.Groups.Count > 1) builds.Add(singleBuildMatch.Groups[1].Value);
                        }

                        if (builds.Count > 1)
                        {
                            foreach (var build in builds)
                            {
                                string buildVersion = DetermineVersionFromBuild(build);
                                if (string.IsNullOrEmpty(buildVersion)) buildVersion = version;
                                updates.Add(new WindowsUpdate
                                {
                                    Edition = edition,
                                    Version = buildVersion,
                                    Build = build,
                                    KBNumber = kbNumber,
                                    UpdateTitle = updateText,
                                    ReleaseDate = releaseDate,
                                    IsSecurityUpdate = updateText.Contains("security", StringComparison.OrdinalIgnoreCase),
                                    SourceUrl = href
                                });
                            }
                        }
                        else
                        {
                            updates.Add(new WindowsUpdate
                            {
                                Edition = edition,
                                Version = version,
                                Build = builds.FirstOrDefault() ?? string.Empty,
                                KBNumber = kbNumber,
                                UpdateTitle = updateText,
                                ReleaseDate = releaseDate,
                                IsSecurityUpdate = updateText.Contains("security", StringComparison.OrdinalIgnoreCase),
                                SourceUrl = href
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting updates from navigation links");
            }
            return updates;
        }

        private WindowsUpdate? ExtractUpdateFromListItem(HtmlNode item, string version, WindowsEdition edition)
        {
            try
            {
                var text = ExtractText(item);
                if (string.IsNullOrWhiteSpace(text) || text.Length < 10) return null;

                string kbNumber = ExtractKBNumber(text);
                if (string.IsNullOrEmpty(kbNumber) && !text.Contains("update", StringComparison.OrdinalIgnoreCase) && !text.Contains("build", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string build = string.Empty;
                var multipleBuildsMatch = Regex.Match(text, @"OS Builds\s+([\d\.]+)\s+and\s+([\d\.]+)", RegexOptions.IgnoreCase);
                if (multipleBuildsMatch.Success && multipleBuildsMatch.Groups.Count > 2)
                {
                    build = multipleBuildsMatch.Groups[1].Value;
                }
                else
                {
                    var buildMatch = Regex.Match(text, @"(?:OS )?[Bb]uild\s+([\d\.]+)", RegexOptions.IgnoreCase);
                    if (buildMatch.Success && buildMatch.Groups.Count > 1) build = buildMatch.Groups[1].Value;
                }

                var releaseDate = ExtractDateFromText(text);
                string updateVersion = version;
                if (string.IsNullOrEmpty(updateVersion) && !string.IsNullOrEmpty(build)) updateVersion = DetermineVersionFromBuild(build);
                if (string.IsNullOrEmpty(updateVersion))
                {
                    var versionMatch = Regex.Match(text, @"Windows 10,? version (\d{4}|\d{2}H\d)", RegexOptions.IgnoreCase);
                    if (versionMatch.Success && versionMatch.Groups.Count > 1) updateVersion = versionMatch.Groups[1].Value;
                }

                return new WindowsUpdate
                {
                    Edition = edition,
                    Version = updateVersion,
                    Build = build,
                    KBNumber = kbNumber,
                    UpdateTitle = text.Length > 200 ? text.Substring(0, 197) + "..." : text,
                    ReleaseDate = releaseDate,
                    IsSecurityUpdate = text.Contains("security", StringComparison.OrdinalIgnoreCase) || text.Contains("vulnerability", StringComparison.OrdinalIgnoreCase)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting update from list item");
                return null;
            }
        }

        private List<WindowsUpdate> ExtractUpdatesFromContent(HtmlDocument doc, WindowsEdition edition)
        {
            var updates = new List<WindowsUpdate>();
            try
            {
                var contentNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'content')] | //main | //div[@role='main']");
                if (contentNodes != null)
                {
                    foreach (var node in contentNodes)
                    {
                        var elements = node.SelectNodes(".//p | .//div | .//span");
                        if (elements != null)
                        {
                            foreach (var element in elements)
                            {
                                var text = ExtractText(element);
                                if (text.Contains("KB", StringComparison.OrdinalIgnoreCase))
                                {
                                    var kbNumber = ExtractKBNumber(text);
                                    if (!string.IsNullOrEmpty(kbNumber))
                                    {
                                        string version = string.Empty;
                                        var versionMatch = Regex.Match(text, @"Windows 10,? version (\d{4}|\d{2}H\d)", RegexOptions.IgnoreCase);
                                        if (versionMatch.Success && versionMatch.Groups.Count > 1) version = versionMatch.Groups[1].Value;

                                        string build = string.Empty;
                                        var buildMatch = Regex.Match(text, @"(OS )?Build (\d+\.\d+)", RegexOptions.IgnoreCase);
                                        if (buildMatch.Success && buildMatch.Groups.Count > 2) build = buildMatch.Groups[2].Value;

                                        updates.Add(new WindowsUpdate
                                        {
                                            Edition = edition,
                                            Version = version,
                                            Build = build,
                                            KBNumber = kbNumber,
                                            UpdateTitle = text.Length > 200 ? text.Substring(0, 197) + "..." : text,
                                            ReleaseDate = ExtractDateFromText(text),
                                            IsSecurityUpdate = text.Contains("security", StringComparison.OrdinalIgnoreCase)
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting updates from content");
            }
            return updates;
        }

        private string ExtractVersionFromHeader(string headerText)
        {
            if (string.IsNullOrEmpty(headerText)) return string.Empty;
            var versionMatch = Regex.Match(headerText, @"version\s+(\d\w+|\d+\.\d+(\.\d+)?)", RegexOptions.IgnoreCase);
            if (versionMatch.Success && versionMatch.Groups.Count > 1) return versionMatch.Groups[1].Value;
            if (headerText.Contains("20H2", StringComparison.OrdinalIgnoreCase)) return "20H2";
            if (headerText.Contains("21H1", StringComparison.OrdinalIgnoreCase)) return "21H1";
            if (headerText.Contains("21H2", StringComparison.OrdinalIgnoreCase)) return "21H2";
            if (headerText.Contains("22H2", StringComparison.OrdinalIgnoreCase)) return "22H2";
            return string.Empty;
        }

        private string DetermineVersionFromBuild(string build)
        {
            if (string.IsNullOrEmpty(build)) return string.Empty;
            string majorBuild = build.Split('.')[0];
            switch (majorBuild)
            {
                // Windows 11 build numbers
                case "26200": return "25H2"; // Windows 11 25H2
                case "22631": return "23H2"; // Windows 11 23H2
                case "22621": return "22H2"; // Windows 11 22H2
                case "22000": return "21H2"; // Windows 11 initial release
            
                // Windows 10 build numbers
                case "19045": return "22H2"; // Windows 10 22H2
                case "19044": return "21H2"; // Windows 10 21H2
                case "19043": return "21H1"; // Windows 10 21H1
                case "19042": return "20H2"; // Windows 10 20H2
                case "19041": return "2004"; // Windows 10 2004
                case "18363": return "1909"; // Windows 10 1909
                case "18362": return "1903"; // Windows 10 1903
                case "17763": return "1809"; // Windows 10 1809
                case "17134": return "1803"; // Windows 10 1803
                case "16299": return "1709"; // Windows 10 1709
                case "15063": return "1703"; // Windows 10 1703
                case "14393": return "1607"; // Windows 10 1607
                case "10586": return "1511"; // Windows 10 1511
                case "10240": return "1507"; // Windows 10 initial release
                default: return string.Empty;
            }
        }

        private DateTime? ExtractDateFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var monthNamePattern = @"(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},?\s+\d{4}";
            var match = Regex.Match(text, monthNamePattern, RegexOptions.IgnoreCase);
            if (match.Success && DateTime.TryParse(match.Value, out var date)) return date.Date;
            match = Regex.Match(text, @"\d{1,2}\s+(January|February|March|April|May|June|July|August|September|October|November)\s+\d{4}", RegexOptions.IgnoreCase);
            if (match.Success && DateTime.TryParse(match.Value, out date)) return date.Date;
            match = Regex.Match(text, @"\d{1,2}[./-]\d{1,2}[./-]\d{4}");
            if (match.Success && DateTime.TryParse(match.Value, out date)) return date.Date;
            return null;
        }

        private async Task<List<WindowsVersion>> ScrapeReleaseInformationAsync(string url, WindowsEdition edition)
        {
            var versions = new List<WindowsVersion>();
            try
            {
                _logger.LogInformation("Scraping release information from {Url} for {Edition}", url, edition);

                var html = await _httpClient.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Try aria-labels first (case-insensitive)
                var servicingChannelsTable = FindTableByAriaLabel(doc, new[] { "Servicing channels" });
                var ltscTable = FindTableByAriaLabel(doc, new[] { "Enterprise and IoT Enterprise LTSB/LTSC editions" });

                // Fallback: discover by headers if aria-label search fails
                if (servicingChannelsTable == null || ltscTable == null)
                {
                    var headerTables = FindTablesByHeader(doc, new[] { "Version", "Servicing" });
                    foreach (var t in headerTables)
                    {
                        if (servicingChannelsTable == null && !TableLooksLikeLTSC(t)) servicingChannelsTable = t;
                        if (ltscTable == null && TableLooksLikeLTSC(t)) ltscTable = t;
                        if (servicingChannelsTable != null && ltscTable != null) break;
                    }
                }

                if (servicingChannelsTable != null)
                {
                    versions.AddRange(ParseVersionsFromTable(servicingChannelsTable, edition, "Regular"));
                }
                else
                {
                    _logger.LogWarning("Servicing channels table not found for {Edition}", edition);
                }

                if (ltscTable != null)
                {
                    versions.AddRange(ParseVersionsFromTable(ltscTable, edition, "LTSC"));
                }
                else
                {
                    _logger.LogWarning("LTSC/LTSB editions table not found for {Edition}", edition);
                }

                // Last resort: parse any matching tables to avoid empty data
                if (!versions.Any())
                {
                    var anyTables = FindTablesByHeader(doc, new[] { "Version" });
                    foreach (var t in anyTables)
                    {
                        versions.AddRange(ParseVersionsFromTable(t, edition, TableLooksLikeLTSC(t) ? "LTSC" : "Regular"));
                    }
                }

                if (!versions.Any())
                {
                    _logger.LogWarning("No versions parsed from {Url} for {Edition}. Writing HTML snapshot for diagnostics.", url, edition);
                    try
                    {
                        var snapshotPath = Path.Combine(AppContext.BaseDirectory, $"{edition.ToString().ToLower()}-releaseinfo-snapshot.html");
                        await File.WriteAllTextAsync(snapshotPath, html);
                        _logger.LogInformation("Saved HTML snapshot to {Path}", snapshotPath);
                    }
                    catch { }
                }

                _logger.LogInformation("Parsed {Count} versions from {Url}", versions.Count, url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping release information from {Url}", url);
            }
            return versions;
        }

        private HtmlNode? FindTableByAriaLabel(HtmlDocument doc, IEnumerable<string> labels)
        {
            foreach (var label in labels)
            {
                // exact match
                var node = doc.DocumentNode.SelectSingleNode($"//table[@aria-label='{label}']");
                if (node != null) return node;
                // case-insensitive contains
                node = doc.DocumentNode.SelectSingleNode($"//table[contains(translate(@aria-label,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'), '{label.ToLower()}')]");
                if (node != null) return node;
            }
            return null;
        }

        private List<HtmlNode> FindTablesByHeader(HtmlDocument doc, IEnumerable<string> requiredHeaders)
        {
            var result = new List<HtmlNode>();
            var tables = doc.DocumentNode.SelectNodes("//table");
            if (tables == null) return result;

            foreach (var table in tables)
            {
                var ths = table.SelectNodes(".//thead//th") ?? table.SelectNodes(".//tr[1]//th");
                if (ths == null || ths.Count == 0) continue;
                var headers = ths.Select(h => ExtractText(h)).ToList();
                if (requiredHeaders.All(req => headers.Any(h => h.Contains(req, StringComparison.OrdinalIgnoreCase))))
                {
                    result.Add(table);
                }
            }
            return result;
        }

        private bool TableLooksLikeLTSC(HtmlNode table)
        {
            string text = ExtractText(table).ToLowerInvariant();
            if (text.Contains("ltsc") || text.Contains("ltsb") || text.Contains("long-term")) return true;
            return false;
        }

        private List<WindowsVersion> ParseVersionsFromTable(HtmlNode table, WindowsEdition edition, string servicingChannel)
        {
            var versions = new List<WindowsVersion>();
            try
            {
                var headerCells = table.SelectNodes(".//thead/tr/th") ?? table.SelectNodes(".//tr[1]/th");
                var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var headerIndexByLower = new Dictionary<int, string>();
                if (headerCells != null)
                {
                    for (int i = 0; i < headerCells.Count; i++)
                    {
                        var header = ExtractText(headerCells[i]).Replace("\u00A0", " ").Trim();
                        columnIndex[header] = i;
                        headerIndexByLower[i] = header.ToLowerInvariant();
                    }
                }

                int? FindColIndexFunc(Func<string, bool> predicate)
                {
                    foreach (var kvp in headerIndexByLower)
                    {
                        if (predicate(kvp.Value)) return kvp.Key;
                    }
                    return null;
                }

                bool ContainsAll(string text, params string[] tokens)
                {
                    foreach (var t in tokens)
                    {
                        if (!text.Contains(t, StringComparison.OrdinalIgnoreCase)) return false;
                    }
                    return true;
                }

                var rows = table.SelectNodes(".//tbody/tr") ?? table.SelectNodes(".//tr[not(th)]");
                if (rows == null || rows.Count == 0)
                {
                    _logger.LogWarning("No rows found in table for servicing channel {Channel}", servicingChannel);
                    return versions;
                }

                // Pre-detect special columns by header content
                int? colEndConsumer = FindColIndexFunc(h => h.Contains("end of servicing") && (h.Contains("home") || h.Contains("pro") || h.Contains("workstations")));
                int? colEndEnterprise = FindColIndexFunc(h => h.Contains("end of servicing") && (h.Contains("enterprise") || h.Contains("education") || h.Contains("iot")));
                int? colLatestUpdate = FindColIndexFunc(h => h.Contains("latest update") || h.Contains("latest cumulative update") || h.Contains("latest security update") || h.Contains("latest update title"));
                int? colLatestRevisionDate = FindColIndexFunc(h => h.Contains("latest revision date") || h.Contains("revision date") || h.Contains("last updated") || h.Contains("last revision"));
                int? colMainstreamEnd = FindColIndexFunc(h => h.Contains("mainstream") && h.Contains("support") && (h.Contains("end") || h.Contains("date")));
                int? colExtendedEnd = FindColIndexFunc(h => h.Contains("extended") && h.Contains("support") && (h.Contains("end") || h.Contains("date")));
                int? colOsBuild = FindColIndexFunc(h => h.Contains("os build") || (h.Equals("build") || h.Contains("build")));

                foreach (var row in rows)
                {
                    var cells = row.SelectNodes(".//td");
                    if (cells == null || cells.Count == 0) continue;

                    try
                    {
                        string versionCellText = string.Empty;
                        string servicingOption = string.Empty;
                        string availabilityText = string.Empty;
                        string endOfServicing = string.Empty; // generic fallback

                        if (columnIndex.Count > 0)
                        {
                            versionCellText = GetCellValueByHeader(cells, columnIndex, new[] { "Version" });
                            servicingOption = GetCellValueByHeader(cells, columnIndex, new[] { "Servicing option", "Servicing Option", "Servicing channel", "Servicing Channel" });
                            availabilityText = GetCellValueByHeader(cells, columnIndex, new[] { "Availability date", "Availability", "Release date", "Start date", "Start Date" });
                            endOfServicing = GetCellValueByHeader(cells, columnIndex, new[] { "End of servicing", "End of service", "End of Servicing", "End of Service" });
                            // OS build via detected column if present
                        }
                        else
                        {
                            versionCellText = ExtractText(cells.ElementAtOrDefault(0));
                            servicingOption = ExtractText(cells.ElementAtOrDefault(1));
                            availabilityText = ExtractText(cells.ElementAtOrDefault(2));
                            endOfServicing = ExtractText(cells.ElementAtOrDefault(3));
                        }

                        // Extract values from special columns where available
                        string endConsumer = colEndConsumer.HasValue ? ExtractText(cells.ElementAtOrDefault(colEndConsumer.Value)) : string.Empty;
                        string endEnterprise = colEndEnterprise.HasValue ? ExtractText(cells.ElementAtOrDefault(colEndEnterprise.Value)) : string.Empty;
                        string latestUpdate = colLatestUpdate.HasValue ? ExtractText(cells.ElementAtOrDefault(colLatestUpdate.Value)) : string.Empty;
                        string latestRevisionDate = colLatestRevisionDate.HasValue ? ExtractText(cells.ElementAtOrDefault(colLatestRevisionDate.Value)) : string.Empty;
                        string mainstreamEnd = colMainstreamEnd.HasValue ? ExtractText(cells.ElementAtOrDefault(colMainstreamEnd.Value)) : string.Empty;
                        string extendedEnd = colExtendedEnd.HasValue ? ExtractText(cells.ElementAtOrDefault(colExtendedEnd.Value)) : string.Empty;
                        string buildText = colOsBuild.HasValue ? ExtractText(cells.ElementAtOrDefault(colOsBuild.Value)) : string.Empty;

                        string versionName = ExtractVersionName(versionCellText);
                        string build = !string.IsNullOrWhiteSpace(buildText) ? ExtractBuildFromVersionText(buildText) : ExtractBuildFromVersionText(versionCellText);

                        DateTime? availabilityDate = ParseDate(availabilityText) ?? ExtractDateFromText(availabilityText);
                        DateTime? endOfServicingDate = ParseDate(endOfServicing) ?? ExtractDateFromText(endOfServicing);

                        var servicingType = servicingChannel.Equals("LTSC", StringComparison.OrdinalIgnoreCase) || servicingOption.Contains("LTSC", StringComparison.OrdinalIgnoreCase) || servicingOption.Contains("LTSB", StringComparison.OrdinalIgnoreCase)
                            ? ServicingType.LTSC
                            : ServicingType.Regular;

                        var version = new WindowsVersion
                        {
                            Edition = edition,
                            Version = versionName,
                            Build = build,
                            ServiceOption = string.IsNullOrWhiteSpace(servicingOption) ? servicingChannel : servicingOption,
                            Availability = availabilityText,
                            ReleaseDate = availabilityDate,
                            SupportEndDate = string.IsNullOrWhiteSpace(endOfServicing) ? null : endOfServicing,
                            ServicingType = servicingType,
                            // Fill latest columns if present
                            LatestUpdate = string.IsNullOrWhiteSpace(latestUpdate) ? null : latestUpdate,
                            LatestRevisionDate = string.IsNullOrWhiteSpace(latestRevisionDate) ? null : latestRevisionDate
                        };

                        // Map consumer/enterprise vs LTSC support columns
                        if (servicingType == ServicingType.Regular)
                        {
                            if (!string.IsNullOrWhiteSpace(endConsumer)) version.EndOfServicingStandard = endConsumer;
                            if (!string.IsNullOrWhiteSpace(endEnterprise)) version.EndOfServicingEnterprise = endEnterprise;
                        }
                        else
                        {
                            // LTSC
                            if (!string.IsNullOrWhiteSpace(mainstreamEnd)) version.MainstreamSupportEndDate = mainstreamEnd;
                            if (!string.IsNullOrWhiteSpace(extendedEnd)) version.ExtendedSupportEndDate = extendedEnd;
                        }

                        // Determine current version status
                        DateTime todayUtc = DateTime.UtcNow.Date;
                        DateTime? supportWindowEnd = null;
                        if (servicingType == ServicingType.LTSC)
                        {
                            // For LTSC use ExtendedSupportEndDate to determine current window, fallback to Mainstream if extended is missing
                            supportWindowEnd = (ParseDate(extendedEnd) ?? ExtractDateFromText(extendedEnd))
                                               ?? (ParseDate(mainstreamEnd) ?? ExtractDateFromText(mainstreamEnd));
                        }
                        else
                        {
                            // For GA/Regular use End of Servicing
                            supportWindowEnd = endOfServicingDate;
                        }

                        if (availabilityDate.HasValue && availabilityDate.Value <= todayUtc && (!supportWindowEnd.HasValue || supportWindowEnd.Value > todayUtc))
                        {
                            version.IsCurrentVersion = true;
                        }

                        versions.Add(version);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing version row");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing versions from table for servicing channel {Channel}", servicingChannel);
            }
            return versions;
        }

        private string GetCellValueByHeader(IList<HtmlNode> cells, Dictionary<string, int> headerMap, IEnumerable<string> possibleHeaders)
        {
            foreach (var header in possibleHeaders)
            {
                foreach (var key in headerMap.Keys)
                {
                    if (key.Equals(header, StringComparison.OrdinalIgnoreCase))
                    {
                        var cell = cells.ElementAtOrDefault(headerMap[key]);
                        if (cell != null) return ExtractText(cell);
                    }
                }
            }
            return string.Empty;
        }

        private string ExtractText(HtmlNode? node)
        {
            if (node == null) return string.Empty;
            var text = HtmlEntity.DeEntitize(node.InnerText ?? string.Empty).Trim();
            return text;
        }

        private string ExtractKBNumber(string text)
        {
            var match = Regex.Match(text, @"KB\d+", RegexOptions.IgnoreCase);
            return match.Success ? match.Value.ToUpperInvariant() : string.Empty;
        }

        private DateTime? ParseDate(string? dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString)) return null;
            if (DateTime.TryParse(dateString, out var date)) return date.Date;
            return null;
        }

        private bool TryParseDate(string? text, out DateTime date)
        {
            date = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (DateTime.TryParse(text, out var d)) { date = d.Date; return true; }
            var match = Regex.Match(text, @"(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},?\s+\d{4}");
            if (match.Success && DateTime.TryParse(match.Value, out d)) { date = d.Date; return true; }
            match = Regex.Match(text, @"\d{1,2}\s+(January|February|March|April|May|June|July|August|September|October|November)\s+\d{4}");
            if (match.Success && DateTime.TryParse(match.Value, out d)) { date = d.Date; return true; }
            return false;
        }

        private async Task SaveUpdatesToStorageAsync(WindowsEdition edition, List<WindowsUpdate> updates)
        {
            try
            {
                var fileName = $"{_windowsStoragePath}/{edition.ToString().ToLower()}-updates.json";
                var json = JsonSerializer.Serialize(updates, new JsonSerializerOptions { WriteIndented = true });
                await _storageService.WriteAsync(fileName, json);
                _logger.LogInformation("Saved {Count} updates for {Edition} to local storage", updates.Count, edition);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving updates to storage for {Edition}", edition);
            }
        }

        private async Task SaveReleaseVersionsToStorageAsync(WindowsEdition edition, WindowsReleaseVersions releaseVersions)
        {
            try
            {
                var fileName = $"{_windowsStoragePath}/{edition.ToString().ToLower()}-versions.json";
                var json = JsonSerializer.Serialize(releaseVersions, new JsonSerializerOptions { WriteIndented = true });
                await _storageService.WriteAsync(fileName, json);
                await UpdateLastUpdateTimeAsync();
                _logger.LogInformation("Saved channel-specific versions for {Edition} to local storage", edition);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving channel-specific versions to storage for {Edition}", edition);
            }
        }

        private async Task UpdateLastUpdateTimeAsync()
        {
            try
            {
                var fileName = $"{_windowsStoragePath}/last-update.json";
                var metadata = new { lastUpdate = DateTime.UtcNow };
                var json = JsonSerializer.Serialize(metadata);
                await _storageService.WriteAsync(fileName, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating last update time");
            }
        }

        private async Task<List<WindowsUpdate>?> LoadUpdatesFromStorageAsync(WindowsEdition edition)
        {
            try
            {
                var fileName = $"{_windowsStoragePath}/{edition.ToString().ToLower()}-updates.json";
                if (await _storageService.ExistsAsync(fileName))
                {
                    var json = await _storageService.ReadAsync(fileName);
                    return JsonSerializer.Deserialize<List<WindowsUpdate>>(json);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading updates from storage for {Edition}", edition);
            }

            return null;
        }

        private async Task<WindowsReleaseVersions?> LoadReleaseVersionsFromStorageAsync(WindowsEdition edition)
        {
            try
            {
                var fileName = $"{_windowsStoragePath}/{edition.ToString().ToLower()}-versions.json";
                if (await _storageService.ExistsAsync(fileName))
                {
                    var json = await _storageService.ReadAsync(fileName);

                    // Try to detect new structure first
                    try
                    {
                        var obj = JsonSerializer.Deserialize<WindowsReleaseVersions>(json);
                        if (obj != null && (obj.RegularVersions?.Any() == true || obj.LtscVersions?.Any() == true))
                        {
                            return obj;
                        }
                    }
                    catch
                    {
                        // ignore and try legacy format
                    }

                    // Legacy format: flat list
                    try
                    {
                        var legacy = JsonSerializer.Deserialize<List<WindowsVersion>>(json) ?? new List<WindowsVersion>();
                        var mapped = new WindowsReleaseVersions
                        {
                            Edition = edition,
                            RegularVersions = legacy.Where(v => v.ServicingType == ServicingType.Regular).Select(v => new RegularServicingVersion
                            {
                                Version = v.Version,
                                ServicingOption = v.ServiceOption,
                                AvailabilityDate = v.Availability,
                                ReleaseDate = v.ReleaseDate,
                                EndOfServicingConsumer = v.EndOfServicingStandard ?? string.Empty,
                                EndOfServicingEnterprise = v.EndOfServicingEnterprise ?? string.Empty,
                                LatestUpdate = v.LatestUpdate ?? string.Empty,
                                LatestRevisionDate = v.LatestRevisionDate ?? string.Empty,
                                LatestBuild = v.Build,
                                Edition = v.Edition,
                                IsCurrentVersion = v.IsCurrentVersion
                            }).ToList(),
                            LtscVersions = legacy.Where(v => v.ServicingType == ServicingType.LTSC || v.ServicingType == ServicingType.LTSB).Select(v => new LtscServicingVersion
                            {
                                Version = v.Version,
                                ServicingOption = v.ServiceOption,
                                AvailabilityDate = v.Availability,
                                ReleaseDate = v.ReleaseDate,
                                MainstreamSupportEndDate = v.MainstreamSupportEndDate ?? string.Empty,
                                ExtendedSupportEndDate = v.ExtendedSupportEndDate ?? string.Empty,
                                LatestUpdate = v.LatestUpdate ?? string.Empty,
                                LatestRevisionDate = v.LatestRevisionDate ?? string.Empty,
                                LatestBuild = v.Build,
                                Edition = v.Edition,
                                IsCurrentVersion = v.IsCurrentVersion
                            }).ToList()
                        };
                        return mapped;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading channel-specific versions from storage for {Edition}", edition);
            }

            return null;
        }

        private async Task<List<WindowsFeatureUpdate>?> LoadFeatureUpdatesFromStorageAsync(WindowsEdition edition)
        {
            try
            {
                var fileName = $"{_windowsStoragePath}/{edition.ToString().ToLower()}-feature-updates.json";
                if (await _storageService.ExistsAsync(fileName))
                {
                    var json = await _storageService.ReadAsync(fileName);
                    return JsonSerializer.Deserialize<List<WindowsFeatureUpdate>>(json);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading feature updates from storage for {Edition}", edition);
            }

            return null;
        }
        
        private List<WindowsVersion> FlattenReleaseVersions(WindowsReleaseVersions rv)
        {
            var list = new List<WindowsVersion>();
            foreach (var v in rv.RegularVersions)
            {
                list.Add(new WindowsVersion
                {
                    Edition = v.Edition,
                    Version = v.Version,
                    Build = v.LatestBuild,
                    ServiceOption = v.ServicingOption,
                    Availability = v.AvailabilityDate,
                    ReleaseDate = v.ReleaseDate,
                    ServicingType = ServicingType.Regular,
                    EndOfServicingStandard = v.EndOfServicingConsumer,
                    EndOfServicingEnterprise = v.EndOfServicingEnterprise,
                    LatestUpdate = v.LatestUpdate,
                    LatestRevisionDate = v.LatestRevisionDate,
                    IsCurrentVersion = v.IsCurrentVersion
                });
            }
            foreach (var v in rv.LtscVersions)
            {
                list.Add(new WindowsVersion
                {
                    Edition = v.Edition,
                    Version = v.Version,
                    Build = v.LatestBuild,
                    ServiceOption = v.ServicingOption,
                    Availability = v.AvailabilityDate,
                    ReleaseDate = v.ReleaseDate,
                    ServicingType = ServicingType.LTSC,
                    MainstreamSupportEndDate = v.MainstreamSupportEndDate,
                    ExtendedSupportEndDate = v.ExtendedSupportEndDate,
                    LatestUpdate = v.LatestUpdate,
                    LatestRevisionDate = v.LatestRevisionDate,
                    IsCurrentVersion = v.IsCurrentVersion
                });
            }
            return list;
        }

        private async Task<List<WindowsVersion>?> LoadVersionsFromStorageAsync(WindowsEdition edition)
        {
            try
            {
                var releaseVersions = await LoadReleaseVersionsFromStorageAsync(edition);
                if (releaseVersions != null)
                {
                    return FlattenReleaseVersions(releaseVersions);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading versions from storage for {Edition}", edition);
            }

            return null;
        }

        private string ExtractVersionName(string versionText)
        {
            if (string.IsNullOrWhiteSpace(versionText)) return string.Empty;
            var match = Regex.Match(versionText, @"Version\s+(\d+H\d|\d{4})", RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1) return match.Groups[1].Value;
            match = Regex.Match(versionText, @"^(\d+H\d|\d{4})", RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1) return match.Groups[1].Value;
            match = Regex.Match(versionText, @"version\s+(\d+H\d|\d{4})", RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1) return match.Groups[1].Value;
            var parts = versionText.Split(new[] { '(', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0) return parts[0].Trim();
            return versionText.Trim();
        }

        private string ExtractBuildFromVersionText(string versionText)
        {
            if (string.IsNullOrWhiteSpace(versionText)) return string.Empty;
            var match = Regex.Match(versionText, @"(?:OS\s+)?build\s+([\d\.]+)", RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1) return match.Groups[1].Value;
            match = Regex.Match(versionText, @"\b(\d{5,}(?:\.[\d]+)?)\b");
            if (match.Success && match.Groups.Count > 1) return match.Groups[1].Value;
            return string.Empty;
        }

        private async Task<WindowsUpdate> EnrichUpdateFromDetailPageAsync(WindowsUpdate update)
        {
            if (string.IsNullOrEmpty(update.SourceUrl)) return update;
            try
            {
                string detailUrl = update.SourceUrl;
                if (!detailUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    detailUrl = "https://support.microsoft.com" + detailUrl;
                }

                var html = await _httpClient.GetStringAsync(detailUrl);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var mainContent = doc.DocumentNode.SelectSingleNode("//main[@id='supArticleContent']") ?? doc.DocumentNode;

                var descriptionParagraphs = mainContent.SelectNodes(".//p[not(contains(@class, 'title'))]");
                if (descriptionParagraphs != null && descriptionParagraphs.Count > 0)
                {
                    update.Description = ExtractText(descriptionParagraphs[0]);
                }

                var improvementsList = mainContent.SelectNodes(".//ul/li[contains(., 'improve') or contains(., 'address')]");
                if (improvementsList != null)
                {
                    foreach (var item in improvementsList)
                    {
                        string text = ExtractText(item);
                        if (!string.IsNullOrEmpty(text) && text.Length > 10) update.Highlights.Add(text);
                    }
                }

                var knownIssuesHeading = mainContent.SelectSingleNode(".//h2[contains(., 'Known issues')]");
                if (knownIssuesHeading != null)
                {
                    var nextElement = knownIssuesHeading.NextSibling;
                    while (nextElement != null)
                    {
                        if (nextElement.Name is "ul" or "ol")
                        {
                            var issueItems = nextElement.SelectNodes(".//li");
                            if (issueItems != null)
                            {
                                foreach (var item in issueItems)
                                {
                                    string text = ExtractText(item);
                                    if (!string.IsNullOrEmpty(text) && text.Length > 10) update.KnownIssues.Add(text);
                                }
                            }
                            break;
                        }
                        nextElement = nextElement.NextSibling;
                    }
                }

                if (mainContent.InnerText.Contains("security", StringComparison.OrdinalIgnoreCase) ||
                    mainContent.InnerText.Contains("vulnerabilit", StringComparison.OrdinalIgnoreCase))
                {
                    update.IsSecurityUpdate = true;
                }

                if (update.UpdateTitle.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
                    mainContent.InnerText.Contains("preview release", StringComparison.OrdinalIgnoreCase) ||
                    mainContent.InnerText.Contains("optional", StringComparison.OrdinalIgnoreCase))
                {
                    update.IsOptionalUpdate = true;
                }

                if (update.UpdateTitle.Contains("cumulative", StringComparison.OrdinalIgnoreCase) ||
                    mainContent.InnerText.Contains("cumulative update", StringComparison.OrdinalIgnoreCase))
                {
                    update.Type = "Cumulative";
                }
                else if (update.IsSecurityUpdate)
                {
                    update.Type = "Security";
                }
                else if (update.UpdateTitle.Contains("feature", StringComparison.OrdinalIgnoreCase) ||
                         mainContent.InnerText.Contains("feature update", StringComparison.OrdinalIgnoreCase))
                {
                    update.Type = "Feature";
                }
                else if (update.IsOptionalUpdate)
                {
                    update.Type = "Optional";
                }
                else
                {
                    update.Type = "General";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enriching update KB{KBNumber} from detail page", update.KBNumber);
            }

            return update;
        }
    }
}
