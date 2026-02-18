using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages.Windows
{
    public class Server2025Model : PageModel
    {
        private readonly ILogger<Server2025Model> _logger;

        public Server2025Model(ILogger<Server2025Model> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            _logger.LogInformation("Windows Server 2025 Releases page visited");
        }
    }
}
