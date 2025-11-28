using System.Net;
using System.Text.RegularExpressions;
using OfficeVersionsCore.Models;

namespace OfficeVersionsCore.Services
{
    /// <summary>
    /// Utility class to clean and improve Windows update data
    /// </summary>
    public static class UpdateDataCleaner
    {
        /// <summary>
        /// Clean and improve Windows update data
        /// </summary>
        /// <param name="updates">Raw Windows update data to clean</param>
        /// <returns>Cleaned Windows update data</returns>
        public static List<WindowsUpdate> CleanWindowsUpdates(List<WindowsUpdate> updates)
        {
            if (updates == null || !updates.Any())
                return new List<WindowsUpdate>();

            var result = new List<WindowsUpdate>();
            var processedKBs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var update in updates.Where(u => !string.IsNullOrWhiteSpace(u.KBNumber)))
            {
                // Skip duplicates based on KB number
                if (processedKBs.Contains(update.KBNumber))
                    continue;

                var cleanedUpdate = new WindowsUpdate
                {
                    KBNumber = update.KBNumber,
                    ReleaseDate = FixFutureDates(update.ReleaseDate),
                    Edition = update.Edition,
                    IsSecurityUpdate = update.IsSecurityUpdate || 
                                      (update.UpdateTitle?.Contains("security", StringComparison.OrdinalIgnoreCase) == true),
                    IsOptionalUpdate = update.IsOptionalUpdate || 
                                      (update.UpdateTitle?.Contains("optional", StringComparison.OrdinalIgnoreCase) == true),
                    Highlights = update.Highlights ?? new List<string>(),
                    KnownIssues = update.KnownIssues ?? new List<string>()
                };

                // Clean and decode update title
                cleanedUpdate.UpdateTitle = CleanHtmlContent(update.UpdateTitle);
                
                // Extract version from the update title if missing
                cleanedUpdate.Version = !string.IsNullOrWhiteSpace(update.Version) 
                    ? update.Version 
                    : ExtractVersionFromTitle(cleanedUpdate.UpdateTitle);
                
                // Extract build from the update title if missing
                cleanedUpdate.Build = !string.IsNullOrWhiteSpace(update.Build)
                    ? update.Build
                    : ExtractBuildFromTitle(cleanedUpdate.UpdateTitle);
                
                // Extract description from update title if missing
                cleanedUpdate.Description = !string.IsNullOrWhiteSpace(update.Description)
                    ? update.Description
                    : ExtractDescriptionFromTitle(cleanedUpdate.UpdateTitle);
                
                // Determine update type if missing
                cleanedUpdate.Type = !string.IsNullOrWhiteSpace(update.Type)
                    ? update.Type
                    : DetermineUpdateType(cleanedUpdate);
                
                // Generate support URL if missing
                cleanedUpdate.SupportUrl = !string.IsNullOrWhiteSpace(update.SupportUrl)
                    ? update.SupportUrl
                    : $"https://support.microsoft.com/help/{cleanedUpdate.KBNumber.Replace("KB", "")}";
                
                result.Add(cleanedUpdate);
                processedKBs.Add(cleanedUpdate.KBNumber);
            }

            return result;
        }

