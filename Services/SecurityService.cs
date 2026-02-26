using System.Collections.Concurrent;
using System.Text.Json;
using OfficeVersionsCore.Models;

namespace OfficeVersionsCore.Services;

/// <summary>
/// In-memory security service that tracks suspicious IPs and auto-blocks them.
/// Persists blocked IPs to a JSON file so blocks survive restarts.
/// </summary>
public class SecurityService : ISecurityService
{
    // --- blocked IPs: IP -> BlockedIp ---
    private readonly ConcurrentDictionary<string, BlockedIp> _blockedIps = new(StringComparer.OrdinalIgnoreCase);

    // --- suspicious request counters per IP ---
    private readonly ConcurrentDictionary<string, int> _suspiciousCounters = new(StringComparer.OrdinalIgnoreCase);

    // --- recent security events (capped at MaxEvents) ---
    private const int MaxEvents = 200;
    private readonly ConcurrentQueue<SecurityEvent> _recentEvents = new();

    private readonly ILogger<SecurityService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _persistencePath;
    private readonly int _autoBlockThreshold;
    private readonly int _blockDurationMinutes;
    private readonly bool _autoBlockEnabled;

    // Manually blocked IPs that should never be auto-unblocked
    private readonly HashSet<string> _manuallyBlockedIps = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _persistenceLock = new();

    public SecurityService(ILogger<SecurityService> logger, IConfiguration configuration, IWebHostEnvironment env)
    {
        _logger = logger;
        _configuration = configuration;

        _autoBlockEnabled = configuration.GetValue<bool>("SecurityMonitoring:AutoBlockEnabled", true);
        _autoBlockThreshold = configuration.GetValue<int>("SecurityMonitoring:SuspiciousRequestThreshold", 10);
        _blockDurationMinutes = configuration.GetValue<int>("SecurityMonitoring:BlockDurationMinutes", 60);

        var relativePath = configuration.GetValue<string>(
            "SecurityMonitoring:BlockedIpsPersistencePath",
            "content/security/blocked-ips.json");
        _persistencePath = Path.Combine(env.ContentRootPath, relativePath);

        LoadPersistedBlockedIps();
    }

    /// <inheritdoc/>
    public bool IsBlocked(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return false;

        if (_blockedIps.TryGetValue(ipAddress, out var entry))
        {
            if (entry.IsActive)
                return true;

            // Expired — clean it up
            _blockedIps.TryRemove(ipAddress, out _);
            _suspiciousCounters.TryRemove(ipAddress, out _);
        }

        return false;
    }

    /// <inheritdoc/>
    public bool RecordSuspiciousRequest(
        string ipAddress, string path, string method, string userAgent,
        SecurityThreatType threatType, string description)
    {
        var ev = new SecurityEvent
        {
            IpAddress = ipAddress,
            Path = path,
            Method = method,
            UserAgent = userAgent,
            ThreatType = threatType,
            Description = description
        };

        EnqueueEvent(ev);

        _logger.LogWarning(
            "Security event [{ThreatType}] from {IpAddress}: {Description} | Path: {Path} | UA: {UserAgent}",
            threatType, ipAddress, description, path, userAgent);

        if (!_autoBlockEnabled)
            return false;

        // Already blocked — nothing more to do
        if (IsBlocked(ipAddress))
            return false;

        var count = _suspiciousCounters.AddOrUpdate(ipAddress, 1, (_, c) => c + 1);

        if (count >= _autoBlockThreshold)
        {
            var duration = TimeSpan.FromMinutes(_blockDurationMinutes);
            BlockIp(ipAddress,
                $"Auto-blocked after {count} suspicious requests ({threatType})",
                duration);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void BlockIp(string ipAddress, string reason, TimeSpan? duration = null, bool isManual = false)
    {
        var entry = new BlockedIp
        {
            IpAddress = ipAddress,
            Reason = reason,
            BlockedAt = DateTime.UtcNow,
            ExpiresAt = duration.HasValue ? DateTime.UtcNow.Add(duration.Value) : null,
            IsManual = isManual,
            SuspiciousRequestCount = _suspiciousCounters.TryGetValue(ipAddress, out var c) ? c : 0
        };

        _blockedIps[ipAddress] = entry;

        if (isManual)
            _manuallyBlockedIps.Add(ipAddress);

        _logger.LogWarning(
            "IP {IpAddress} blocked. Reason: {Reason}. Expires: {Expires}",
            ipAddress, reason, entry.ExpiresAt?.ToString("u") ?? "never");

        PersistBlockedIps();
    }

    /// <inheritdoc/>
    public void UnblockIp(string ipAddress)
    {
        _blockedIps.TryRemove(ipAddress, out _);
        _suspiciousCounters.TryRemove(ipAddress, out _);
        _manuallyBlockedIps.Remove(ipAddress);

        _logger.LogInformation("IP {IpAddress} unblocked", ipAddress);
        PersistBlockedIps();
    }

    /// <inheritdoc/>
    public SecurityStatus GetStatus()
    {
        // Remove expired entries first
        var expired = _blockedIps.Where(kv => !kv.Value.IsActive).Select(kv => kv.Key).ToList();
        foreach (var ip in expired)
            _blockedIps.TryRemove(ip, out _);

        return new SecurityStatus
        {
            BlockedIpCount = _blockedIps.Count,
            TotalEventsRecorded = _recentEvents.Count,
            BlockedIps = _blockedIps.Values.OrderByDescending(b => b.BlockedAt).ToList(),
            RecentEvents = _recentEvents.OrderByDescending(e => e.OccurredAt).Take(100).ToList()
        };
    }

    // ---------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------

    private void EnqueueEvent(SecurityEvent ev)
    {
        _recentEvents.Enqueue(ev);
        // Trim to MaxEvents
        while (_recentEvents.Count > MaxEvents)
            _recentEvents.TryDequeue(out _);
    }

    private void PersistBlockedIps()
    {
        try
        {
            lock (_persistenceLock)
            {
                var dir = Path.GetDirectoryName(_persistencePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var active = _blockedIps.Values.Where(b => b.IsActive).ToList();
                var json = JsonSerializer.Serialize(active, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_persistencePath, json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist blocked IPs to {Path}", _persistencePath);
        }
    }

    private void LoadPersistedBlockedIps()
    {
        try
        {
            if (!File.Exists(_persistencePath))
                return;

            var json = File.ReadAllText(_persistencePath);
            var entries = JsonSerializer.Deserialize<List<BlockedIp>>(json);
            if (entries == null) return;

            foreach (var entry in entries.Where(e => e.IsActive))
            {
                _blockedIps[entry.IpAddress] = entry;
                if (entry.IsManual)
                    _manuallyBlockedIps.Add(entry.IpAddress);
            }

            _logger.LogInformation(
                "Loaded {Count} persisted blocked IPs from {Path}",
                _blockedIps.Count, _persistencePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load persisted blocked IPs from {Path}", _persistencePath);
        }
    }
}
