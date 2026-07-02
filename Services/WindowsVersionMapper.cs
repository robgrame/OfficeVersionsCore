using Microsoft.Extensions.Logging;
using OfficeVersionsCore.Models;

namespace OfficeVersionsCore.Services;

/// <summary>
/// Service responsible for mapping Windows builds to versions and filtering updates by edition
/// </summary>
public interface IWindowsVersionMapper
{
    string DetermineVersionFromBuild(string build);
    List<WindowsUpdate> FilterUpdatesByEdition(List<WindowsUpdate> updates, WindowsEdition edition);
    bool IsValidBuildForEdition(string build, WindowsEdition edition);
}

/// <summary>
/// Service responsible for mapping Windows builds to versions and filtering updates by edition
/// </summary>
public class WindowsVersionMapper : IWindowsVersionMapper
{
    private readonly ILogger<WindowsVersionMapper> _logger;

    // Build number prefixes for filtering Windows Server updates
    // Windows Server editions often share update pages with Windows 10/11, 
    // so we need to filter by specific build numbers
    private static readonly Dictionary<WindowsEdition, string> EditionBuildPrefixes = new()
    {
        { WindowsEdition.WindowsServer2012R2, "9600" },   // Windows Server 2012 R2
        { WindowsEdition.WindowsServer2016, "14393" },    // Windows Server 2016 / Windows 10 1607
        { WindowsEdition.WindowsServer2019, "17763" },    // Windows Server 2019 / Windows 10 1809
        { WindowsEdition.WindowsServer2022, "20348" },    // Windows Server 2022 (dedicated)
        { WindowsEdition.WindowsServer2025, "26100" }     // Windows Server 2025 / Windows 11 24H2
    };

    public WindowsVersionMapper(ILogger<WindowsVersionMapper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Determines the Windows version from a build number
    /// </summary>
    /// <param name="build">Build number (e.g., "19045.2965")</param>
    /// <returns>Version string (e.g., "22H2") or empty if not recognized</returns>
    public string DetermineVersionFromBuild(string build)
    {
        if (string.IsNullOrEmpty(build)) return string.Empty;

        // Validate build format - must be 4-5 digits minimum for Windows
        var buildMatch = System.Text.RegularExpressions.Regex.Match(build, @"^(\d{4,5})(?:\.\d+)?$");
        if (!buildMatch.Success)
        {
            _logger.LogWarning("Invalid build format: {Build}", build);
            return string.Empty;
        }

        string majorBuild = build.Split('.')[0];

        // Validate major build is 4 or 5 digits
        if (majorBuild.Length < 4 || majorBuild.Length > 5)
        {
            _logger.LogWarning("Invalid major build length: {MajorBuild}", majorBuild);
            return string.Empty;
        }

        // Map build numbers to versions
        return majorBuild switch
        {
            // Windows 11 build numbers
            "26200" => "25H2", // Windows 11 25H2
            "26100" => "24H2", // Windows 11 24H2 / Windows Server 2025
            "22631" => "23H2", // Windows 11 23H2
            "22621" => "22H2", // Windows 11 22H2
            "22000" => "21H2", // Windows 11 initial release

            // Windows Server 2022
            "20348" => "21H2", // Windows Server 2022

            // Windows 10 build numbers
            "19045" => "22H2", // Windows 10 22H2
            "19044" => "21H2", // Windows 10 21H2
            "19043" => "21H1", // Windows 10 21H1
            "19042" => "20H2", // Windows 10 20H2
            "19041" => "2004", // Windows 10 2004
            "18363" => "1909", // Windows 10 1909
            "18362" => "1903", // Windows 10 1903
            "17763" => "1809", // Windows 10 1809 / Windows Server 2019
            "17134" => "1803", // Windows 10 1803
            "16299" => "1709", // Windows 10 1709
            "15254" => "1709 Mobile",
            "15063" => "1703", // Windows 10 1703
            "14393" => "1607", // Windows 10 1607 / Windows Server 2016
            "10586" => "1511", // Windows 10 1511
            "10240" => "1507", // Windows 10 initial release

            // Windows Server 2012 R2
            "9600" => "Server 2012 R2",

            _ => string.Empty
        };
    }

    /// <summary>
    /// Checks if a build number is valid for a specific Windows edition
    /// </summary>
    /// <param name="build">Build number to check</param>
    /// <param name="edition">Windows edition</param>
    /// <returns>True if build is valid for edition, false otherwise</returns>
    public bool IsValidBuildForEdition(string build, WindowsEdition edition)
    {
        // Windows 10 and Windows 11 accept all builds (no filtering needed)
        if (edition == WindowsEdition.Windows10 || edition == WindowsEdition.Windows11)
        {
            return true;
        }

        // For Windows Server editions, check build prefix
        if (EditionBuildPrefixes.TryGetValue(edition, out var buildPrefix))
        {
            if (string.IsNullOrEmpty(build))
            {
                // If no build specified, we can't filter - accept it
                return true;
            }

            // Extract major build (first part before dot)
            string majorBuild = build.Split('.')[0];

            // Check if build starts with expected prefix
            bool isValid = majorBuild.StartsWith(buildPrefix, StringComparison.Ordinal);

            if (!isValid)
            {
                _logger.LogDebug("Build {Build} is not valid for {Edition} (expected prefix: {Prefix})",
                    build, edition, buildPrefix);
            }

            return isValid;
        }

        // Unknown edition - accept all builds
        return true;
    }

    /// <summary>
    /// Filters a list of updates to only include those relevant to a specific Windows edition
    /// </summary>
    /// <param name="updates">List of updates to filter</param>
    /// <param name="edition">Windows edition to filter for</param>
    /// <returns>Filtered list of updates</returns>
    public List<WindowsUpdate> FilterUpdatesByEdition(List<WindowsUpdate> updates, WindowsEdition edition)
    {
        if (updates == null || updates.Count == 0)
        {
            return updates ?? new List<WindowsUpdate>();
        }

        // For Windows 10 and Windows 11, no filtering needed
        if (edition == WindowsEdition.Windows10 || edition == WindowsEdition.Windows11)
        {
            _logger.LogDebug("No filtering needed for {Edition} - returning all {Count} updates", edition, updates.Count);
            return updates;
        }

        // For Windows Server editions, filter by build number
        if (EditionBuildPrefixes.ContainsKey(edition))
        {
            var originalCount = updates.Count;
            var filtered = updates.Where(u => IsValidBuildForEdition(u.Build, edition)).ToList();

            _logger.LogInformation("Filtered updates for {Edition}: {Original} → {Filtered} (removed {Removed})",
                edition, originalCount, filtered.Count, originalCount - filtered.Count);

            return filtered;
        }

        // Unknown edition - return all updates
        _logger.LogWarning("Unknown edition {Edition} - returning all {Count} updates without filtering", edition, updates.Count);
        return updates;
    }
}
