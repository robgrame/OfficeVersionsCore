using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages.Windows
{
    public class Server2022Model : PageModel
    {
        private readonly ILogger<Server2022Model> _logger;

        public Server2022Model(ILogger<Server2022Model> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            _logger.LogInformation("Windows Server 2022 Releases page visited");
        }
    }
}