        /// <summary>
        /// Clean and improve Windows version data
        /// </summary>
        /// <param name="versions">Raw Windows version data to clean</param>
        /// <returns>Cleaned Windows version data</returns>
        public static List<WindowsVersion> CleanWindowsVersions(List<WindowsVersion> versions)
        {
            if (versions == null || !versions.Any())
                return new List<WindowsVersion>();

            var result = new List<WindowsVersion>();
            var processedVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // First pass: clean each version and remove duplicates
            foreach (var version in versions)
            {
                // Skip if we don't have a valid version string
                if (string.IsNullOrWhiteSpace(version.Version))
                    continue;

                // Create unique key for deduplication
                string versionKey = $"{version.Edition}-{version.Version}-{version.ServicingType}";
                if (processedVersions.Contains(versionKey))
                    continue;

                // Determine servicing type if not set
                ServicingType servicingType = version.ServicingType;
                if (servicingType == ServicingType.Regular && 
                    (version.ServiceOption?.Contains("LTSC", StringComparison.OrdinalIgnoreCase) == true ||
                     version.ServiceOption?.Contains("LTSB", StringComparison.OrdinalIgnoreCase) == true ||
                     version.Version?.Contains("LTSC", StringComparison.OrdinalIgnoreCase) == true))
                {
                    servicingType = ServicingType.LTSC;
                }
                else if (servicingType == ServicingType.Regular && 
                         version.ServiceOption?.Contains("LTSB", StringComparison.OrdinalIgnoreCase) == true)
                {
                    servicingType = ServicingType.LTSB;
                }

                var cleanedVersion = new WindowsVersion
                {
                    Edition = version.Edition,
                    Version = CleanVersionString(version.Version),
                    Build = CleanBuildString(version.Build),
                    ServiceOption = string.IsNullOrWhiteSpace(version.ServiceOption) ? 
                                    DetermineServiceOption(version) : version.ServiceOption,
                    Availability = version.Availability,
                    ReleaseDate = FixFutureDates(version.ReleaseDate),
                    IsCurrentVersion = version.IsCurrentVersion,
                    SupportEndDate = version.SupportEndDate,
                    AdditionalNotes = version.AdditionalNotes,
                    SecurityUpdates = version.SecurityUpdates,
                    ServicingType = servicingType,
                    
                    // Add new properties for both table types
                    EndOfServicingStandard = version.EndOfServicingStandard,
                    EndOfServicingEnterprise = version.EndOfServicingEnterprise,
                    MainstreamSupportEndDate = version.MainstreamSupportEndDate,
                    ExtendedSupportEndDate = version.ExtendedSupportEndDate,
                    LatestUpdate = version.LatestUpdate,
                    LatestRevisionDate = version.LatestRevisionDate
                };

                // Initialize collections
                cleanedVersion.NewFeatures = version.NewFeatures ?? new List<string>();
                cleanedVersion.KnownIssues = version.KnownIssues ?? new List<string>();

                // Add to result and mark as processed
                result.Add(cleanedVersion);
                processedVersions.Add(versionKey);
            }

            // Second pass: ensure we have a current version for each servicing type
            var versionsByEdition = result.GroupBy(v => v.Edition);
            
            foreach (var editionGroup in versionsByEdition)
            {
                // Reset current version flags first (important if data is incorrectly marked)
                foreach (var v in editionGroup)
                {
                    v.IsCurrentVersion = false;
                }
                
                // First, look at regular versions
                var regularVersions = editionGroup.Where(v => v.ServicingType == ServicingType.Regular).ToList();
                if (regularVersions.Any())
                {
                    // Try to identify current version based on known current versions
                    var currentVersionMap = new Dictionary<WindowsEdition, string>
                    {
                        { WindowsEdition.Windows10, "22H2" }, // Current as of 2024
                        { WindowsEdition.Windows11, "23H2" }  // Current as of 2024
                    };
                    
                    // Try to find the current version for this edition
                    if (currentVersionMap.TryGetValue(editionGroup.Key, out string? currentVersionStr) && currentVersionStr != null)
                    {
                        // First check for an exact match by version number
                        var currentVersion = regularVersions.FirstOrDefault(v => 
                            v.Version.Equals(currentVersionStr, StringComparison.OrdinalIgnoreCase));
                        
                        if (currentVersion != null)
                        {
                            currentVersion.IsCurrentVersion = true;
                        }
                        else
                        {
                            // If exact match isn't found, try to find a version that starts with the same prefix
                            currentVersion = regularVersions.FirstOrDefault(v => 
                                v.Version.StartsWith(currentVersionStr, StringComparison.OrdinalIgnoreCase));
                                
                            if (currentVersion != null)
                            {
                                currentVersion.IsCurrentVersion = true;
                            }
                            else
                            {
                                // Fallback: mark latest version in this edition as current
                                var latestVersion = regularVersions
                                    .Where(v => v.ReleaseDate.HasValue)
                                    .OrderByDescending(v => v.ReleaseDate)
                                    .ThenByDescending(v => ParseVersionNumber(v.Version))
                                    .FirstOrDefault();
    
                                if (latestVersion != null)
                                {
                                    latestVersion.IsCurrentVersion = true;
                                }
                                else if (regularVersions.Any())
                                {
                                    // Last resort: mark first version in group
                                    regularVersions.First().IsCurrentVersion = true;
                                }
                            }
                        }
                    }
                }
                
                // Also ensure we have a current LTSC version if any exist
                var ltscVersions = editionGroup
                    .Where(v => v.ServicingType == ServicingType.LTSC || v.ServicingType == ServicingType.LTSB)
                    .ToList();
                    
                if (ltscVersions.Any())
                {
                    // Try to determine the current LTSC version
                    // Check for known current LTSC versions first
                    var ltscVersionMap = new Dictionary<WindowsEdition, string>
                    {
                        { WindowsEdition.Windows10, "LTSC 2021" }, // Current as of 2024
                        { WindowsEdition.Windows11, "LTSC 2023" }  // Current as of 2024 (check if exists)
                    };
                    
                    if (ltscVersionMap.TryGetValue(editionGroup.Key, out string? currentLtscStr) && currentLtscStr != null)
                    {
                        var currentLtsc = ltscVersions.FirstOrDefault(v => 
                            v.Version.Contains(currentLtscStr, StringComparison.OrdinalIgnoreCase) || 
                            v.ServiceOption.Contains(currentLtscStr, StringComparison.OrdinalIgnoreCase));
                            
                        if (currentLtsc != null)
                        {
                            currentLtsc.IsCurrentVersion = true;
                        }
                        else
                        {
                            // If we couldn't find a specific LTSC version, mark the latest one as current
                            var latestLtsc = ltscVersions
                                .Where(v => v.ReleaseDate.HasValue)
                                .OrderByDescending(v => v.ReleaseDate)
                                .ThenByDescending(v => ParseVersionNumber(v.Version))
                                .FirstOrDefault();
                                
                            if (latestLtsc != null)
                            {
                                latestLtsc.IsCurrentVersion = true;
                            }
                            else
                            {
                                // Last resort: mark first LTSC version in group
                                ltscVersions.First().IsCurrentVersion = true;
                            }
                        }
                    }
                    else
                    {
                        // If no specific LTSC version is known, mark the latest one as current
                        var latestLtsc = ltscVersions
                            .Where(v => v.ReleaseDate.HasValue)
                            .OrderByDescending(v => v.ReleaseDate)
                            .ThenByDescending(v => ParseVersionNumber(v.Version))
                            .FirstOrDefault();
                            
                        if (latestLtsc != null)
                        {
                            latestLtsc.IsCurrentVersion = true;
                        }
                        else
                        {
                            // Last resort: mark first LTSC version in group
                            ltscVersions.First().IsCurrentVersion = true;
                        }
                    }
                }
            }

            return result;
        }
        
