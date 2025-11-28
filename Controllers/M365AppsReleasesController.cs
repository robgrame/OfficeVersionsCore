using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OfficeVersionsCore.Services;
using OfficeVersionsCore.Models;

namespace OfficeVersionsCore.Controllers
{
    /// <summary>
    /// API Controller for Office 365 release data
    /// Replaces the GetM365AppsReleases Azure Function
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    [EnableRateLimiting("api-sliding")]  // Apply sliding window rate limiting to all endpoints
    public class M365AppsReleasesController : ControllerBase
    {
        private readonly IOffice365Service _office365Service;
        private readonly ILogger<M365AppsReleasesController> _logger;

        public M365AppsReleasesController(IOffice365Service office365Service, ILogger<M365AppsReleasesController> logger)
        {
            _office365Service = office365Service;
            _logger = logger;
        }

        /// <summary>
        /// Gets all Office 365 versions data
        /// </summary>
        /// <returns>Complete Office 365 versions data with metadata</returns>
        [HttpGet]
        public async Task<ActionResult<Office365VersionsData>> Get()
        {
            try
            {
                _logger.LogInformation("Fetching all Office 365 versions data");

                var data = await _office365Service.GetLatestVersionsAsync();
                
                if (data == null)
                {
                    _logger.LogWarning("No Office 365 versions data available");
                    return NotFound("Office 365 versions data not available");
                }

                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Office 365 versions data");
                return StatusCode(500, "Internal server error occurred while fetching data");
            }
        }

        /// <summary>
        /// Gets versions for a specific channel
        /// </summary>
        /// <param name="channel">The channel name (e.g., "Current Channel", "Monthly Enterprise Channel")</param>
        /// <returns>List of versions for the specified channel</returns>
        [HttpGet("channel/{channel}")]
        public async Task<ActionResult<List<Office365Version>>> GetByChannel(string channel)
        {
            try
            {
                _logger.LogInformation("Fetching Office 365 versions for channel: {Channel}", channel);

                var versions = await _office365Service.GetVersionsByChannelAsync(channel);
                
                if (!versions.Any())
                {
                    _logger.LogWarning("No versions found for channel: {Channel}", channel);
                    return NotFound($"No versions found for channel: {channel}");
                }

                return Ok(versions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving versions for channel: {Channel}", channel);
                return StatusCode(500, "Internal server error occurred while fetching channel data");
            }
        }

        /// <summary>
        /// Gets the latest version for a specific channel
        /// </summary>
        /// <param name="channel">The channel name</param>
        /// <returns>The latest version for the specified channel</returns>
        [HttpGet("channel/{channel}/latest")]
        public async Task<ActionResult<Office365Version>> GetLatestByChannel(string channel)
        {
            try
            {
                _logger.LogInformation("Fetching latest Office 365 version for channel: {Channel}", channel);

                var version = await _office365Service.GetLatestVersionForChannelAsync(channel);
                
                if (version == null)
                {
                    _logger.LogWarning("No latest version found for channel: {Channel}", channel);
                    return NotFound($"No latest version found for channel: {channel}");
                }

                return Ok(version);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving latest version for channel: {Channel}", channel);
                return StatusCode(500, "Internal server error occurred while fetching latest version");
            }
        }

        /// <summary>
        /// Gets just the data array (for DataTables compatibility)
        /// </summary>
        /// <returns>Array of Office 365 versions</returns>
        [HttpGet("data")]
        public async Task<ActionResult<List<Office365Version>>> GetData()
        {
            try
            {
                _logger.LogInformation("Fetching Office 365 versions data array");

                var data = await _office365Service.GetLatestVersionsAsync();
                
                if (data?.Data == null)
                {
                    _logger.LogWarning("No Office 365 versions data available");
                    return NotFound("Office 365 versions data not available");
                }

                return Ok(data.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Office 365 versions data array");
                return StatusCode(500, "Internal server error occurred while fetching data");
            }
        }
    }
}