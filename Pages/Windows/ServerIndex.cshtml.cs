using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages.Windows
{
    public class ServerIndexModel : PageModel
    {
        private readonly ILogger<ServerIndexModel> _logger;

        public ServerIndexModel(ILogger<ServerIndexModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            _logger.LogInformation("Windows Server Index page visited");
        }
    }
}
