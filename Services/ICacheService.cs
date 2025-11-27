namespace OfficeVersionsCore.Services;

/// <summary>
/// Interface for distributed caching with Redis support
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Get cached value as string
    /// </summary>
    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set cached value as string
    /// </summary>
    Task SetStringAsync(string key, string value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove cached value
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if key exists in cache
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh expiration time of a key
    /// </summary>
    Task RefreshAsync(string key, CancellationToken cancellationToken = default);
}
