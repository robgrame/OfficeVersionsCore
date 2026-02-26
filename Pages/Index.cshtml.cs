using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly IConfiguration _configuration;

    public string BuyMeACoffeeUrl { get; private set; } = string.Empty;
    public bool BuyMeACoffeeEnabled { get; private set; }

    public IndexModel(ILogger<IndexModel> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public void OnGet()
    {
        BuyMeACoffeeUrl = _configuration["BuyMeACoffee:Url"] ?? "https://buymeacoffee.com/office365versions";
        BuyMeACoffeeEnabled = _configuration.GetValue<bool>("BuyMeACoffee:Enabled", false);
        
        // Log the configuration value for debugging
        _logger.LogInformation($"BuyMeACoffee:Enabled = {BuyMeACoffeeEnabled}");
    }
}
