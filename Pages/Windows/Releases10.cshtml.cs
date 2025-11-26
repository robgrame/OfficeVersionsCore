using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages.Windows
{
    public class Releases10Model : PageModel
    {
        private readonly ILogger<Releases10Model> _logger;

        public Releases10Model(ILogger<Releases10Model> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            _logger.LogInformation("Windows 10 Releases page visited");
        }
    }
}
