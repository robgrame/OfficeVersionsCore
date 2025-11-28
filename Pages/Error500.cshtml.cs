using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages
{
    /// <summary>
    /// Page model for 500 Internal Server Error page
    /// </summary>
    public class Error500Model : PageModel
    {
        private readonly ILogger<Error500Model> _logger;
        private readonly IWebHostEnvironment _env;

        public Error500Model(ILogger<Error500Model> logger, IWebHostEnvironment env)
        {
            _logger = logger;
            _env = env;
        }

        /// <summary>
        /// Whether to show error details (only in development)
        /// </summary>
        public bool ShowDetails => _env.IsDevelopment();

        /// <summary>
        /// Error message to display (only in development)
        /// </summary>
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            _logger.LogError("500 Error - Internal server error at: {Path}", HttpContext.Request.Path);

            // Only show error details in development
            if (_env.IsDevelopment())
            {
                var exceptionFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
                if (exceptionFeature?.Error != null)
                {
                    ErrorMessage = $"{exceptionFeature.Error.GetType().Name}: {exceptionFeature.Error.Message}\n\n{exceptionFeature.Error.StackTrace}";
                }
            }
        }
    }
}
