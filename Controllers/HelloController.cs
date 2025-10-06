using Microsoft.AspNetCore.Mvc;

namespace OfficeVersionsCore.Controllers
{
    /// <summary>
    /// API Controller for Hello functionality
    /// Replaces the Hello Azure Function
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    [ApiExplorerSettings(IgnoreApi = true)] // Exclude this controller from Swagger/API explorer
    public class HelloController : ControllerBase
    {
        private readonly ILogger<HelloController> _logger;

        public HelloController(ILogger<HelloController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Hello endpoint with optional name parameter
        /// </summary>
        /// <param name="name">Optional name for personalized greeting</param>
        /// <returns>Greeting message</returns>
        [HttpGet]
        public ActionResult<object> Get([FromQuery] string? name = null)
        {
            try
            {
                _logger.LogInformation("Hello endpoint called with name: {Name}", name ?? "no name");

                var responseMessage = !string.IsNullOrEmpty(name)
                    ? $"Hello, {name}. This HTTP triggered function executed successfully."
                    : "This HTTP triggered function executed successfully. Pass a name in the query string for a personalized response.";

                return Ok(new { message = responseMessage });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Hello endpoint");
                return StatusCode(500, "Internal server error occurred");
            }
        }

        /// <summary>
        /// Hello endpoint with name in POST body
        /// </summary>
        /// <param name="request">Request containing name</param>
        /// <returns>Greeting message</returns>
        [HttpPost]
        public ActionResult<object> Post([FromBody] HelloRequest? request = null)
        {
            try
            {
                var name = request?.Name;
                _logger.LogInformation("Hello POST endpoint called with name: {Name}", name ?? "no name");

                var responseMessage = !string.IsNullOrEmpty(name)
                    ? $"Hello, {name}. This HTTP triggered function executed successfully."
                    : "This HTTP triggered function executed successfully. Pass a name in the request body for a personalized response.";

                return Ok(new { message = responseMessage });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Hello POST endpoint");
                return StatusCode(500, "Internal server error occurred");
            }
        }
    }

    /// <summary>
    /// Request model for Hello POST endpoint
    /// </summary>
    public class HelloRequest
    {
        public string? Name { get; set; }
    }
}