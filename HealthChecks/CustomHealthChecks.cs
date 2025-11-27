using Microsoft.Extensions.Diagnostics.HealthChecks;
using OfficeVersionsCore.Services;

namespace OfficeVersionsCore.HealthChecks;

/// <summary>
/// Health check for storage service availability
/// </summary>
public class StorageHealthCheck : IHealthCheck
{
    private readonly IStorageService _storageService;
    private readonly ILogger<StorageHealthCheck> _logger;

    public StorageHealthCheck(IStorageService storageService, ILogger<StorageHealthCheck> logger)
    {
        _storageService = storageService;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to read the last-update file to verify storage is accessible
            var lastUpdatePath = "last-update.json";
            var lastUpdateContent = await _storageService.ReadAsync(lastUpdatePath);
            
            if (!string.IsNullOrEmpty(lastUpdateContent))
            {
                return HealthCheckResult.Healthy("Storage service is operational");
            }
            
            return HealthCheckResult.Degraded("Storage service returned empty data");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage health check failed");
            return HealthCheckResult.Unhealthy($"Storage service is unavailable: {ex.Message}");
        }
    }
}

/// <summary>
/// Health check for data freshness
/// </summary>
public class DataFreshnessHealthCheck : IHealthCheck
{
    private readonly IStorageService _storageService;
    private readonly ILogger<DataFreshnessHealthCheck> _logger;

    public DataFreshnessHealthCheck(IStorageService storageService, ILogger<DataFreshnessHealthCheck> logger)
    {
        _storageService = storageService;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if latest update file exists and is recent
            var lastUpdatePath = "last-update.json";
            var exists = await _storageService.ExistsAsync(lastUpdatePath);
            
            if (!exists)
            {
                return HealthCheckResult.Unhealthy("No data available - last update file not found");
            }

            var lastModified = await _storageService.GetLastModifiedAsync(lastUpdatePath);
            
            if (lastModified.HasValue)
            {
                var now = DateTime.UtcNow;
                var age = now - lastModified.Value;
                var maxAge = TimeSpan.FromHours(24);
                
                if (age > maxAge)
                {
                    return HealthCheckResult.Degraded($"Data is stale - last update was {age.TotalHours:F1} hours ago");
                }
                
                return HealthCheckResult.Healthy("Data is up-to-date");
            }

            return HealthCheckResult.Degraded("Data freshness could not be determined");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Data freshness health check failed");
            return HealthCheckResult.Degraded($"Unable to verify data freshness: {ex.Message}");
        }
    }
}
