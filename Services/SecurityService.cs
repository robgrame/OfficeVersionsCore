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
    // ---------------------------------------------------------------
    // Blocked IPs + suspicious counters
    // ---------------------------------------------------------------
    private readonly ConcurrentDictionary<string, BlockedIp> _blockedIps = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _suspiciousCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _manuallyBlockedIps = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _persistenceLock = new();

    // ---------------------------------------------------------------
    // Per-IP per-minute request counters
    // Key: IP, Value: (count, bucket start minute)
    // ---------------------------------------------------------------
    private readonly ConcurrentDictionary<string, (int Count, DateTime BucketStart)> _perIpMinuteCounters
        = new(StringComparer.OrdinalIgnoreCase);

    // Per-IP per-second counters (rapid request detection)
    private readonly ConcurrentDictionary<string, (int Count, DateTime BucketStart)> _perIpSecondCounters
        = new(StringComparer.OrdinalIgnoreCase);

    // Per-IP 404 counters (scanner detection) – reset every ScannerDetection404BlockMinutes
    private readonly ConcurrentDictionary<string, (int Count, DateTime BucketStart)> _perIp404Counters
        = new(StringComparer.OrdinalIgnoreCase);

    // Auth failure counters per IP (5-minute window)
    private readonly ConcurrentDictionary<string, (int Count, DateTime BucketStart)> _authFailuresByIp
        = new(StringComparer.OrdinalIgnoreCase);

    // Auth failure counters per user (5-minute window)
    private readonly ConcurrentDictionary<string, (int Count, DateTime BucketStart)> _authFailuresByUser
        = new(StringComparer.OrdinalIgnoreCase);

    // Track how many distinct IPs have had auth failures in current window (for multi-user/distributed attack)
    private readonly ConcurrentDictionary<string, DateTime> _recentAuthFailureIps
        = new(StringComparer.OrdinalIgnoreCase);

    // ---------------------------------------------------------------
    // Global metrics window
    // ---------------------------------------------------------------
    private long _totalRequests;
    private int _total404s;
    private int _total500s;
    private int _totalAuthFailures;
    private DateTime _windowStart = DateTime.UtcNow;
    private readonly object _metricsLock = new();

    // Top IPs by request count (simple sliding counter, reset with window)
    private readonly ConcurrentDictionary<string, int> _topIpsRequestCount
        = new(StringComparer.OrdinalIgnoreCase);

    // ---------------------------------------------------------------
    // Events and alerts
    // ---------------------------------------------------------------
    private const int MaxEvents = 200;
    private const int MaxAlerts = 100;
    private readonly ConcurrentQueue<SecurityEvent> _recentEvents = new();
    private readonly ConcurrentQueue<SecurityAlert> _recentAlerts = new();

    // ---------------------------------------------------------------
    // Configuration
    // ---------------------------------------------------------------
    private readonly ILogger<SecurityService> _logger;
    private readonly string _persistencePath;

    // IpBlocking config
    private readonly bool _scannerDetectionEnabled;
    private readonly int _max404PerIpBeforeBlock;
    private readonly int _scannerDetection404BlockMinutes;

    // SuspiciousActivity config
    private readonly int _maxSuspiciousRequestsPerMinute;
    private readonly bool _rapidRequestDetectionEnabled;
    private readonly int _rapidRequestThresholdPerSecond;
    private readonly bool _botDetectionEnabled;
    private readonly int _maxBotRequestsPerMinute;
    private readonly int _suspiciousBlockMinutes;

    // RequestRate config
    private readonly int _maxPerIpPerMinute;
    private readonly int _rateExceededBlockMinutes;

    // AuthFailures config
    private readonly int _maxAuthFailuresPerIpPer5Min;
    private readonly int _maxAuthFailuresPerUserPer5Min;
    private readonly int _maxTotalAuthFailuresPer5Min;
    private readonly int _authFailureBlockMinutes;

    public SecurityService(ILogger<SecurityService> logger, IConfiguration configuration, IWebHostEnvironment env)
    {
        _logger = logger;

        // IpBlocking
        _scannerDetectionEnabled = configuration.GetValue<bool>("SecurityMonitoring:IpBlocking:EnableScannerDetection", true);
        _max404PerIpBeforeBlock = configuration.GetValue<int>("SecurityMonitoring:IpBlocking:Max404PerIpBeforeBlock", 10);
        _scannerDetection404BlockMinutes = configuration.GetValue<int>("SecurityMonitoring:IpBlocking:ScannerDetection404BlockMinutes", 120);

        // SuspiciousActivity
        _maxSuspiciousRequestsPerMinute = configuration.GetValue<int>("SecurityMonitoring:SuspiciousActivity:MaxSuspiciousRequestsPerMinute", 5);
        _rapidRequestDetectionEnabled = configuration.GetValue<bool>("SecurityMonitoring:SuspiciousActivity:EnableRapidRequestDetection", true);
        _rapidRequestThresholdPerSecond = configuration.GetValue<int>("SecurityMonitoring:SuspiciousActivity:RapidRequestThresholdPerSecond", 50);
        _botDetectionEnabled = configuration.GetValue<bool>("SecurityMonitoring:SuspiciousActivity:EnableBotDetection", true);
        _maxBotRequestsPerMinute = configuration.GetValue<int>("SecurityMonitoring:SuspiciousActivity:MaxBotRequestsPerMinute", 200);
        _suspiciousBlockMinutes = configuration.GetValue<int>("SecurityMonitoring:IpBlocking:ScannerDetection404BlockMinutes", 120);

        // RequestRate
        _maxPerIpPerMinute = configuration.GetValue<int>("SecurityMonitoring:RequestRate:MaxPerIpPerMinute", 100);
        _rateExceededBlockMinutes = configuration.GetValue<int>("SecurityMonitoring:IpBlocking:ScannerDetection404BlockMinutes", 120);

        // AuthFailures
        _maxAuthFailuresPerIpPer5Min = configuration.GetValue<int>("SecurityMonitoring:AuthFailures:MaxFailuresPerIpPer5Min", 10);
        _maxAuthFailuresPerUserPer5Min = configuration.GetValue<int>("SecurityMonitoring:AuthFailures:MaxFailuresPerUserPer5Min", 5);
        _maxTotalAuthFailuresPer5Min = configuration.GetValue<int>("SecurityMonitoring:AuthFailures:MaxTotalFailuresPer5Min", 50);
        _authFailureBlockMinutes = configuration.GetValue<int>("SecurityMonitoring:IpBlocking:ScannerDetection404BlockMinutes", 120);

        // Persistence – use WebRootPath to align with LocalStorageService (wwwroot/content/)
        var relativePath = configuration.GetValue<string>(
            "SecurityMonitoring:BlockedIpsPersistencePath",
            "content/security/blocked-ips.json");
        _persistencePath = Path.Combine(env.WebRootPath, relativePath);

        LoadPersistedBlockedIps();
    }

    // ---------------------------------------------------------------
    // IsBlocked
    // ---------------------------------------------------------------

    /// <inheritdoc/>
    public bool IsBlocked(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return false;

        if (_blockedIps.TryGetValue(ipAddress, out var entry))
        {
            if (entry.IsActive)
                return true;

            _blockedIps.TryRemove(ipAddress, out _);
            _suspiciousCounters.TryRemove(ipAddress, out _);
        }

        return false;
    }

    // ---------------------------------------------------------------
    // RecordRequest
    // ---------------------------------------------------------------

    /// <inheritdoc/>
    public bool RecordRequest(string ipAddress, string path, int statusCode, string userAgent)
    {
        var now = DateTime.UtcNow;

        // Global metrics
        Interlocked.Increment(ref _totalRequests);
        if (statusCode == 404) Interlocked.Increment(ref _total404s);
        else if (statusCode >= 500) Interlocked.Increment(ref _total500s);

        _topIpsRequestCount.AddOrUpdate(ipAddress, 1, (_, c) => c + 1);

        // Per-IP per-minute counter
        var perMinute = IncrementBucketCounter(_perIpMinuteCounters, ipAddress, now, TimeSpan.FromMinutes(1));

        // Per-IP per-second counter (rapid request detection)
        if (_rapidRequestDetectionEnabled)
        {
            var perSecond = IncrementBucketCounter(_perIpSecondCounters, ipAddress, now, TimeSpan.FromSeconds(1));
            if (perSecond > _rapidRequestThresholdPerSecond)
            {
                return RecordSuspiciousRequest(ipAddress, path, "GET", userAgent,
                    SecurityThreatType.RapidRequests,
                    $"Rapid requests detected: {perSecond} req/s (threshold: {_rapidRequestThresholdPerSecond})");
            }
        }

        // Per-IP per-minute rate check
        if (perMinute > _maxPerIpPerMinute)
        {
            return RecordSuspiciousRequest(ipAddress, path, "GET", userAgent,
                SecurityThreatType.ExcessiveRequests,
                $"IP exceeded rate limit: {perMinute} req/min (max: {_maxPerIpPerMinute})");
        }

        // Scanner detection: 404-based IP blocking
        if (_scannerDetectionEnabled && statusCode == 404)
        {
            var count404 = IncrementBucketCounter(_perIp404Counters, ipAddress, now,
                TimeSpan.FromMinutes(_scannerDetection404BlockMinutes));

            if (count404 >= _max404PerIpBeforeBlock && !IsBlocked(ipAddress))
            {
                BlockIp(ipAddress,
                    $"Scanner detected: {count404} 404 errors in {_scannerDetection404BlockMinutes} min",
                    TimeSpan.FromMinutes(_scannerDetection404BlockMinutes));

                _logger.LogWarning(
                    "IP {IpAddress} auto-blocked as scanner ({Count} 404s, path: {Path})",
                    ipAddress, count404, path);

                EnqueueEvent(new SecurityEvent
                {
                    IpAddress = ipAddress,
                    Path = path,
                    Method = "GET",
                    UserAgent = userAgent,
                    ThreatType = SecurityThreatType.ScannerDetected,
                    Description = $"Scanner auto-blocked after {count404} 404 errors"
                });

                return true;
            }
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

        if (IsBlocked(ipAddress))
            return false;

        var count = _suspiciousCounters.AddOrUpdate(ipAddress, 1, (_, c) => c + 1);

        if (count >= _maxSuspiciousRequestsPerMinute)
        {
            BlockIp(ipAddress,
                $"Auto-blocked after {count} suspicious requests ({threatType})",
                TimeSpan.FromMinutes(_suspiciousBlockMinutes));
            return true;
        }

        return false;
    }

    // ---------------------------------------------------------------
    // RecordAuthFailure
    // ---------------------------------------------------------------

    /// <inheritdoc/>
    public bool RecordAuthFailure(string ipAddress, string? username = null)
    {
        var now = DateTime.UtcNow;
        Interlocked.Increment(ref _totalAuthFailures);

        // Track unique IPs with auth failures in the window
        _recentAuthFailureIps[ipAddress] = now;

        // Per-IP counter (5-minute window)
        var ipCount = IncrementBucketCounter(_authFailuresByIp, ipAddress, now, TimeSpan.FromMinutes(5));

        // Per-user counter (5-minute window)
        if (!string.IsNullOrWhiteSpace(username))
        {
            var userCount = IncrementBucketCounter(_authFailuresByUser, username, now, TimeSpan.FromMinutes(5));
            if (userCount >= _maxAuthFailuresPerUserPer5Min && !IsBlocked(ipAddress))
            {
                BlockIp(ipAddress,
                    $"Brute-force on account '{username}': {userCount} failures in 5 min",
                    TimeSpan.FromMinutes(_authFailureBlockMinutes));

                EnqueueEvent(new SecurityEvent
                {
                    IpAddress = ipAddress,
                    Path = "/auth",
                    Method = "POST",
                    UserAgent = string.Empty,
                    ThreatType = SecurityThreatType.AuthFailure,
                    Description = $"Brute-force on account '{username}': {userCount} failures"
                });
                return true;
            }
        }

        if (ipCount >= _maxAuthFailuresPerIpPer5Min && !IsBlocked(ipAddress))
        {
            BlockIp(ipAddress,
                $"Too many auth failures from IP: {ipCount} in 5 min",
                TimeSpan.FromMinutes(_authFailureBlockMinutes));

            EnqueueEvent(new SecurityEvent
            {
                IpAddress = ipAddress,
                Path = "/auth",
                Method = "POST",
                UserAgent = string.Empty,
                ThreatType = SecurityThreatType.AuthFailure,
                Description = $"Too many auth failures: {ipCount} in 5 min"
            });
            return true;
        }

        _logger.LogWarning(
            "Auth failure from {IpAddress} (user: {User}). Count in window: {Count}",
            ipAddress, username ?? "unknown", ipCount);

        return false;
    }

    // ---------------------------------------------------------------
    // BlockIp / UnblockIp
    // ---------------------------------------------------------------

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

    // ---------------------------------------------------------------
    // GetMetrics
    // ---------------------------------------------------------------

    /// <inheritdoc/>
    public SecurityMetrics GetMetrics()
    {
        var now = DateTime.UtcNow;
        long totalReq;
        int tot404, tot500, totAuth;
        DateTime windowStart;

        lock (_metricsLock)
        {
            totalReq = Interlocked.Read(ref _totalRequests);
            tot404 = _total404s;
            tot500 = _total500s;
            totAuth = _totalAuthFailures;
            windowStart = _windowStart;
        }

        var windowMinutes = Math.Max((now - windowStart).TotalMinutes, 1);
        var errorCount = tot404 + tot500;
        var errorRate = totalReq > 0 ? errorCount * 100.0 / totalReq : 0.0;

        var topIps = _topIpsRequestCount
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        return new SecurityMetrics
        {
            WindowStart = windowStart,
            TotalRequests = totalReq,
            RequestsPerMinute = totalReq / windowMinutes,
            Total404s = tot404,
            Total500s = tot500,
            ErrorRatePercentage = errorRate,
            TotalAuthFailures = totAuth,
            SuspiciousEventCount = _recentEvents.Count(e => e.OccurredAt >= windowStart),
            TopIpsByRequestCount = topIps
        };
    }

    /// <inheritdoc/>
    public void ResetMetricsWindow()
    {
        lock (_metricsLock)
        {
            Interlocked.Exchange(ref _totalRequests, 0);
            _total404s = 0;
            _total500s = 0;
            _totalAuthFailures = 0;
            _windowStart = DateTime.UtcNow;
            _topIpsRequestCount.Clear();
            _recentAuthFailureIps.Clear();
        }
    }

    // ---------------------------------------------------------------
    // GetStatus / RecordAlert
    // ---------------------------------------------------------------

    /// <inheritdoc/>
    public void RecordAlert(SecurityAlert alert)
    {
        _recentAlerts.Enqueue(alert);
        while (_recentAlerts.Count > MaxAlerts)
            _recentAlerts.TryDequeue(out _);
    }

    /// <inheritdoc/>
    public SecurityStatus GetStatus()
    {
        var expired = _blockedIps.Where(kv => !kv.Value.IsActive).Select(kv => kv.Key).ToList();
        foreach (var ip in expired)
            _blockedIps.TryRemove(ip, out _);

        return new SecurityStatus
        {
            BlockedIpCount = _blockedIps.Count,
            TotalEventsRecorded = _recentEvents.Count,
            BlockedIps = _blockedIps.Values.OrderByDescending(b => b.BlockedAt).ToList(),
            RecentEvents = _recentEvents.OrderByDescending(e => e.OccurredAt).Take(100).ToList(),
            Metrics = GetMetrics(),
            RecentAlerts = _recentAlerts.OrderByDescending(a => a.OccurredAt).ToList()
        };
    }

    // ---------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------

    private void EnqueueEvent(SecurityEvent ev)
    {
        _recentEvents.Enqueue(ev);
        while (_recentEvents.Count > MaxEvents)
            _recentEvents.TryDequeue(out _);
    }

    /// <summary>
    /// Thread-safe bucket counter: returns new count within the current bucket.
    /// If the current bucket is older than <paramref name="bucketDuration"/>, it is reset.
    /// </summary>
    private static int IncrementBucketCounter(
        ConcurrentDictionary<string, (int Count, DateTime BucketStart)> counters,
        string key,
        DateTime now,
        TimeSpan bucketDuration)
    {
        var updated = counters.AddOrUpdate(
            key,
            _ => (1, now),
            (_, existing) =>
            {
                if (now - existing.BucketStart >= bucketDuration)
                    return (1, now); // reset bucket
                return (existing.Count + 1, existing.BucketStart);
            });
        return updated.Count;
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
