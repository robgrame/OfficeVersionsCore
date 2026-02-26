using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using OfficeVersionsCore.Models;

namespace OfficeVersionsCore.Services;

/// <summary>
/// Sends security alerts via email (SMTP) and/or Telegram Bot API.
/// </summary>
public class SecurityAlertService : ISecurityAlertService
{
    private readonly ILogger<SecurityAlertService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    // Cooldown: track last-sent time per alert title to avoid flooding
    private readonly ConcurrentDictionary<string, DateTime> _lastSentAt = new(StringComparer.OrdinalIgnoreCase);

    public SecurityAlertService(
        ILogger<SecurityAlertService> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc/>
    public async Task SendAlertAsync(SecurityAlert alert, CancellationToken cancellationToken = default)
    {
        // Check whether this alert level is configured to be sent
        var configuredLevels = _configuration
            .GetSection("SecurityMonitoring:Alerting:AlertLevels")
            .Get<string[]>() ?? ["Warning", "Critical"];

        if (!configuredLevels.Contains(alert.Level.ToString(), StringComparer.OrdinalIgnoreCase))
            return;

        // Check cooldown
        var cooldownMinutes = _configuration.GetValue<int>("SecurityMonitoring:AlertCooldownMinutes", 15);
        var cooldownKey = alert.Title;
        if (_lastSentAt.TryGetValue(cooldownKey, out var lastSent)
            && (DateTime.UtcNow - lastSent).TotalMinutes < cooldownMinutes)
        {
            _logger.LogDebug(
                "Alert '{Title}' suppressed (cooldown). Last sent: {LastSent}", alert.Title, lastSent);
            return;
        }

        _lastSentAt[cooldownKey] = DateTime.UtcNow;

        var tasks = new List<Task>();

        if (_configuration.GetValue<bool>("SecurityMonitoring:Alerting:EmailEnabled", false))
            tasks.Add(SendEmailAsync(alert, cancellationToken));

        if (_configuration.GetValue<bool>("SecurityMonitoring:Alerting:TelegramEnabled", false))
            tasks.Add(SendTelegramAsync(alert, cancellationToken));

        if (tasks.Count > 0)
            await Task.WhenAll(tasks);
    }

    // ---------------------------------------------------------------
    // Email
    // ---------------------------------------------------------------

    private async Task SendEmailAsync(SecurityAlert alert, CancellationToken cancellationToken)
    {
        var smtpHost = _configuration.GetValue<string>("SecurityMonitoring:Alerting:SmtpHost");
        var fromEmail = _configuration.GetValue<string>("SecurityMonitoring:Alerting:FromEmail");
        var alertEmails = _configuration
            .GetSection("SecurityMonitoring:Alerting:AlertEmails")
            .Get<string[]>() ?? [];

        if (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(fromEmail) || alertEmails.Length == 0)
        {
            _logger.LogDebug("Email alerting skipped: SMTP not fully configured");
            return;
        }

        try
        {
            var smtpPort = _configuration.GetValue<int>("SecurityMonitoring:Alerting:SmtpPort", 587);
            var smtpUser = _configuration.GetValue<string>("SecurityMonitoring:Alerting:SmtpUser");
            var smtpPassword = _configuration.GetValue<string>("SecurityMonitoring:Alerting:SmtpPassword");
            var includeDetails = _configuration.GetValue<bool>("SecurityMonitoring:Alerting:IncludeDetails", true);

            var subject = $"[{alert.Level}] Security Alert – {alert.Title}";
            var body = BuildEmailBody(alert, includeDetails);

            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = _configuration.GetValue<bool>("SecurityMonitoring:Alerting:SmtpUseSsl", smtpPort != 25),
                Credentials = !string.IsNullOrWhiteSpace(smtpUser)
                    ? new NetworkCredential(smtpUser, smtpPassword)
                    : null
            };

            using var message = new MailMessage
            {
                From = new MailAddress(fromEmail, "OfficeVersionsCore Security"),
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };

            foreach (var to in alertEmails)
                message.To.Add(to);

            await client.SendMailAsync(message, cancellationToken);

            _logger.LogInformation(
                "Security alert email sent for '{Title}' to {Recipients}",
                alert.Title, string.Join(", ", alertEmails));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send security alert email for '{Title}'", alert.Title);
        }
    }

    private static string BuildEmailBody(SecurityAlert alert, bool includeDetails)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Security Alert – {alert.Level}");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine($"Title   : {alert.Title}");
        sb.AppendLine($"Time    : {alert.OccurredAt:u}");
        if (!string.IsNullOrWhiteSpace(alert.IpAddress))
            sb.AppendLine($"IP      : {alert.IpAddress}");
        sb.AppendLine();
        if (includeDetails)
        {
            sb.AppendLine("Details:");
            sb.AppendLine(alert.Message);
        }
        sb.AppendLine();
        sb.AppendLine("-- OfficeVersionsCore Security Monitoring");
        return sb.ToString();
    }

    // ---------------------------------------------------------------
    // Telegram
    // ---------------------------------------------------------------

    private async Task SendTelegramAsync(SecurityAlert alert, CancellationToken cancellationToken)
    {
        var botToken = _configuration.GetValue<string>("SecurityMonitoring:Alerting:TelegramBotToken");
        var chatId = _configuration.GetValue<string>("SecurityMonitoring:Alerting:TelegramChatId");

        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
        {
            _logger.LogDebug("Telegram alerting skipped: bot token or chat ID not configured");
            return;
        }

        try
        {
            var includeDetails = _configuration.GetValue<bool>("SecurityMonitoring:Alerting:IncludeDetails", true);
            var emoji = alert.Level == AlertLevel.Critical ? "🚨" : "⚠️";
            var text = BuildTelegramMessage(alert, emoji, includeDetails);

            var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var payload = new { chat_id = chatId, text, parse_mode = "HTML" };

            var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.PostAsJsonAsync(url, payload, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Telegram security alert sent for '{Title}'", alert.Title);
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Telegram alert failed for '{Title}'. Status: {Status}. Response: {Body}",
                    alert.Title, response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram alert for '{Title}'", alert.Title);
        }
    }

    private static string BuildTelegramMessage(SecurityAlert alert, string emoji, bool includeDetails)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{emoji} <b>[{alert.Level}] {alert.Title}</b>");
        sb.AppendLine($"🕐 {alert.OccurredAt:u}");
        if (!string.IsNullOrWhiteSpace(alert.IpAddress))
            sb.AppendLine($"🌐 IP: <code>{alert.IpAddress}</code>");
        if (includeDetails)
        {
            sb.AppendLine();
            sb.AppendLine(alert.Message);
        }
        return sb.ToString();
    }
}
