using OfficeVersionsCore.Models;

namespace OfficeVersionsCore.Services;

/// <summary>
/// Service that dispatches security alerts via configured channels (email, Telegram).
/// </summary>
public interface ISecurityAlertService
{
    /// <summary>
    /// Sends an alert through all configured and enabled channels.
    /// Respects the AlertCooldown so the same alert title is not sent more than once per cooldown period.
    /// </summary>
    Task SendAlertAsync(SecurityAlert alert, CancellationToken cancellationToken = default);
}
