using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages
{
    /// <summary>
    /// Page model for 404 Not Found error page
    /// </summary>
    public class Error404Model : PageModel
    {
        private readonly ILogger<Error404Model> _logger;

        public Error404Model(ILogger<Error404Model> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            _logger.LogWarning("404 Error - Page not found: {Path}", HttpContext.Request.Path);
        }
    }
}
