using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using System.Text;

namespace OfficeVersionsCore.Services;

/// <summary>
/// Cache service implementation with Redis support (fallback to in-memory)
/// </summary>
public class DistributedCacheService : ICacheService
{
    private readonly IDistributedCache _distributedCache;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<DistributedCacheService> _logger;
    private readonly bool _useDistributed;

    public DistributedCacheService(
        IDistributedCache distributedCache, 
        IMemoryCache memoryCache,
        ILogger<DistributedCacheService> logger)
    {
        _distributedCache = distributedCache;
        _memoryCache = memoryCache;
        _logger = logger;
        
        // Check if Redis is available (try a test operation)
        _useDistributed = IsDistributedCacheAvailable();
        
        if (!_useDistributed)
        {
            _logger.LogInformation("Using in-memory cache (distributed cache not available)");
        }
    }

    public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_useDistributed)
            {
                var value = await _distributedCache.GetStringAsync(key, cancellationToken);
                if (value != null)
                {
                    _logger.LogDebug("Cache hit (distributed): {Key}", key);
                }
                return value;
            }
            else
            {
                if (_memoryCache.TryGetValue(key, out object? value))
                {
                    _logger.LogDebug("Cache hit (memory): {Key}", key);
                    return value?.ToString();
                }
                return null;
            }
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
            var options = new DistributedCacheEntryOptions();
            if (expiration.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = expiration;
            }
            else
            {
                // Default: 1 hour
                options.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            }

            if (_useDistributed)
            {
                await _distributedCache.SetStringAsync(key, value, options, cancellationToken);
                _logger.LogDebug("Cached (distributed): {Key}", key);
            }
            else
            {
                var cacheOptions = new MemoryCacheEntryOptions();
                if (expiration.HasValue)
                {
                    cacheOptions.AbsoluteExpirationRelativeToNow = expiration;
                }
                else
                {
                    cacheOptions.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                }

                _memoryCache.Set(key, value, cacheOptions);
                _logger.LogDebug("Cached (memory): {Key}", key);
            }
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
            if (_useDistributed)
            {
                await _distributedCache.RemoveAsync(key, cancellationToken);
            }
            else
            {
                _memoryCache.Remove(key);
            }

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
            if (_useDistributed)
            {
                var value = await _distributedCache.GetAsync(key, cancellationToken);
                return value != null;
            }
            else
            {
                return _memoryCache.TryGetValue(key, out _);
            }
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
            if (_useDistributed)
            {
                await _distributedCache.RefreshAsync(key, cancellationToken);
            }
            // In-memory cache doesn't have refresh, so we do nothing
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error refreshing cache: {Key}", key);
        }
    }

    /// <summary>
    /// Check if distributed cache (Redis) is available
    /// </summary>
    private bool IsDistributedCacheAvailable()
    {
        try
        {
            // Try to perform a simple operation
            var testTask = _distributedCache.GetAsync("__cache_test__");
            testTask.Wait(TimeSpan.FromSeconds(2));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
