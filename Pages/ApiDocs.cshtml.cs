using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages
{
    public class ApiDocsModel : PageModel
    {
        public string PublicApiKey { get; } = "49bf26efd7714a989237922df7d115f9";
        public string ApiBaseUrl { get; } = "https://api.office365versions.com";

        public void OnGet()
        {
        }
    }
}
