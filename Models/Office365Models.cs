using System.Text.Json.Serialization;

namespace OfficeVersionsCore.Models
{
    /// <summary>
    /// Represents a single Office 365 version/release entry
    /// </summary>
    public class Office365Version
    {
        [JsonPropertyName("channel")]
        public string Channel { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("build")]
        public string Build { get; set; } = string.Empty;

        // Existing fields from aggregated latest versions JSON
        [JsonPropertyName("latestReleaseDate")]
        public string LatestReleaseDate { get; set; } = string.Empty;

        [JsonPropertyName("firstAvailabilityDate")]
        public string FirstAvailabilityDate { get; set; } = string.Empty;

        [JsonPropertyName("endOfService")]
        public string EndOfService { get; set; } = string.Empty;

        // Additional fields present in channel release history JSON files
        [JsonPropertyName("releaseDate")]
        public string ReleaseDate { get; set; } = string.Empty;

        [JsonPropertyName("fullBuild")]
        public string FullBuild { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents metadata information about the data update
    /// </summary>
    public class DataForNerds
    {
        [JsonPropertyName("LastUpdatedUTC")]
        public string LastUpdatedUTC { get; set; } = string.Empty;

        [JsonPropertyName("Source Page")]
        public List<string> SourcePage { get; set; } = new();

        [JsonPropertyName("TimeElapsed (ms)")]
        public int TimeElapsedMs { get; set; }
    }

    /// <summary>
    /// Root object containing Office 365 versions data and metadata
    /// </summary>
    public class Office365VersionsData
    {
        [JsonPropertyName("DataForNerds")]
        public DataForNerds DataForNerds { get; set; } = new();

        [JsonPropertyName("data")]
        public List<Office365Version> Data { get; set; } = new();
    }

    /// <summary>
    /// Settings/configuration response model
    /// </summary>
    public class AppSettings
    {
        public string GoogleTag { get; set; } = string.Empty;
    }
}