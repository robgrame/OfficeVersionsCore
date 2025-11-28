using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    private readonly ILogger<ErrorModel> _logger;

    public ErrorModel(ILogger<ErrorModel> logger)
    {
        _logger = logger;
    }

    public void OnGet()
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        
        // Log the error details for diagnostics
        var exceptionFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        if (exceptionFeature != null)
        {
            _logger.LogError(exceptionFeature.Error, 
                "Error occurred for request {RequestId}: {ErrorMessage}", 
                RequestId, 
                exceptionFeature.Error.Message);
        }
        else
        {
            var statusCodeFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodeReExecuteFeature>();
            if (statusCodeFeature != null)
            {
                _logger.LogWarning(
                    "Status code {StatusCode} for path {OriginalPath}", 
                    HttpContext.Response.StatusCode,
                    statusCodeFeature.OriginalPath);
            }
        }
    }
}

