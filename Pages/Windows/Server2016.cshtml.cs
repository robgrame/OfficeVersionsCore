using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages.Windows
{
    public class Server2016Model : PageModel
    {
        private readonly ILogger<Server2016Model> _logger;

        public Server2016Model(ILogger<Server2016Model> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            _logger.LogInformation("Windows Server 2016 Releases page visited");
        }
    }
}
