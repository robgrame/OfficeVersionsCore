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
    /// Records a suspicious request from the given IP and returns true if the IP
    /// was auto-blocked as a result.
    /// </summary>
    bool RecordSuspiciousRequest(string ipAddress, string path, string method, string userAgent, SecurityThreatType threatType, string description);

    /// <summary>
    /// Manually blocks an IP address (permanent or for a specified duration).
    /// </summary>
    void BlockIp(string ipAddress, string reason, TimeSpan? duration = null, bool isManual = false);

    /// <summary>
    /// Removes a block for the given IP address.
    /// </summary>
    void UnblockIp(string ipAddress);

    /// <summary>
    /// Returns the current security status including all blocked IPs and recent events.
    /// </summary>
    SecurityStatus GetStatus();
}
