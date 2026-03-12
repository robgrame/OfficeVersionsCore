using System.Net;

namespace OfficeVersionsCore.Infrastructure
{
    /// <summary>
    /// Middleware that restricts direct external access to API endpoints (/api/*).
    /// Allows requests that either:
    ///   1. Come through Azure API Management (carry the correct gateway header), OR
    ///   2. Are same-origin browser requests (Referer matches the app's own host) — needed for Razor Pages fetch() calls.
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
                // Allow if the request carries a valid APIM gateway key
                var headerValue = context.Request.Headers[_headerName].FirstOrDefault();
                if (!string.IsNullOrEmpty(headerValue) && headerValue == _gatewayKey)
                {
                    await _next(context);
                    return;
                }

                // Allow same-origin browser requests (Razor Pages JavaScript fetch calls)
                if (IsSameOriginRequest(context))
                {
                    await _next(context);
                    return;
                }

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

            await _next(context);
        }

        private static bool IsSameOriginRequest(HttpContext context)
        {
            var requestHost = context.Request.Host.Host;

            // Check Referer header (set by browsers on fetch/XHR from same page)
            var referer = context.Request.Headers.Referer.FirstOrDefault();
            if (!string.IsNullOrEmpty(referer)
                && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri)
                && string.Equals(refererUri.Host, requestHost, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Check Origin header (set by browsers on CORS/fetch requests)
            var origin = context.Request.Headers.Origin.FirstOrDefault();
            if (!string.IsNullOrEmpty(origin)
                && Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
                && string.Equals(originUri.Host, requestHost, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}
