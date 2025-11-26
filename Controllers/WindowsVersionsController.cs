using Microsoft.AspNetCore.Mvc;
using OfficeVersionsCore.Services;
using OfficeVersionsCore.Models;

namespace OfficeVersionsCore.Controllers
{
    /// <summary>
    /// API Controller for Windows release data
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class WindowsVersionsController : ControllerBase
    {
        private readonly IWindowsVersionsService _windowsService;
        private readonly ILogger<WindowsVersionsController> _logger;

        public WindowsVersionsController(IWindowsVersionsService windowsService, ILogger<WindowsVersionsController> logger)
        {
            _windowsService = windowsService;
            _logger = logger;
        }

        /// <summary>
        /// Gets aggregated Windows versions data from all editions
        /// </summary>
        /// <returns>Aggregated Windows versions data</returns>
        [HttpGet]
        [HttpGet("data")]
        public async Task<ActionResult<object>> GetAllVersionsData()
        {
            try
            {
                _logger.LogInformation("Fetching aggregated Windows versions data");

                // Fetch data for both Windows 10 and 11
                var win10Response = await _windowsService.GetWindowsVersionsAsync(WindowsEdition.Windows10);
                var win11Response = await _windowsService.GetWindowsVersionsAsync(WindowsEdition.Windows11);

                var allVersions = new List<object>();

                // Process Windows 10 data
                if (win10Response.Success && win10Response.Data != null)
                {
                    foreach (var version in win10Response.Data)
                    {
                        var productName = _windowsService.GenerateProductName(
                            WindowsEdition.Windows10, 
                            version.Version, 
                            version.ServicingType);

                        allVersions.Add(new
                        {
                            product = productName,
                            latestVersion = version.Version,
                            latestBuild = version.Build,
                            latestReleaseDate = version.ReleaseDate,
                            servicingChannels = !string.IsNullOrEmpty(version.ServiceOption) 
                                ? new[] { version.ServiceOption } 
                                : Array.Empty<string>()
                        });
                    }
                }

                // Process Windows 11 data
                if (win11Response.Success && win11Response.Data != null)
                {
                    foreach (var version in win11Response.Data)
                    {
                        var productName = _windowsService.GenerateProductName(
                            WindowsEdition.Windows11, 
                            version.Version, 
                            version.ServicingType);

                        allVersions.Add(new
                        {
                            product = productName,
                            latestVersion = version.Version,
                            latestBuild = version.Build,
                            latestReleaseDate = version.ReleaseDate,
                            servicingChannels = !string.IsNullOrEmpty(version.ServiceOption) 
                                ? new[] { version.ServiceOption } 
                                : Array.Empty<string>()
                        });
                    }
                }

                // Sort by release date descending and take only latest per product
                var latestByProduct = allVersions
                    .GroupBy(v => ((dynamic)v).product)
                    .Select(g => g.OrderByDescending(v => ((dynamic)v).latestReleaseDate).First())
                    .OrderByDescending(v => ((dynamic)v).latestReleaseDate)
                    .ToList();

                // Get last update time
                var lastUpdate = await _windowsService.GetLastUpdateTimeAsync();

                // Return in format expected by DataTable
                var result = new
                {
                    data = latestByProduct,
                    dataForNerds = new
                    {
                        lastUpdatedUTC = lastUpdate?.ToString("o") ?? DateTime.UtcNow.ToString("o"),
                        source = "API",
                        timestamp = DateTime.UtcNow
                    }
                };

                _logger.LogInformation("Aggregated Windows versions data retrieved successfully with {Count} products", latestByProduct.Count);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving aggregated Windows versions data");
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Internal server error: {ex.Message}",
                    data = new List<object>()
                });
            }
        }

        /// <summary>
        /// Gets all Windows versions for a specific edition
        /// </summary>
        /// <param name="edition">The Windows edition (Windows10 or Windows11)</param>
        /// <returns>Complete Windows versions data</returns>
        [HttpGet("{edition}")]
        public async Task<ActionResult<ApiResponse<List<WindowsVersion>>>> GetVersions(WindowsEdition edition)
        {
            try
            {
                _logger.LogInformation("Fetching all Windows versions for {Edition}", edition);

                var response = await _windowsService.GetWindowsVersionsAsync(edition);

                if (!response.Success || response.Data == null)
                {
                    _logger.LogWarning("No Windows versions data available for {Edition}", edition);
                    return NotFound(response);
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Windows versions for {Edition}", edition);
                return StatusCode(500, new ApiResponse<List<WindowsVersion>>
                {
                    Success = false,
                    Message = $"Internal server error: {ex.Message}",
                    Data = null,
                    Timestamp = DateTime.UtcNow,
                    Source = "API"
                });
            }
        }

        /// <summary>
        /// Gets all Windows releases for display in the releases table
        /// </summary>
        /// <returns>Complete list of all releases from both Windows editions</returns>
        [HttpGet("releases")]
        public async Task<ActionResult<List<object>>> GetAllReleases()
        {
            try
            {
                _logger.LogInformation("Fetching all Windows releases");

                var releases = new List<object>();

                // Fetch updates for both Windows 10 and 11
                var win10UpdatesResponse = await _windowsService.GetWindowsUpdatesAsync(WindowsEdition.Windows10);
                var win11UpdatesResponse = await _windowsService.GetWindowsUpdatesAsync(WindowsEdition.Windows11);

                // Process Windows 10 updates
                if (win10UpdatesResponse.Success && win10UpdatesResponse.Data != null)
                {
                    foreach (var update in win10UpdatesResponse.Data)
                    {
                        releases.Add(new
                        {
                            product = "Windows 10",
                            version = update.Version,
                            buildNumber = update.Build,
                            releaseDate = update.ReleaseDate,
                            servicingOption = update.Edition.ToString(),
                            kb = update.KBNumber,
                            url = string.Empty,
                            updateTitle = update.UpdateTitle,
                            isSecurityUpdate = update.IsSecurityUpdate
                        });
                    }
                }

                // Process Windows 11 updates
                if (win11UpdatesResponse.Success && win11UpdatesResponse.Data != null)
                {
                    foreach (var update in win11UpdatesResponse.Data)
                    {
                        releases.Add(new
                        {
                            product = "Windows 11",
                            version = update.Version,
                            buildNumber = update.Build,
                            releaseDate = update.ReleaseDate,
                            servicingOption = update.Edition.ToString(),
                            kb = update.KBNumber,
                            url = string.Empty,
                            updateTitle = update.UpdateTitle,
                            isSecurityUpdate = update.IsSecurityUpdate
                        });
                    }
                }

                // Sort by release date descending
                var sortedReleases = releases
                    .OrderByDescending(r => ((dynamic)r).releaseDate)
                    .ToList();

                _logger.LogInformation("All Windows releases retrieved successfully with {Count} items", sortedReleases.Count);
                return Ok(sortedReleases);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all Windows releases");
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Internal server error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Gets Windows 10 releases for display in the releases table
        /// </summary>
        /// <returns>List of Windows 10 releases only</returns>
        [HttpGet("windows10/releases")]
        public async Task<ActionResult<List<object>>> GetWindows10Releases()
        {
            try
            {
                _logger.LogInformation("Fetching Windows 10 releases");

                var releases = new List<object>();

                var win10UpdatesResponse = await _windowsService.GetWindowsUpdatesAsync(WindowsEdition.Windows10);

                if (win10UpdatesResponse.Success && win10UpdatesResponse.Data != null)
                {
                    foreach (var update in win10UpdatesResponse.Data)
                    {
                        releases.Add(new
                        {
                            version = update.Version,
                            buildNumber = update.Build,
                            releaseDate = update.ReleaseDate,
                            servicingOption = update.Edition.ToString(),
                            kb = update.KBNumber,
                            url = string.Empty,
                            updateTitle = update.UpdateTitle,
                            isSecurityUpdate = update.IsSecurityUpdate
                        });
                    }
                }

                // Sort by release date descending
                var sortedReleases = releases
                    .OrderByDescending(r => ((dynamic)r).releaseDate)
                    .ToList();

                _logger.LogInformation("Windows 10 releases retrieved successfully with {Count} items", sortedReleases.Count);
                return Ok(sortedReleases);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Windows 10 releases");
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Internal server error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Gets Windows 11 releases for display in the releases table
        /// </summary>
        /// <returns>List of Windows 11 releases only</returns>
        [HttpGet("windows11/releases")]
        public async Task<ActionResult<List<object>>> GetWindows11Releases()
        {
            try
            {
                _logger.LogInformation("Fetching Windows 11 releases");

                var releases = new List<object>();

                var win11UpdatesResponse = await _windowsService.GetWindowsUpdatesAsync(WindowsEdition.Windows11);

                if (win11UpdatesResponse.Success && win11UpdatesResponse.Data != null)
                {
                    foreach (var update in win11UpdatesResponse.Data)
                    {
                        releases.Add(new
                        {
                            version = update.Version,
                            buildNumber = update.Build,
                            releaseDate = update.ReleaseDate,
                            servicingOption = update.Edition.ToString(),
                            kb = update.KBNumber,
                            url = string.Empty,
                            updateTitle = update.UpdateTitle,
                            isSecurityUpdate = update.IsSecurityUpdate
                        });
                    }
                }

                // Sort by release date descending
                var sortedReleases = releases
                    .OrderByDescending(r => ((dynamic)r).releaseDate)
                    .ToList();

                _logger.LogInformation("Windows 11 releases retrieved successfully with {Count} items", sortedReleases.Count);
                return Ok(sortedReleases);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Windows 11 releases");
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Internal server error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Gets all Windows updates for a specific edition
        /// </summary>
        /// <param name="edition">The Windows edition (Windows10 or Windows11)</param>
        /// <returns>List of Windows updates</returns>
        [HttpGet("{edition}/updates")]
        public async Task<ActionResult<ApiResponse<List<WindowsUpdate>>>> GetUpdates(WindowsEdition edition)
        {
            try
            {
                _logger.LogInformation("Fetching Windows updates for {Edition}", edition);

                var response = await _windowsService.GetWindowsUpdatesAsync(edition);

                if (!response.Success || response.Data == null)
                {
                    _logger.LogWarning("No Windows updates available for {Edition}", edition);
                    return NotFound(response);
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Windows updates for {Edition}", edition);
                return StatusCode(500, new ApiResponse<List<WindowsUpdate>>
                {
                    Success = false,
                    Message = $"Internal server error: {ex.Message}",
                    Data = null,
                    Timestamp = DateTime.UtcNow,
                    Source = "API"
                });
            }
        }

        /// <summary>
        /// Gets release summary for a specific edition
        /// </summary>
        /// <param name="edition">The Windows edition (Windows10 or Windows11)</param>
        /// <returns>Release summary with statistics</returns>
        [HttpGet("{edition}/summary")]
        public async Task<ActionResult<ApiResponse<WindowsReleaseSummary>>> GetSummary(WindowsEdition edition)
        {
            try
            {
                _logger.LogInformation("Fetching release summary for {Edition}", edition);

                var response = await _windowsService.GetReleaseSummaryAsync(edition);

                if (!response.Success || response.Data == null)
                {
                    _logger.LogWarning("No release summary available for {Edition}", edition);
                    return NotFound(response);
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving release summary for {Edition}", edition);
                return StatusCode(500, new ApiResponse<WindowsReleaseSummary>
                {
                    Success = false,
                    Message = $"Internal server error: {ex.Message}",
                    Data = null,
                    Timestamp = DateTime.UtcNow,
                    Source = "API"
                });
            }
        }

        /// <summary>
        /// Gets the latest version for a specific edition
        /// </summary>
        /// <param name="edition">The Windows edition (Windows10 or Windows11)</param>
        /// <returns>The latest version information</returns>
        [HttpGet("{edition}/latest")]
        public async Task<ActionResult<ApiResponse<WindowsVersion>>> GetLatest(WindowsEdition edition)
        {
            try
            {
                _logger.LogInformation("Fetching latest version for {Edition}", edition);

                var response = await _windowsService.GetLatestVersionAsync(edition);

                if (!response.Success || response.Data == null)
                {
                    _logger.LogWarning("No latest version found for {Edition}", edition);
                    return NotFound(response);
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving latest version for {Edition}", edition);
                return StatusCode(500, new ApiResponse<WindowsVersion>
                {
                    Success = false,
                    Message = $"Internal server error: {ex.Message}",
                    Data = null,
                    Timestamp = DateTime.UtcNow,
                    Source = "API"
                });
            }
        }

        /// <summary>
        /// Gets recent updates for a specific edition
        /// </summary>
        /// <param name="edition">The Windows edition (Windows10 or Windows11)</param>
        /// <param name="count">Number of recent updates to return (default: 10)</param>
        /// <returns>List of recent updates</returns>
        [HttpGet("{edition}/recent")]
        public async Task<ActionResult<ApiResponse<List<WindowsUpdate>>>> GetRecent(WindowsEdition edition, [FromQuery] int count = 10)
        {
            try
            {
                _logger.LogInformation("Fetching {Count} recent updates for {Edition}", count, edition);

                var response = await _windowsService.GetRecentUpdatesAsync(edition, count);

                if (!response.Success || response.Data == null)
                {
                    _logger.LogWarning("No recent updates found for {Edition}", edition);
                    return NotFound(response);
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving recent updates for {Edition}", edition);
                return StatusCode(500, new ApiResponse<List<WindowsUpdate>>
                {
                    Success = false,
                    Message = $"Internal server error: {ex.Message}",
                    Data = null,
                    Timestamp = DateTime.UtcNow,
                    Source = "API"
                });
            }
        }

        /// <summary>
        /// Gets feature updates for a specific edition
        /// </summary>
        /// <param name="edition">The Windows edition (Windows10 or Windows11)</param>
        /// <returns>List of feature updates</returns>
        [HttpGet("{edition}/feature-updates")]
        public async Task<ActionResult<ApiResponse<List<WindowsFeatureUpdate>>>> GetFeatureUpdates(WindowsEdition edition)
        {
            try
            {
                _logger.LogInformation("Fetching feature updates for {Edition}", edition);

                var response = await _windowsService.GetFeatureUpdatesAsync(edition);

                if (!response.Success || response.Data == null)
                {
                    _logger.LogWarning("No feature updates found for {Edition}", edition);
                    return NotFound(response);
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving feature updates for {Edition}", edition);
                return StatusCode(500, new ApiResponse<List<WindowsFeatureUpdate>>
                {
                    Success = false,
                    Message = $"Internal server error: {ex.Message}",
                    Data = null,
                    Timestamp = DateTime.UtcNow,
                    Source = "API"
                });
            }
        }

        /// <summary>
        /// Compares two Windows versions
        /// </summary>
        /// <param name="version1">First version to compare</param>
        /// <param name="version2">Second version to compare</param>
        /// <returns>Comparison result</returns>
        [HttpGet("compare")]
        public async Task<ActionResult<ApiResponse<VersionComparison>>> CompareVersions([FromQuery] string version1, [FromQuery] string version2)
        {
            try
            {
                _logger.LogInformation("Comparing versions {Version1} and {Version2}", version1, version2);

                if (string.IsNullOrWhiteSpace(version1) || string.IsNullOrWhiteSpace(version2))
                {
                    return BadRequest(new ApiResponse<VersionComparison>
                    {
                        Success = false,
                        Message = "Both version1 and version2 parameters are required",
                        Data = null,
                        Timestamp = DateTime.UtcNow,
                        Source = "API"
                    });
                }

                var response = await _windowsService.CompareVersionsAsync(version1, version2);

                if (!response.Success || response.Data == null)
                {
                    _logger.LogWarning("Version comparison failed for {Version1} and {Version2}", version1, version2);
                    return NotFound(response);
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error comparing versions {Version1} and {Version2}", version1, version2);
                return StatusCode(500, new ApiResponse<VersionComparison>
                {
                    Success = false,
                    Message = $"Internal server error: {ex.Message}",
                    Data = null,
                    Timestamp = DateTime.UtcNow,
                    Source = "API"
                });
            }
        }

        /// <summary>
        /// Gets the last update timestamp
        /// </summary>
        /// <returns>The last time data was updated</returns>
        [HttpGet("last-update")]
        public async Task<ActionResult<object>> GetLastUpdate()
        {
            try
            {
                _logger.LogInformation("Fetching last update time");

                var lastUpdate = await _windowsService.GetLastUpdateTimeAsync();

                return Ok(new
                {
                    success = true,
                    lastUpdate = lastUpdate,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving last update time");
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Internal server error: {ex.Message}",
                    timestamp = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Manually triggers a data refresh (admin use)
        /// </summary>
        /// <returns>Result of refresh operation</returns>
        [HttpPost("refresh")]
        public async Task<ActionResult<object>> RefreshData()
        {
            try
            {
                _logger.LogInformation("Manual data refresh triggered");

                var success = await _windowsService.RefreshDataAsync();

                return Ok(new
                {
                    success,
                    message = success ? "Data refresh completed successfully" : "Data refresh failed",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during manual data refresh");
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Internal server error: {ex.Message}",
                    timestamp = DateTime.UtcNow
                });
            }
        }
    }
}
