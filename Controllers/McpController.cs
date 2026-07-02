using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OfficeVersionsCore.Models;
using OfficeVersionsCore.Services;

namespace OfficeVersionsCore.Controllers
{
    /// <summary>
    /// Minimal HTTP-based MCP endpoint for AI clients and API Management.
    /// </summary>
    [ApiController]
    [Route("mcp")]
    [Produces("application/json")]
    [EnableRateLimiting("api")]
    public class McpController : ControllerBase
    {
        private const string ProtocolVersion = "2025-03-26";
        private const string ServerName = "officeversions-core-mcp";
        private const string ServerVersion = "1.0.0";

        private readonly ILogger<McpController> _logger;
        private readonly IOffice365Service _office365Service;
        private readonly IWindowsVersionsService _windowsVersionsService;

        public McpController(
            ILogger<McpController> logger,
            IOffice365Service office365Service,
            IWindowsVersionsService windowsVersionsService)
        {
            _logger = logger;
            _office365Service = office365Service;
            _windowsVersionsService = windowsVersionsService;
        }

        /// <summary>
        /// Returns basic metadata for the MCP server endpoint.
        /// </summary>
        [HttpGet]
        public ActionResult<object> GetServerInfo()
        {
            return Ok(new
            {
                name = ServerName,
                version = ServerVersion,
                protocol = "mcp-http",
                endpoint = "/mcp",
                capabilities = new[]
                {
                    "initialize",
                    "ping",
                    "tools/list",
                    "tools/call"
                },
                tools = GetToolDefinitions()
            });
        }

        /// <summary>
        /// Handles JSON-RPC style MCP requests over HTTP.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Post([FromBody] JsonElement? payload)
        {
            try
            {
                if (payload is null || payload.Value.ValueKind == JsonValueKind.Undefined)
                {
                    return BadRequest(CreateErrorResponse(null, -32600, "Invalid Request"));
                }

                if (!payload.Value.TryGetProperty("jsonrpc", out var jsonrpcElement) ||
                    jsonrpcElement.ValueKind != JsonValueKind.String ||
                    jsonrpcElement.GetString() != "2.0")
                {
                    return BadRequest(CreateErrorResponse(GetRequestId(payload.Value), -32600, "Invalid JSON-RPC request"));
                }

                if (!payload.Value.TryGetProperty("method", out var methodElement) ||
                    methodElement.ValueKind != JsonValueKind.String)
                {
                    return BadRequest(CreateErrorResponse(GetRequestId(payload.Value), -32600, "Missing method"));
                }

                var method = methodElement.GetString();
                var requestId = GetRequestId(payload.Value);
                var parameters = payload.Value.TryGetProperty("params", out var parametersElement)
                    ? parametersElement
                    : default;

                switch (method)
                {
                    case "initialize":
                        return Ok(CreateSuccessResponse(requestId, new
                        {
                            protocolVersion = ProtocolVersion,
                            capabilities = new
                            {
                                tools = new { listChanged = false }
                            },
                            serverInfo = new
                            {
                                name = ServerName,
                                version = ServerVersion
                            }
                        }));

                    case "ping":
                        return Ok(CreateSuccessResponse(requestId, new { ok = true }));

                    case "tools/list":
                        return Ok(CreateSuccessResponse(requestId, new
                        {
                            tools = GetToolDefinitions()
                        }));

                    case "tools/call":
                        var toolResult = await ExecuteToolCallAsync(parameters);
                        return Ok(CreateSuccessResponse(requestId, toolResult));

                    default:
                        return Ok(CreateErrorResponse(requestId, -32601, "Method not found"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled MCP request failure");
                return StatusCode(500, CreateErrorResponse(null, -32603, "Internal error"));
            }
        }

        private async Task<object> ExecuteToolCallAsync(JsonElement parameters)
        {
            if (parameters.ValueKind != JsonValueKind.Object)
            {
                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = "Missing tool parameters" }
                    },
                    isError = true
                };
            }

            if (!parameters.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
            {
                return new
                {
                    content = new[]
                    {
                        new { type = "text", text = "Missing tool name" }
                    },
                    isError = true
                };
            }

            var toolName = nameElement.GetString();
            var arguments = parameters.TryGetProperty("arguments", out var argumentsElement)
                ? argumentsElement
                : default;

            return toolName switch
            {
                "get_site_overview" => await GetSiteOverviewAsync(),
                "get_latest_office_versions" => await GetLatestOfficeVersionsAsync(),
                "get_office_channel_latest" => await GetOfficeChannelLatestAsync(arguments),
                "get_windows_latest_version" => await GetWindowsLatestVersionAsync(arguments),
                _ => new
                {
                    content = new[]
                    {
                        new { type = "text", text = $"Unknown tool '{toolName}'" }
                    },
                    isError = true
                }
            };
        }

        private async Task<object> GetSiteOverviewAsync()
        {
            var officeData = await _office365Service.GetLatestVersionsAsync();
            var windows10 = await _windowsVersionsService.GetLatestVersionAsync(WindowsEdition.Windows10);
            var windows11 = await _windowsVersionsService.GetLatestVersionAsync(WindowsEdition.Windows11);

            var officeChannels = (officeData?.Data?
                .GroupBy(v => v.Channel)
                .Select(g => new
                {
                    channel = g.Key,
                    latestVersion = g.OrderByDescending(v => v.LatestReleaseDate).FirstOrDefault()?.Version ?? "Unknown",
                    latestBuild = g.OrderByDescending(v => v.LatestReleaseDate).FirstOrDefault()?.Build ?? "Unknown"
                })
                .OrderBy(x => x.channel)
                .ToList() ?? Enumerable.Empty<object>()).ToList();

            return WrapToolResult(new
            {
                site = new
                {
                    name = "Office Versions",
                    description = "Website tracking Microsoft 365 Apps and Windows release information.",
                    homepage = "https://www.office365versions.com",
                    apiDocs = "/swagger",
                    mcpEndpoint = "/mcp"
                },
                office = new
                {
                    available = officeData?.Data?.Any() == true,
                    channels = officeChannels
                },
                windows = new
                {
                    windows10 = windows10.Success ? windows10.Data : null,
                    windows11 = windows11.Success ? windows11.Data : null
                }
            });
        }

        private async Task<object> GetLatestOfficeVersionsAsync()
        {
            var data = await _office365Service.GetLatestVersionsAsync();
            return WrapToolResult(new
            {
                available = data?.Data?.Any() == true,
                count = data?.Data?.Count ?? 0,
                channels = (data?.Data?
                    .GroupBy(v => v.Channel)
                    .Select(g => new
                    {
                        channel = g.Key,
                        latestVersion = g.OrderByDescending(v => v.LatestReleaseDate).FirstOrDefault()?.Version ?? "Unknown",
                        latestBuild = g.OrderByDescending(v => v.LatestReleaseDate).FirstOrDefault()?.Build ?? "Unknown"
                    })
                    .OrderBy(x => x.channel)
                    .ToList() ?? Enumerable.Empty<object>()).ToList()
            });
        }

        private async Task<object> GetOfficeChannelLatestAsync(JsonElement arguments)
        {
            var channel = GetStringArgument(arguments, "channel") ?? "Current Channel";
            var latest = await _office365Service.GetLatestVersionForChannelAsync(channel);

            return WrapToolResult(new
            {
                requestedChannel = channel,
                found = latest != null,
                version = latest
            });
        }

        private async Task<object> GetWindowsLatestVersionAsync(JsonElement arguments)
        {
            var edition = GetStringArgument(arguments, "edition") ?? "windows11";
            var parsedEdition = edition.ToLowerInvariant() switch
            {
                "windows10" or "win10" => WindowsEdition.Windows10,
                "windows11" or "win11" => WindowsEdition.Windows11,
                _ => WindowsEdition.Windows11
            };

            var result = await _windowsVersionsService.GetLatestVersionAsync(parsedEdition);
            return WrapToolResult(new
            {
                edition = parsedEdition.ToString(),
                found = result.Success && result.Data != null,
                version = result.Data
            });
        }

        private static object WrapToolResult(object payload)
        {
            return new
            {
                content = new[]
                {
                    new { type = "text", text = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) }
                },
                isError = false
            };
        }

