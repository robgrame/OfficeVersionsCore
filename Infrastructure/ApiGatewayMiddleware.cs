using System.Net;

namespace OfficeVersionsCore.Infrastructure
{
    /// <summary>
    /// Middleware that restricts direct external access to API endpoints (/api/*).
    /// Three-layer validation:
    ///   1. APIM gateway key + IP whitelist — for external API consumers via APIM
    ///   2. Same-origin browser requests — for Razor Pages JavaScript fetch() calls
    ///   3. All other direct access is blocked with 403
    /// Razor Pages and static files are unaffected.
    /// </summary>
    public class ApiGatewayMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiGatewayMiddleware> _logger;
        private readonly IConfiguration _configuration;

        public ApiGatewayMiddleware(
            RequestDelegate next,
            ILogger<ApiGatewayMiddleware> logger,
            IConfiguration configuration)
        {
            _next = next;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Read config dynamically so App Config refresh is picked up
            var enforceGateway = _configuration.GetValue<bool>("ApiManagement:EnforceGateway", false);

            // Only enforce on /api/* paths
            if (enforceGateway
                && context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                var gatewayKey = _configuration["ApiManagement:GatewayKey"] ?? string.Empty;
                var headerName = _configuration["ApiManagement:GatewayHeaderName"] ?? "X-APIM-Gateway-Key";

                // Allow if the request carries a valid APIM gateway key AND comes from an allowed IP
                var headerValue = context.Request.Headers[headerName].FirstOrDefault();
                if (!string.IsNullOrEmpty(headerValue) && !string.IsNullOrEmpty(gatewayKey)
                    && headerValue == gatewayKey)
                {
                    var allowedIps = GetAllowedIps();
                    if (allowedIps.Count == 0 || IsFromAllowedIp(context, allowedIps))
                    {
                        await _next(context);
                        return;
                    }

                    _logger.LogWarning(
                        "Valid gateway key but untrusted IP {IP} for {Path}",
                        context.Connection.RemoteIpAddress,
                        context.Request.Path);
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

        private HashSet<string> GetAllowedIps()
        {
            var allowedIps = _configuration.GetSection("ApiManagement:AllowedIPs").Get<string[]>();
            return allowedIps != null
                ? new HashSet<string>(allowedIps, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsFromAllowedIp(HttpContext context, HashSet<string> allowedIps)
        {
            var remoteIp = context.Connection.RemoteIpAddress?.ToString();
            if (string.IsNullOrEmpty(remoteIp))
                return false;

            // Also check X-Forwarded-For (Azure App Service sits behind a load balancer)
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                // X-Forwarded-For may contain multiple IPs; the first is the original client
                var clientIp = forwardedFor.Split(',')[0].Trim();
                if (allowedIps.Contains(clientIp))
                    return true;
            }

            return allowedIps.Contains(remoteIp);
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
