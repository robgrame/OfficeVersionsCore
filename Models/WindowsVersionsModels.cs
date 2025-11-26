using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OfficeVersionsCore.Models
{
    /// <summary>
    /// Represents a Windows version/build information
    /// </summary>
    public class WindowsVersion
    {
        public string Version { get; set; } = string.Empty;
        public string Build { get; set; } = string.Empty;
        public string KBNumber { get; set; } = string.Empty;
        public DateTime? ReleaseDate { get; set; }
        public string ServiceOption { get; set; } = string.Empty;
        public string Availability { get; set; } = string.Empty;
        public WindowsEdition Edition { get; set; }
        public List<string> NewFeatures { get; set; } = new();
        public List<string> KnownIssues { get; set; } = new();
        public string? SupportEndDate { get; set; }
        public bool IsCurrentVersion { get; set; }
        public bool IsLatestUpdate { get; set; }
        public string? AdditionalNotes { get; set; }
        public string? SecurityUpdates { get; set; }
        
        // Additional properties to match Microsoft's documentation tables
        public string? EndOfServicingStandard { get; set; } // Home, Pro, Pro Education, Pro for Workstations
        public string? EndOfServicingEnterprise { get; set; } // Enterprise, Education, IoT Enterprise
        public string? MainstreamSupportEndDate { get; set; } // For LTSC editions
        public string? ExtendedSupportEndDate { get; set; } // For LTSC editions
        public string? LatestUpdate { get; set; }
        public string? LatestRevisionDate { get; set; }
        
        // Servicing type property to distinguish between regular and LTSC editions
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ServicingType ServicingType { get; set; } = ServicingType.Regular;
    }

    /// <summary>
    /// Servicing type enumeration
    /// </summary>
    public enum ServicingType
    {
        Regular,   // Standard servicing channel
        LTSC,      // Long-Term Servicing Channel
        LTSB       // Long-Term Servicing Branch (older name for LTSC)
    }

    /// <summary>
    /// Servicing type display names for product naming
    /// </summary>
    public static class ServicingTypeNames
    {
        public static string GetDisplayName(ServicingType type, WindowsEdition edition) =>
            type switch
            {
                ServicingType.LTSC => $"Enterprise LTSC",
                ServicingType.LTSB => $"Enterprise LTSB",
                ServicingType.Regular => "Standard",
                _ => "Unknown"
            };

        public static string GetFullProductName(WindowsEdition edition, string version, ServicingType servicingType) =>
            servicingType switch
            {
                ServicingType.LTSC => $"{edition.GetDisplayName()} Enterprise LTSC {version}",
                ServicingType.LTSB => $"{edition.GetDisplayName()} Enterprise LTSB {version}",
                ServicingType.Regular => $"{edition.GetDisplayName()} {version}",
                _ => $"{edition.GetDisplayName()} {version}"
            };
    }

    /// <summary>
    /// Windows edition display name helper
    /// </summary>
    public static class WindowsEditionExtensions
    {
        public static string GetDisplayName(this WindowsEdition edition) =>
            edition switch
            {
                WindowsEdition.Windows10 => "Windows 10",
                WindowsEdition.Windows11 => "Windows 11",
                _ => "Windows"
            };
    }

    /// <summary>
    /// Windows edition enumeration
    /// </summary>
    public enum WindowsEdition
    {
        Windows10,
        Windows11
    }

    /// <summary>
    /// Represents a Windows update entry
    /// </summary>
    public class WindowsUpdate
    {
        public string Version { get; set; } = string.Empty;
        public string Build { get; set; } = string.Empty;
        public string KBNumber { get; set; } = string.Empty;
        public DateTime? ReleaseDate { get; set; }
        public WindowsEdition Edition { get; set; }
        public string UpdateTitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Type { get; set; } // Cumulative, Security, Feature, etc.
        public List<string> Highlights { get; set; } = new();
        public List<string> KnownIssues { get; set; } = new();
        public string? SupportUrl { get; set; }
        public bool IsSecurityUpdate { get; set; }
        public bool IsOptionalUpdate { get; set; }
        public string SourceUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents Windows release information summary
    /// </summary>
    public class WindowsReleaseSummary
    {
        public WindowsEdition Edition { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestBuild { get; set; } = string.Empty;
        public DateTime? LastUpdateDate { get; set; }
        public int TotalUpdates { get; set; }
        public int SecurityUpdates { get; set; }
        public List<WindowsVersion> RecentVersions { get; set; } = new();
        public List<WindowsUpdate> RecentUpdates { get; set; } = new();
        
        // Use specific models for each channel
        public List<RegularServicingVersion> RegularVersions { get; set; } = new();
        public List<LtscServicingVersion> LtscVersions { get; set; } = new();
    }

    /// <summary>
    /// Storage payload for versions grouped by servicing channel
    /// </summary>
    public class WindowsReleaseVersions
    {
        public WindowsEdition Edition { get; set; }
        public List<RegularServicingVersion> RegularVersions { get; set; } = new();
        public List<LtscServicingVersion> LtscVersions { get; set; } = new();
    }

    /// <summary>
    /// Windows regular servicing channel version information
    /// Maps directly to the main Windows release table columns
    /// </summary>
    public class RegularServicingVersion
    {
        public string Version { get; set; } = string.Empty;
        public string ServicingOption { get; set; } = string.Empty;
        public string AvailabilityDate { get; set; } = string.Empty;
        public DateTime? ReleaseDate { get; set; } // Parsed from AvailabilityDate
        public string EndOfServicingConsumer { get; set; } = string.Empty; // Home, Pro, etc.
        public string EndOfServicingEnterprise { get; set; } = string.Empty; // Enterprise, Education, etc.
        public string LatestUpdate { get; set; } = string.Empty;
        public string LatestRevisionDate { get; set; } = string.Empty;
        public string LatestBuild { get; set; } = string.Empty;
        public WindowsEdition Edition { get; set; }
        public bool IsCurrentVersion { get; set; }
    }

    /// <summary>
    /// Windows LTSC edition version information
    /// Maps directly to the "Enterprise and IoT Enterprise LTSB/LTSC editions" table columns
    /// </summary>
    public class LtscServicingVersion
    {
        public string Version { get; set; } = string.Empty;
        public string ServicingOption { get; set; } = string.Empty;
        public string AvailabilityDate { get; set; } = string.Empty;
        public DateTime? ReleaseDate { get; set; } // Parsed from AvailabilityDate
        public string MainstreamSupportEndDate { get; set; } = string.Empty;
        public string ExtendedSupportEndDate { get; set; } = string.Empty;
        public string LatestUpdate { get; set; } = string.Empty;
        public string LatestRevisionDate { get; set; } = string.Empty;
        public string LatestBuild { get; set; } = string.Empty;
        public WindowsEdition Edition { get; set; }
        public bool IsCurrentVersion { get; set; }
    }

    /// <summary>
    /// Windows servicing channel information
    /// </summary>
    public class ServicingChannel
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ReleaseFrequency { get; set; } = string.Empty;
        public string SupportDuration { get; set; } = string.Empty;
        public string TargetAudience { get; set; } = string.Empty;
    }

    /// <summary>
    /// API response wrapper
    /// </summary>
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? Source { get; set; }
    }

    /// <summary>
    /// Windows version comparison result
    /// </summary>
    public class VersionComparison
    {
        public string Version1 { get; set; } = string.Empty;
        public string Version2 { get; set; } = string.Empty;
        public int Result { get; set; } // -1 = v1 < v2, 0 = equal, 1 = v1 > v2
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents Windows feature update information
    /// </summary>
    public class WindowsFeatureUpdate
    {
        public string Version { get; set; } = string.Empty;
        public string Codename { get; set; } = string.Empty;
        public DateTime? ReleaseDate { get; set; }
        public DateTime? EndOfServiceDate { get; set; }
        public WindowsEdition Edition { get; set; }
        public List<string> KeyFeatures { get; set; } = new();
        public string? SupportStatus { get; set; }
        public bool IsSupported { get; set; }
        public string? UpgradePath { get; set; }
    }
}