        private static object[] GetToolDefinitions() =>
        [
            new
            {
                name = "get_site_overview",
                description = "Returns summary information about the Office Versions website and its current data availability.",
                inputSchema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>()
                }
            },
            new
            {
                name = "get_latest_office_versions",
                description = "Returns the latest Microsoft 365 Apps release information by channel.",
                inputSchema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>()
                }
            },
            new
            {
                name = "get_office_channel_latest",
                description = "Returns the latest Microsoft 365 Apps release for a specific channel.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        channel = new { type = "string", description = "Channel name, e.g. Current Channel" }
                    },
                    required = new[] { "channel" }
                }
            },
            new
            {
                name = "get_windows_latest_version",
                description = "Returns the latest Windows release information for Windows 10 or Windows 11.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        edition = new { type = "string", description = "Edition name: windows10 or windows11" }
                    },
                    required = new[] { "edition" }
                }
            }
        ];

        private static object CreateSuccessResponse(object? requestId, object result) => new
        {
            jsonrpc = "2.0",
            id = requestId,
            result
        };

        private static object CreateErrorResponse(object? requestId, int code, string message) => new
        {
            jsonrpc = "2.0",
            id = requestId,
            error = new
            {
                code,
                message
            }
        };

        private static object? GetRequestId(JsonElement payload)
        {
            return payload.TryGetProperty("id", out var idElement)
                ? idElement.ValueKind switch
                {
                    JsonValueKind.String => idElement.GetString(),
                    JsonValueKind.Number => idElement.GetInt32(),
                    JsonValueKind.Null => null,
                    _ => idElement.GetRawText()
                }
                : null;
        }

        private static string? GetStringArgument(JsonElement arguments, string name)
        {
            if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out var valueElement))
            {
                return null;
            }

            return valueElement.ValueKind == JsonValueKind.String ? valueElement.GetString() : valueElement.GetRawText();
        }
    }
}
