namespace OfficeVersionsCore.Models;

/// <summary>
/// Represents a blocked IP address entry
/// </summary>
public class BlockedIp
{
    /// <summary>IP address that is blocked</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>Reason the IP was blocked</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>When the IP was blocked (UTC)</summary>
    public DateTime BlockedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the block expires (UTC); null = permanent</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Whether the block was set manually by an admin (vs. auto-blocked)</summary>
    public bool IsManual { get; set; }

    /// <summary>Number of suspicious requests that triggered the auto-block</summary>
    public int SuspiciousRequestCount { get; set; }

    /// <summary>Whether this block entry is still active</summary>
    public bool IsActive => ExpiresAt == null || ExpiresAt > DateTime.UtcNow;
}

/// <summary>
/// Represents a security event for a single request
/// </summary>
public class SecurityEvent
{
    /// <summary>IP address of the requester</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>Requested path</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>HTTP method</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>User-Agent header</summary>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>Type of threat detected</summary>
    public SecurityThreatType ThreatType { get; set; }

    /// <summary>When the event occurred (UTC)</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Human-readable description of the threat</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Type of security threat detected
/// </summary>
public enum SecurityThreatType
{
    SuspiciousPath,
    SuspiciousUserAgent,
    ExcessiveRequests,
    ManualBlock
}

/// <summary>
/// Summary of the current security monitoring state
/// </summary>
public class SecurityStatus
{
    /// <summary>Total number of currently blocked IPs</summary>
    public int BlockedIpCount { get; set; }

    /// <summary>Total security events recorded since startup</summary>
    public int TotalEventsRecorded { get; set; }

    /// <summary>All currently active blocked IPs</summary>
    public IList<BlockedIp> BlockedIps { get; set; } = [];

    /// <summary>Recent security events (up to last 100)</summary>
    public IList<SecurityEvent> RecentEvents { get; set; } = [];
}
