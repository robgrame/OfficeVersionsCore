using OfficeVersionsCore.Models;
using OfficeVersionsCore.Services;

namespace OfficeVersionsCore.Infrastructure;

/// <summary>
/// Middleware that blocks requests from known-bad IPs and detects vulnerability-scanning
/// patterns (common exploit paths, malicious user-agents, SQL injection, honeypot access, etc.).
/// All requests are recorded for rate and error tracking.
/// </summary>
public class SecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityMiddleware> _logger;
    private readonly bool _enabled;
    private readonly bool _sqlInjectionDetectionEnabled;
    private readonly bool _pathTraversalDetectionEnabled;
    private readonly bool _botDetectionEnabled;
    private readonly string[] _honeypotExtensions;

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

    // SQL injection pattern fragments (simple heuristic, not exhaustive)
    private static readonly string[] SqlInjectionPatterns =
    [
        "' or '",
        "' or 1=1",
        "\" or \"",
        " or 1=1",
        " or 1=1--",
        "' or 'x'='x",
        " union select",
        " union all select",
        "drop table",
        "insert into",
        "delete from",
        "exec(",
        "execute(",
        "xp_cmdshell",
        "'; --",
        "';--",
        "%27 or",
        "%27or",
        "0x",           // hex encoding common in SQLi
        "char(0x",
        "cast(",
        "convert(",
        "information_schema",
        "sys.tables",
        "load_file(",
        "into outfile",
    ];

    public SecurityMiddleware(RequestDelegate next, ILogger<SecurityMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _enabled = configuration.GetValue<bool>("SecurityMonitoring:Enabled", true);
        _sqlInjectionDetectionEnabled = configuration.GetValue<bool>("SecurityMonitoring:SuspiciousActivity:EnableSqlInjectionDetection", true);
        _pathTraversalDetectionEnabled = configuration.GetValue<bool>("SecurityMonitoring:SuspiciousActivity:EnablePathTraversalDetection", true);
        _botDetectionEnabled = configuration.GetValue<bool>("SecurityMonitoring:SuspiciousActivity:EnableBotDetection", true);
        _honeypotExtensions = configuration
            .GetSection("SecurityMonitoring:Honeypot:SuspiciousExtensions")
            .Get<string[]>() ?? [".php", ".asp", ".aspx", ".jsp", ".cgi"];
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
        var queryString = context.Request.QueryString.Value ?? string.Empty;
        var method = context.Request.Method;
        var userAgent = context.Request.Headers.UserAgent.ToString();

        // 2. Honeypot: request to a suspicious file extension (e.g. .php on a .NET app)
        if (IsHoneypotRequest(path))
        {
            var autoBlocked = securityService.RecordSuspiciousRequest(
                ipAddress, path, method, userAgent,
                SecurityThreatType.HoneypotAccess,
                $"Honeypot access – suspicious extension in path: {path}");

            context.Response.StatusCode = autoBlocked
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status404NotFound;
            if (autoBlocked)
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "Forbidden", message = "Access denied." });
            }
            return;
        }

        // 3. Check for suspicious path (known scanner probes)
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

        // 4. Path traversal detection
        if (_pathTraversalDetectionEnabled && ContainsPathTraversal(path + queryString))
        {
            var autoBlocked = securityService.RecordSuspiciousRequest(
                ipAddress, path, method, userAgent,
                SecurityThreatType.PathTraversal,
                $"Path traversal attempt detected: {TruncateString(path + queryString, 200)}");

            context.Response.StatusCode = autoBlocked
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status400BadRequest;
            if (autoBlocked)
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "Forbidden", message = "Access denied." });
            }
            return;
        }

        // 5. SQL injection detection in query string
        if (_sqlInjectionDetectionEnabled && ContainsSqlInjection(queryString))
        {
            var autoBlocked = securityService.RecordSuspiciousRequest(
                ipAddress, path, method, userAgent,
                SecurityThreatType.SqlInjection,
                $"SQL injection attempt detected in query: {TruncateString(queryString, 200)}");

            context.Response.StatusCode = autoBlocked
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status400BadRequest;
            if (autoBlocked)
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "Forbidden", message = "Access denied." });
            }
            return;
        }

        // 6. Check for suspicious User-Agent (bot detection)
        if (_botDetectionEnabled && IsSuspiciousUserAgent(userAgent))
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

        // 7. Pass through and record the response status for metrics.
        // NOTE: Recording happens after the response so we capture the real HTTP status code
        // (needed for 404-based scanner detection). A mid-stream auto-block will take effect
        // on the IP's next request. This is an intentional trade-off.
        await _next(context);

        // Record request after pipeline completes so we have the real status code
        var blocked = securityService.RecordRequest(ipAddress, path, context.Response.StatusCode, userAgent);
        if (blocked)
        {
            // IP was just auto-blocked mid-stream; log but don't change already-sent response
            _logger.LogWarning("IP {IpAddress} auto-blocked after response was already sent for {Path}", ipAddress, path);
        }
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

    private bool IsHoneypotRequest(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var ext = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(ext)
            && _honeypotExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
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

        return false;
    }

    private static bool ContainsPathTraversal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Contains("../") || value.Contains("..\\")
            || value.Contains("%2e%2e", StringComparison.OrdinalIgnoreCase)
            || value.Contains("%252e", StringComparison.OrdinalIgnoreCase)
            || value.Contains("..%2f", StringComparison.OrdinalIgnoreCase)
            || value.Contains("..%5c", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsSqlInjection(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Decode up to 3 times to catch double/triple URL-encoded attack payloads
        var decoded = value;
        for (var i = 0; i < 3; i++)
        {
            try
            {
                var next = Uri.UnescapeDataString(decoded);
                if (next == decoded) break; // no more encoding
                decoded = next;
            }
            catch (UriFormatException)
            {
                break; // malformed encoding — stop decoding
            }
        }

        decoded = decoded.ToLowerInvariant();

        foreach (var pattern in SqlInjectionPatterns)
        {
            if (decoded.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

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
