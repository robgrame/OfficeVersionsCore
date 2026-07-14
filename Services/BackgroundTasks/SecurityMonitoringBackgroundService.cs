using OfficeVersionsCore.Models;
using OfficeVersionsCore.Services;

namespace OfficeVersionsCore.Services.BackgroundTasks;

/// <summary>
/// Background service that periodically analyses security metrics and raises alerts
/// when configured thresholds are exceeded.
/// </summary>
public class SecurityMonitoringBackgroundService : BackgroundService
{
    private readonly ILogger<SecurityMonitoringBackgroundService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ISecurityService _securityService;
    private readonly ISecurityAlertService _alertService;

    private readonly TimeSpan _checkInterval;
    private readonly int _analysisWindowMinutes;

    // Thresholds
    private readonly double _requestRateWarning;
    private readonly double _requestRateCritical;
    private readonly double _errorRateWarning;
    private readonly double _errorRateCritical;
    private readonly long _errorRateMinRequests;
    private readonly int _max404PerMinute;
    private readonly int _max500PerMinute;
    private readonly int _maxAuthFailuresPer5Min;
    private readonly int _maxSuspiciousPerMinute;

    public SecurityMonitoringBackgroundService(
        ILogger<SecurityMonitoringBackgroundService> logger,
        IConfiguration configuration,
        ISecurityService securityService,
        ISecurityAlertService alertService)
    {
        _logger = logger;
        _configuration = configuration;
        _securityService = securityService;
        _alertService = alertService;

        var intervalSeconds = configuration.GetValue<int>("SecurityMonitoring:CheckIntervalSeconds", 60);
        _checkInterval = TimeSpan.FromSeconds(intervalSeconds);

        _analysisWindowMinutes = configuration.GetValue<int>("SecurityMonitoring:AnalysisWindowMinutes", 5);

        _requestRateWarning = configuration.GetValue<double>("SecurityMonitoring:RequestRate:WarningPerMinute", 1000);
        _requestRateCritical = configuration.GetValue<double>("SecurityMonitoring:RequestRate:CriticalPerMinute", 5000);

        _errorRateWarning = configuration.GetValue<double>("SecurityMonitoring:ErrorRate:WarningPercentage", 5.0);
        _errorRateCritical = configuration.GetValue<double>("SecurityMonitoring:ErrorRate:CriticalPercentage", 15.0);
        _errorRateMinRequests = configuration.GetValue<long>("SecurityMonitoring:ErrorRate:MinRequestsForRateAlert", 100);
        _max404PerMinute = configuration.GetValue<int>("SecurityMonitoring:ErrorRate:Max404PerMinute", 50);
        _max500PerMinute = configuration.GetValue<int>("SecurityMonitoring:ErrorRate:Max500PerMinute", 10);

        _maxAuthFailuresPer5Min = configuration.GetValue<int>("SecurityMonitoring:AuthFailures:MaxTotalFailuresPer5Min", 50);
        _maxSuspiciousPerMinute = configuration.GetValue<int>("SecurityMonitoring:SuspiciousActivity:MaxSuspiciousRequestsPerMinute", 5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Security monitoring background service started. Check interval: {Interval}s, analysis window: {Window} min",
            _checkInterval.TotalSeconds, _analysisWindowMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
                await AnalyseAndAlertAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during security monitoring analysis");
            }
        }

