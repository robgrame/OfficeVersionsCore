using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages.Windows
{
    public class Server2019Model : PageModel
    {
        private readonly ILogger<Server2019Model> _logger;

        public Server2019Model(ILogger<Server2019Model> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            _logger.LogInformation("Windows Server 2019 Releases page visited");
        }
    }
}
