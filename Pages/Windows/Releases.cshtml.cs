using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages.Windows
{
    public class ReleasesModel : PageModel
    {
        private readonly ILogger<ReleasesModel> _logger;

        public ReleasesModel(ILogger<ReleasesModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            _logger.LogInformation("Windows Releases page visited");
        }
    }
}
