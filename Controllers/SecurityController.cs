using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using OfficeVersionsCore.Models;
using OfficeVersionsCore.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace OfficeVersionsCore.Controllers;

/// <summary>
/// Security monitoring and IP management endpoints.
/// These endpoints are protected by an API key (SecurityMonitoring:AdminApiKey in configuration).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("api-strict")]
[SecurityAdminApiKey]
[ApiExplorerSettings(IgnoreApi = true)]
public class SecurityController : ControllerBase
{
    private readonly ISecurityService _securityService;
    private readonly ILogger<SecurityController> _logger;
    private readonly IConfiguration _configuration;

    public SecurityController(
        ISecurityService securityService,
        ILogger<SecurityController> logger,
        IConfiguration configuration)
    {
        _securityService = securityService;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Returns the current security status: blocked IPs and recent security events.
    /// </summary>
    [HttpGet("status")]
    [SwaggerOperation(Summary = "Get security monitoring status", Tags = ["Security"])]
    [ProducesResponseType<SecurityStatus>(StatusCodes.Status200OK)]
    public ActionResult<SecurityStatus> GetStatus()
    {
        _logger.LogInformation("Security status requested from {Ip}", HttpContext.Connection.RemoteIpAddress);
        return Ok(_securityService.GetStatus());
    }

    /// <summary>
    /// Manually blocks an IP address.
    /// </summary>
    /// <param name="request">Block request details</param>
    [HttpPost("block")]
    [SwaggerOperation(Summary = "Manually block an IP address", Tags = ["Security"])]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult BlockIp([FromBody] BlockIpRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IpAddress))
            return BadRequest(new { error = "IpAddress is required." });

        TimeSpan? duration = request.DurationMinutes.HasValue
            ? TimeSpan.FromMinutes(request.DurationMinutes.Value)
            : null;

        _securityService.BlockIp(request.IpAddress, request.Reason ?? "Manually blocked via API", duration, isManual: true);
        _logger.LogWarning("IP {IpAddress} manually blocked via API. Reason: {Reason}", request.IpAddress, request.Reason);

        return Ok(new { message = $"IP {request.IpAddress} has been blocked." });
    }

    /// <summary>
    /// Removes the block for an IP address.
    /// </summary>
    /// <param name="ipAddress">The IP address to unblock</param>
    [HttpDelete("block/{ipAddress}")]
    [SwaggerOperation(Summary = "Unblock an IP address", Tags = ["Security"])]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult UnblockIp(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return BadRequest(new { error = "ipAddress is required." });

        _securityService.UnblockIp(ipAddress);
        _logger.LogInformation("IP {IpAddress} unblocked via API", ipAddress);

        return Ok(new { message = $"IP {ipAddress} has been unblocked." });
    }
}

/// <summary>
/// Request body for blocking an IP address
/// </summary>
public class BlockIpRequest
{
    /// <summary>IP address to block</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>Human-readable reason for the block</summary>
    public string? Reason { get; set; }

    /// <summary>Block duration in minutes; omit or set to null for a permanent block</summary>
    public int? DurationMinutes { get; set; }
}

/// <summary>
/// Action filter that requires a valid admin API key in the X-Admin-Api-Key request header.
/// The expected key is read from configuration: SecurityMonitoring:AdminApiKey.
/// If no key is configured, the endpoints are open (for backwards compatibility with dev environments).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SecurityAdminApiKeyAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expectedKey = configuration.GetValue<string>("SecurityMonitoring:AdminApiKey");

        // If no key is configured, allow access (dev environment)
        if (string.IsNullOrWhiteSpace(expectedKey))
            return;

        if (!context.HttpContext.Request.Headers.TryGetValue("X-Admin-Api-Key", out var providedKey)
            || !string.Equals(providedKey, expectedKey, StringComparison.Ordinal))
        {
            context.Result = new ObjectResult(new { error = "Unauthorized", message = "A valid X-Admin-Api-Key header is required." })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