        /// <summary>
        /// Parse a version string into a numeric representation for sorting
        /// </summary>
        private static int ParseVersionNumber(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return 0;
                
            // Handle YYH# format (e.g., 22H2)
            var yyHNMatch = Regex.Match(version, @"(\d{2})H(\d)");
            if (yyHNMatch.Success && yyHNMatch.Groups.Count > 2)
            {
                int year = int.TryParse(yyHNMatch.Groups[1].Value, out int y) ? y : 0;
                int half = int.TryParse(yyHNMatch.Groups[2].Value, out int h) ? h : 0;
                return year * 10 + half;
            }
            
            // Handle YYMM format (e.g., 1903)
            var yymmMatch = Regex.Match(version, @"(\d{2})(\d{2})");
            if (yymmMatch.Success && yymmMatch.Groups.Count > 2)
            {
                int year = int.TryParse(yymmMatch.Groups[1].Value, out int y) ? y : 0;
                int month = int.TryParse(yymmMatch.Groups[2].Value, out int m) ? m : 0;
                return year * 100 + month;
            }
            
            // Handle plain numeric versions
            if (int.TryParse(version, out int numericVersion))
            {
                return numericVersion;
            }
            
            // For non-standard versions, return 0
            return 0;
        }

        /// <summary>
        /// Clean version string to standard format
        /// </summary>
        public static string CleanVersionString(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return string.Empty;

            // Remove "Version" prefix if present
            version = Regex.Replace(version, @"^version\s+", "", RegexOptions.IgnoreCase).Trim();
            
            // First try to match YYH# format (e.g., 22H2, 21H1, 20H2)
            var versionMatch = Regex.Match(version, @"(\d{2}H\d)");
            if (versionMatch.Success)
            {
                return versionMatch.Groups[1].Value;
            }
            
            // Then try to match valid Windows 10 YYMM versions (1507-2004)
            // This regex only matches valid Windows 10 version ranges
            versionMatch = Regex.Match(version, @"(1[5-9][0-1]\d|20[0-0][0-4])");
            if (versionMatch.Success)
            {
                string candidate = versionMatch.Groups[1].Value;
                // Additional validation: check if it's a known valid version
                if (IsValidWindows10VersionNumber(candidate))
                {
                    return candidate;
                }
            }
            
            // Check for LTSC versions
            if (version.Contains("LTSC", StringComparison.OrdinalIgnoreCase) || 
                version.Contains("LTSB", StringComparison.OrdinalIgnoreCase))
            {
                return version;
            }

            return version;
        }
        
