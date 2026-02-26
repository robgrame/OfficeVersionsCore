using OfficeVersionsCore.Models;
using OfficeVersionsCore.Services;

namespace OfficeVersionsCore.Infrastructure;

/// <summary>
/// Middleware that blocks requests from known-bad IPs and detects vulnerability-scanning
/// patterns (common exploit paths, malicious user-agents, etc.).
/// </summary>
public class SecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityMiddleware> _logger;
    private readonly bool _enabled;

    // Paths commonly probed by vulnerability scanners / bots
    private static readonly HashSet<string> SuspiciousPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/.env",
        "/.env.local",
        "/.env.production",
        "/.git/config",
        "/.git/HEAD",
        "/wp-admin",
        "/wp-login.php",
        "/wp-config.php",
        "/wp-includes",
        "/xmlrpc.php",
        "/phpmyadmin",
        "/phpmyadmin/index.php",
        "/pma",
        "/adminer",
        "/adminer.php",
        "/admin/config.php",
        "/etc/passwd",
        "/etc/shadow",
        "/proc/self/environ",
        "/config.php",
        "/configuration.php",
        "/laravel/.env",
        "/debug/default/view",
        "/_profiler",
        "/actuator",
        "/actuator/env",
        "/actuator/health",
        "/actuator/info",
        "/console",
        "/manager/html",
        "/jmx-console",
        "/solr/admin",
        "/jenkins",
        "/.DS_Store",
        "/backup.zip",
        "/backup.tar.gz",
        "/db.sql",
        "/database.sql",
        "/dump.sql",
        "/www.zip",
        "/site.tar.gz",
        "/.htpasswd",
        "/.htaccess",
        "/crossdomain.xml",
        "/clientaccesspolicy.xml",
        "/cgi-bin/",
        "/cgi-bin/test-cgi",
        "/cgi-bin/printenv",
        "/shell",
        "/cmd",
        "/boaform/admin/formLogin",
        "/GponForm/diag_Form",
        "/setup.cgi",
        "/HNAP1",
        "/index.php?s=/Index/index",
        "/.well-known/security.txt",
    };

    // Substrings of User-Agent headers associated with scanners/attack tools
    private static readonly string[] SuspiciousUserAgentSubstrings =
    [
        "sqlmap",
        "nikto",
        "nmap",
        "masscan",
        "ZmEu",
        "zgrab",
        "dirbuster",
        "gobuster",
        "wfuzz",
        "hydra",
        "burpsuite",
        "owasp",
        "metasploit",
        "nuclei",
        "nessus",
        "openvas",
        "qualys",
        "acunetix",
        "appscan",
        "webinspect",
        "w3af",
        "havij",
        "python-requests/2.18",   // specific scanner version; not all python-requests are blocked
        "Go-http-client/1.1",     // common in brute-force tools
        "Wget/1.9",
        "libwww-perl",
    ];

    public SecurityMiddleware(RequestDelegate next, ILogger<SecurityMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _enabled = configuration.GetValue<bool>("SecurityMonitoring:Enabled", true);
    }

    public async Task InvokeAsync(HttpContext context, ISecurityService securityService)
    {
        if (!_enabled)
        {
            await _next(context);
            return;
        }

        var ipAddress = GetClientIp(context);

        // 1. Check if IP is already blocked
        if (securityService.IsBlocked(ipAddress))
        {
            _logger.LogWarning("Blocked IP {IpAddress} attempted access to {Path}", ipAddress, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Forbidden",
                message = "Access denied."
            });
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;
        var userAgent = context.Request.Headers.UserAgent.ToString();

        // 2. Check for suspicious path
        if (IsSuspiciousPath(path))
        {
            var autoBlocked = securityService.RecordSuspiciousRequest(
                ipAddress, path, method, userAgent,
                SecurityThreatType.SuspiciousPath,
                $"Request to suspicious path: {path}");

            if (autoBlocked)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "Forbidden", message = "Access denied." });
                return;
            }

            // Return 404 (not 403) to avoid revealing that we detected the scan
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // 3. Check for suspicious User-Agent
        if (IsSuspiciousUserAgent(userAgent))
        {
            var autoBlocked = securityService.RecordSuspiciousRequest(
                ipAddress, path, method, userAgent,
                SecurityThreatType.SuspiciousUserAgent,
                $"Suspicious User-Agent: {TruncateString(userAgent, 200)}");

            if (autoBlocked)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "Forbidden", message = "Access denied." });
                return;
            }
        }

        await _next(context);
    }

    // ---------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------

    private static string GetClientIp(HttpContext context)
    {
        // Azure App Service sets X-Forwarded-For reliably; take the leftmost (client) IP.
        // Ensure the app is deployed behind a trusted proxy to prevent header spoofing.
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            // X-Forwarded-For may contain a comma-separated list; take the first (client) IP
            var ip = forwarded.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(ip))
                return ip;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static bool IsSuspiciousPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Exact match
        if (SuspiciousPaths.Contains(path))
            return true;

        // Prefix match (e.g. /wp-admin/something)
        foreach (var suspiciousPath in SuspiciousPaths)
        {
            if (path.StartsWith(suspiciousPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Check for path-traversal attempts
        if (path.Contains("../") || path.Contains("..\\") || path.Contains("%2e%2e") || path.Contains("%252e"))
            return true;

        return false;
    }

    private static bool IsSuspiciousUserAgent(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return false;

        foreach (var substring in SuspiciousUserAgentSubstrings)
        {
            if (userAgent.Contains(substring, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string TruncateString(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";
}
