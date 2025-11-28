using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages
{
    /// <summary>
    /// Page model for 403 Forbidden error page
    /// </summary>
    public class Error403Model : PageModel
    {
        private readonly ILogger<Error403Model> _logger;

        public Error403Model(ILogger<Error403Model> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            _logger.LogWarning("403 Error - Access forbidden to: {Path}", HttpContext.Request.Path);
        }
    }
}