        /// <summary>
        /// Validates if a 4-digit number is a valid Windows 10 version
        /// </summary>
        private static bool IsValidWindows10VersionNumber(string version)
        {
            // List of all valid Windows 10 version numbers
            var validVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "1507", // RTM
                "1511", // November Update
                "1607", // Anniversary Update
                "1703", // Creators Update
                "1709", // Fall Creators Update
                "1803", // April 2018 Update
                "1809", // October 2018 Update
                "1903", // May 2019 Update
                "1909", // November 2019 Update
                "2004"  // May 2020 Update
            };
            
            return validVersions.Contains(version);
        }

        /// <summary>
        /// Clean build string to standard format
        /// </summary>
        private static string CleanBuildString(string build)
        {
            if (string.IsNullOrWhiteSpace(build))
                return string.Empty;

            // Remove "Build" or "OS Build" prefix if present
            build = Regex.Replace(build, @"^(OS\s+)?build\s+", "", RegexOptions.IgnoreCase).Trim();
            
            // Keep only digits and dots for build numbers
            var buildMatch = Regex.Match(build, @"(\d+(\.\d+)*)");
            if (buildMatch.Success)
            {
                return buildMatch.Groups[1].Value;
            }

            return build;
        }

        /// <summary>
        /// Determine service option if not provided
        /// </summary>
        private static string DetermineServiceOption(WindowsVersion version)
        {
            if (version.Version?.Contains("LTSC", StringComparison.OrdinalIgnoreCase) == true)
                return "LTSC";
                
            if (version.Build?.StartsWith("22") == true)
                return "General Availability";
                
            return "General";
        }

        /// <summary>
        /// Clean HTML content by decoding entities and removing excessive whitespace
        /// </summary>
        private static string CleanHtmlContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return string.Empty;

            // Decode HTML entities using System.Net.WebUtility instead of System.Web.HttpUtility
            var decoded = WebUtility.HtmlDecode(content);
            
            // Replace tabs, newlines with spaces
            decoded = Regex.Replace(decoded, @"[\t\r\n]+", " ");
            
            // Replace multiple spaces with a single space
            decoded = Regex.Replace(decoded, @"\s{2,}", " ");
            
            return decoded.Trim();
        }

        /// <summary>
        /// Extract Windows version from update title
        /// </summary>
        private static string ExtractVersionFromTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            // Try to find version in standard format
            var versionRegex = new Regex(@"Windows 10,? version (\d{4}|1\d{3}|\d{2}H\d)", RegexOptions.IgnoreCase);
            var match = versionRegex.Match(title);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            // Try other common patterns
            var patterns = new Dictionary<string, string>
            {
                { @"(\d{2}H\d)", "$1" },          // Matches 22H2, 21H1, etc.
                { @"(1\d{3})", "$1" },            // Matches 1903, 1909, etc.
                { @"(2004)", "$1" }                // Matches 2004
            };

            foreach (var pattern in patterns)
            {
                match = Regex.Match(title, pattern.Key);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            // Check for version names
            if (title.Contains("22H2", StringComparison.OrdinalIgnoreCase)) return "22H2";
            if (title.Contains("21H2", StringComparison.OrdinalIgnoreCase)) return "21H2";
            if (title.Contains("21H1", StringComparison.OrdinalIgnoreCase)) return "21H1";
            if (title.Contains("20H2", StringComparison.OrdinalIgnoreCase)) return "20H2";
            if (title.Contains("2004", StringComparison.OrdinalIgnoreCase)) return "2004";
            if (title.Contains("1909", StringComparison.OrdinalIgnoreCase)) return "1909";
            if (title.Contains("1903", StringComparison.OrdinalIgnoreCase)) return "1903";
            if (title.Contains("1809", StringComparison.OrdinalIgnoreCase)) return "1809";
            if (title.Contains("1803", StringComparison.OrdinalIgnoreCase)) return "1803";
            if (title.Contains("1709", StringComparison.OrdinalIgnoreCase)) return "1709";
            if (title.Contains("1703", StringComparison.OrdinalIgnoreCase)) return "1703";
            if (title.Contains("1607", StringComparison.OrdinalIgnoreCase)) return "1607";
            if (title.Contains("1511", StringComparison.OrdinalIgnoreCase)) return "1511";
            if (title.Contains("1507", StringComparison.OrdinalIgnoreCase)) return "1507";

            return string.Empty;
        }

        /// <summary>
        /// Extract build number from update title
        /// </summary>
        private static string ExtractBuildFromTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            // Look for build numbers in the title (OS Build 19045.6396)
            var buildRegex = new Regex(@"(?:OS Build|Build) (\d{5}\.\d+)", RegexOptions.IgnoreCase);
            var match = buildRegex.Match(title);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            // Try alternative format
            buildRegex = new Regex(@"build (\d{5}(?:\.\d+)?)", RegexOptions.IgnoreCase);
            match = buildRegex.Match(title);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return string.Empty;
        }

        /// <summary>
        /// Extract description from update title
        /// </summary>
        private static string ExtractDescriptionFromTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            // Extract content after special characters that often separate the title from description
            foreach (var separator in new[] { "—", "–", "-", ":", "|" })
            {
                var parts = title.Split(new[] { separator }, 2, StringSplitOptions.None);
                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    return parts[1].Trim();
                }
            }

            // Extract text after KB number as a fallback
            var kbMatch = Regex.Match(title, @"KB\d+");
            if (kbMatch.Success && kbMatch.Index + kbMatch.Length < title.Length)
            {
                return title.Substring(kbMatch.Index + kbMatch.Length).Trim();
            }

            return string.Empty;
        }

        /// <summary>
        /// Determine update type based on content
        /// </summary>
        private static string DetermineUpdateType(WindowsUpdate update)
        {
            var title = update.UpdateTitle ?? string.Empty;

            if (update.IsSecurityUpdate)
            {
                return "Security";
            }
            else if (title.Contains("Preview", StringComparison.OrdinalIgnoreCase))
            {
                return "Preview";
            }
            else if (title.Contains("Cumulative", StringComparison.OrdinalIgnoreCase))
            {
                return update.IsSecurityUpdate ? "Security Update" : "Cumulative Update";
            }
            else if (title.Contains("Feature", StringComparison.OrdinalIgnoreCase))
            {
                return "Feature Update";
            }
            else if (title.Contains("Out-of-band", StringComparison.OrdinalIgnoreCase))
            {
                return "Out-of-band Update";
            }
            else if (title.Contains("Servicing Stack", StringComparison.OrdinalIgnoreCase))
            {
                return "Servicing Stack Update";
            }
            else
            {
                return "General Update";
            }
        }

        /// <summary>
        /// Fix dates that are incorrectly set in the future
        /// </summary>
        private static DateTime? FixFutureDates(DateTime? date)
        {
            if (!date.HasValue)
                return null;

            // If date is more than 1 year in the future, adjust it to current year
            if (date > DateTime.Now.AddYears(1))
            {
                return new DateTime(DateTime.Now.Year, date.Value.Month, date.Value.Day);
            }

            return date;
        }
    }
}
