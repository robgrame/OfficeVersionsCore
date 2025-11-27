using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using System.Text;

namespace OfficeVersionsCore.Services;

/// <summary>
/// Cache service implementation - using in-memory cache only (no Redis)
/// </summary>
public class DistributedCacheService : ICacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<DistributedCacheService> _logger;

    public DistributedCacheService(
        IMemoryCache memoryCache,
        ILogger<DistributedCacheService> logger)
    {
        _memoryCache = memoryCache;
        _logger = logger;
        _logger.LogInformation("Cache service initialized (in-memory only)");
    }

    public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_memoryCache.TryGetValue(key, out object? value))
            {
                _logger.LogDebug("Cache hit (memory): {Key}", key);
                return value?.ToString();
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrieving from cache: {Key}", key);
            return null;
        }
    }

    public async Task SetStringAsync(string key, string value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheOptions = new MemoryCacheEntryOptions();
            if (expiration.HasValue)
            {
                cacheOptions.AbsoluteExpirationRelativeToNow = expiration;
            }
            else
            {
                // Default: 1 hour
                cacheOptions.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            }

            _memoryCache.Set(key, value, cacheOptions);
            _logger.LogDebug("Cached (memory): {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error setting cache: {Key}", key);
            // Don't throw - cache failure shouldn't break the app
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            _memoryCache.Remove(key);
            _logger.LogDebug("Removed from cache: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error removing from cache: {Key}", key);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return _memoryCache.TryGetValue(key, out _);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking cache existence: {Key}", key);
            return false;
        }
    }

    public async Task RefreshAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            // In-memory cache doesn't have refresh capability
            _logger.LogDebug("Refresh not applicable for in-memory cache: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error refreshing cache: {Key}", key);
        }
    }
}
