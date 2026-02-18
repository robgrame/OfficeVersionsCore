using Microsoft.AspNetCore.Mvc.RazorPages;
using OfficeVersionsCore.Services;
using OfficeVersionsCore.Models;

namespace OfficeVersionsCore.Pages.Windows;

/// <summary>
/// Windows Server Updates page model
/// Displays Windows Server release information with support for multiple servicing channels:
/// - Annual Channel (AC): Provides more frequent releases with shorter support lifecycle
/// - Long-Term Servicing Channel (LTSC): Provides longer-term support with less frequent updates
/// 
/// Historical note: Windows Server Semi-Annual Channel (SAC) was retired on August 9, 2022
/// </summary>
public class ServerUpdatesModel : PageModel
{
    private readonly IWindowsVersionsService _windowsService;
    private readonly ILogger<ServerUpdatesModel> _logger;

    public ServerUpdatesModel(IWindowsVersionsService windowsService, ILogger<ServerUpdatesModel> logger)
    {
        _windowsService = windowsService;
        _logger = logger;
    }

    public async Task OnGetAsync()
    {
        try
        {
            _logger.LogInformation("Windows Server Updates page loaded");
            
            // Page content is loaded dynamically via API calls in JavaScript
            // The page supports filtering by version and servicing channel
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Windows Server Updates page");
        }
    }
}
