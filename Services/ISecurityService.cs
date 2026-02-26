using OfficeVersionsCore.Models;

namespace OfficeVersionsCore.Services;

/// <summary>
/// Service for security monitoring and IP blocking
/// </summary>
public interface ISecurityService
{
    /// <summary>
    /// Checks whether the given IP address is currently blocked.
    /// </summary>
    bool IsBlocked(string ipAddress);

    /// <summary>
    /// Records every request. Tracks per-IP and global request rates, error rates,
    /// and triggers scanner detection (404-based auto-blocking).
    /// Returns true if the IP was auto-blocked as a result.
    /// </summary>
    bool RecordRequest(string ipAddress, string path, int statusCode, string userAgent);

    /// <summary>
    /// Records a suspicious request from the given IP and returns true if the IP
    /// was auto-blocked as a result.
    /// </summary>
    bool RecordSuspiciousRequest(string ipAddress, string path, string method, string userAgent, SecurityThreatType threatType, string description);

    /// <summary>
    /// Records an authentication failure for the given IP (and optional username).
    /// Returns true if the IP was auto-blocked due to too many auth failures.
    /// </summary>
    bool RecordAuthFailure(string ipAddress, string? username = null);

    /// <summary>
    /// Manually blocks an IP address (permanent or for a specified duration).
    /// </summary>
    void BlockIp(string ipAddress, string reason, TimeSpan? duration = null, bool isManual = false);

    /// <summary>
    /// Removes a block for the given IP address.
    /// </summary>
    void UnblockIp(string ipAddress);

    /// <summary>
    /// Returns the current aggregated security metrics.
    /// </summary>
    SecurityMetrics GetMetrics();

    /// <summary>
    /// Returns the current security status including all blocked IPs and recent events.
    /// </summary>
    SecurityStatus GetStatus();

    /// <summary>
    /// Adds an alert to the in-memory alert list (called by the monitoring background service).
    /// </summary>
    void RecordAlert(SecurityAlert alert);

    /// <summary>
    /// Resets the global metrics window (called periodically by the monitoring background service).
    /// </summary>
    void ResetMetricsWindow();
}
