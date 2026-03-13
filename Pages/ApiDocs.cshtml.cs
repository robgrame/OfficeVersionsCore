using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages
{
    public class ApiDocsModel : PageModel
    {
        public string PublicApiKey { get; } = "1d55d59bc0c94895ab76348e1fb3a24e";
        public string ApiBaseUrl { get; } = "https://api.office365versions.com";

        public void OnGet()
        {
        }
    }
}