        _logger.LogInformation("Security monitoring background service stopped");
    }

    private async Task AnalyseAndAlertAsync(CancellationToken cancellationToken)
    {
        var metrics = _securityService.GetMetrics();

        _logger.LogDebug(
            "Security metrics: {ReqPerMin:F0} req/min, {ErrorRate:F1}% errors, {Auth} auth failures, {Suspicious} suspicious events",
            metrics.RequestsPerMinute, metrics.ErrorRatePercentage, metrics.TotalAuthFailures, metrics.SuspiciousEventCount);

        // 1. Request rate checks
        if (metrics.RequestsPerMinute >= _requestRateCritical)
        {
            await RaiseAlertAsync(new SecurityAlert
            {
                Level = AlertLevel.Critical,
                Title = "Critical request rate",
                Message = $"Request rate reached {metrics.RequestsPerMinute:F0} req/min (threshold: {_requestRateCritical:F0}). " +
                          $"Total in window: {metrics.TotalRequests}."
            }, cancellationToken);
        }
        else if (metrics.RequestsPerMinute >= _requestRateWarning)
        {
            await RaiseAlertAsync(new SecurityAlert
            {
                Level = AlertLevel.Warning,
                Title = "High request rate",
                Message = $"Request rate is {metrics.RequestsPerMinute:F0} req/min (threshold: {_requestRateWarning:F0}). " +
                          $"Total in window: {metrics.TotalRequests}."
            }, cancellationToken);
        }

        // 2. Error rate checks
        // Only evaluate the error-rate *percentage* when we have a statistically
        // meaningful request volume. On low-traffic apps a couple of bot-driven
        // 404s (e.g. probing /.env, /wp-login.php) out of a handful of requests
        // would otherwise push the rate to 100% and spam alerts every cycle.
        // Raw 404/500-per-minute thresholds below still catch real scanner floods.
        if (metrics.TotalRequests >= _errorRateMinRequests)
        {
        if (metrics.ErrorRatePercentage >= _errorRateCritical)
        {
            await RaiseAlertAsync(new SecurityAlert
            {
                Level = AlertLevel.Critical,
                Title = "Critical error rate",
                Message = $"Error rate is {metrics.ErrorRatePercentage:F1}% (threshold: {_errorRateCritical:F1}%). " +
                          $"404s: {metrics.Total404s}, 500s: {metrics.Total500s}."
            }, cancellationToken);
        }
        else if (metrics.ErrorRatePercentage >= _errorRateWarning)
        {
            await RaiseAlertAsync(new SecurityAlert
            {
                Level = AlertLevel.Warning,
                Title = "Elevated error rate",
                Message = $"Error rate is {metrics.ErrorRatePercentage:F1}% (threshold: {_errorRateWarning:F1}%). " +
                          $"404s: {metrics.Total404s}, 500s: {metrics.Total500s}."
            }, cancellationToken);
        }
        }

        // 3. Raw 404/500 rate checks (per minute)
        var windowMinutes = Math.Max((DateTime.UtcNow - metrics.WindowStart).TotalMinutes, 1);
        var rate404 = metrics.Total404s / windowMinutes;
        var rate500 = metrics.Total500s / windowMinutes;

        if (rate404 >= _max404PerMinute)
        {
            await RaiseAlertAsync(new SecurityAlert
            {
                Level = AlertLevel.Warning,
                Title = "Excessive 404 errors",
                Message = $"404 error rate is {rate404:F0}/min (threshold: {_max404PerMinute}/min). " +
                          $"Possible scanner activity. Total 404s in window: {metrics.Total404s}."
            }, cancellationToken);
        }

        if (rate500 >= _max500PerMinute)
        {
            await RaiseAlertAsync(new SecurityAlert
            {
                Level = AlertLevel.Warning,
                Title = "Excessive 500 errors",
                Message = $"500 error rate is {rate500:F0}/min (threshold: {_max500PerMinute}/min). " +
                          $"Total 500s in window: {metrics.Total500s}."
            }, cancellationToken);
        }

        // 4. Auth failure total check
        if (metrics.TotalAuthFailures >= _maxAuthFailuresPer5Min)
        {
            await RaiseAlertAsync(new SecurityAlert
            {
                Level = AlertLevel.Warning,
                Title = "Excessive authentication failures",
                Message = $"{metrics.TotalAuthFailures} auth failures in the analysis window " +
                          $"(threshold: {_maxAuthFailuresPer5Min}). Possible credential-stuffing or brute-force attack."
            }, cancellationToken);
        }

        // 5. Suspicious event count check
        var suspiciousPerMin = metrics.SuspiciousEventCount / windowMinutes;
        if (suspiciousPerMin >= _maxSuspiciousPerMinute)
        {
            await RaiseAlertAsync(new SecurityAlert
            {
                Level = AlertLevel.Warning,
                Title = "High suspicious activity",
                Message = $"{metrics.SuspiciousEventCount} suspicious events in the analysis window " +
                          $"({suspiciousPerMin:F0}/min, threshold: {_maxSuspiciousPerMinute}/min)."
            }, cancellationToken);
        }

        // Reset metrics window after analysis so next cycle starts fresh
        _securityService.ResetMetricsWindow();
    }

    private async Task RaiseAlertAsync(SecurityAlert alert, CancellationToken cancellationToken)
    {
        _securityService.RecordAlert(alert);
        _logger.LogWarning("[{Level}] Security alert: {Title} – {Message}", alert.Level, alert.Title, alert.Message);
        await _alertService.SendAlertAsync(alert, cancellationToken);
    }
}
