using System.Net;

namespace OfficeVersionsCore.Infrastructure
{
    /// <summary>
    /// Middleware that restricts direct access to API endpoints (/api/*).
    /// When enabled, only requests proxied through Azure API Management (carrying the correct gateway header) are allowed.
    /// Razor Pages and static files are unaffected.
    /// </summary>
    public class ApiGatewayMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiGatewayMiddleware> _logger;
        private readonly string _gatewayKey;
        private readonly string _headerName;
        private readonly bool _enforceGateway;

        public ApiGatewayMiddleware(
            RequestDelegate next,
            ILogger<ApiGatewayMiddleware> logger,
            IConfiguration configuration)
        {
            _next = next;
            _logger = logger;
            _gatewayKey = configuration["ApiManagement:GatewayKey"] ?? string.Empty;
            _headerName = configuration["ApiManagement:GatewayHeaderName"] ?? "X-APIM-Gateway-Key";
            _enforceGateway = configuration.GetValue<bool>("ApiManagement:EnforceGateway", false);
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Only enforce on /api/* paths
            if (_enforceGateway
                && context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                var headerValue = context.Request.Headers[_headerName].FirstOrDefault();

                if (string.IsNullOrEmpty(headerValue) || headerValue != _gatewayKey)
                {
                    _logger.LogWarning(
                        "Direct API access blocked from {IP} to {Path}",
                        context.Connection.RemoteIpAddress,
                        context.Request.Path);

                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "Forbidden",
                        message = "Direct API access is not allowed. Please use the API gateway."
                    });
                    return;
                }
            }

            await _next(context);
        }
    }
}
