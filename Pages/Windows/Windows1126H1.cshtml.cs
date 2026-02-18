using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages.Windows
{
    public class Windows1126H1Model : PageModel
    {
        private readonly ILogger<Windows1126H1Model> _logger;

        public Windows1126H1Model(ILogger<Windows1126H1Model> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            _logger.LogInformation("Windows 11 26H1 releases page visited");
        }
    }
}
