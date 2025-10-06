using Microsoft.AspNetCore.Mvc;
using OfficeVersionsCore.Models;

namespace OfficeVersionsCore.Controllers
{
    /// <summary>
    /// API Controller for application settings
    /// Replaces the Settings Azure Function
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class SettingsController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(IConfiguration configuration, ILogger<SettingsController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Gets application settings including Google Tag
        /// </summary>
        /// <returns>Application settings</returns>
        [HttpGet]
        public ActionResult<AppSettings> Get()
        {
            try
            {
                _logger.LogInformation("Fetching application settings");

                var settings = new AppSettings
                {
                    GoogleTag = _configuration["Google:Tag"] ?? string.Empty
                };

                return Ok(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving application settings");
                return StatusCode(500, "Internal server error occurred while fetching settings");
            }
        }

        /// <summary>
        /// Gets the Google Tag specifically
        /// </summary>
        /// <returns>Google Tag value</returns>
        [HttpGet("googletag")]
        public ActionResult<object> GetGoogleTag()
        {
            try
            {
                _logger.LogInformation("Fetching Google Tag setting");

                var googleTag = _configuration["Google:Tag"] ?? string.Empty;

                return Ok(new { googleTag });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Google Tag setting");
                return StatusCode(500, "Internal server error occurred while fetching Google Tag");
            }
        }
    }
}