using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages.Windows
{
    public class Releases11Model : PageModel
    {
        private readonly ILogger<Releases11Model> _logger;

        public Releases11Model(ILogger<Releases11Model> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            _logger.LogInformation("Windows 11 Releases page visited");
        }
    }
}
