# Rate Limiting & API Throttling Strategy

## ?? Overview

OfficeVersionsCore implements a **multi-layered rate limiting strategy** to protect API endpoints from:
- **DDoS attacks** (Distributed Denial of Service)
- **Resource exhaustion** (CPU, memory, bandwidth)
- **Abuse** (scraping, automated attacks)
- **Unintentional overload** (buggy clients, infinite loops)

---

## ?? Rate Limiting Policies

### 1. **`api` - General API Rate Limit**
**Strategy**: Fixed Window  
**Limit**: 100 requests per minute  
**Queue**: 5 requests  
**Use Case**: Standard API endpoints

```csharp
[EnableRateLimiting("api")]
```

### 2. **`api-strict` - Strict Rate Limit**
**Strategy**: Fixed Window  
**Limit**: 20 requests per minute  
**Queue**: 2 requests  
**Use Case**: Resource-intensive operations (e.g., `/refresh`, admin endpoints)

```csharp
[EnableRateLimiting("api-strict")]
```

### 3. **`api-sliding` - Sliding Window**
**Strategy**: Sliding Window  
**Limit**: 150 requests per minute (distributed across 6 segments of 10 seconds each)  
**Queue**: 5 requests  
**Use Case**: High-traffic API endpoints with smoother distribution

```csharp
[EnableRateLimiting("api-sliding")]
```

**Benefit**: Prevents burst traffic by distributing requests evenly over time.

### 4. **`api-burst` - Token Bucket**
**Strategy**: Token Bucket  
**Burst Capacity**: 30 tokens  
**Refill Rate**: 1 token every 2 seconds  
**Queue**: 10 requests  
**Use Case**: APIs that need to handle occasional bursts

```csharp
[EnableRateLimiting("api-burst")]
```

**How it works**:
- Client starts with 30 tokens
- Each request consumes 1 token
- Tokens refill at 1 per 2 seconds (30 tokens/minute)
- Allows bursts up to 30 requests immediately

### 5. **`api-concurrent` - Concurrency Limiter**
**Strategy**: Concurrency  
**Max Concurrent**: 10 simultaneous requests  
**Queue**: 5 requests  
**Use Case**: Database-heavy or I/O-intensive operations

```csharp
[EnableRateLimiting("api-concurrent")]
```

### 6. **`api-per-ip` - Per-IP Rate Limit**
**Strategy**: Fixed Window (Partitioned by IP)  
**Limit**: 50 requests per minute per IP  
**Queue**: 5 requests per IP  
**Use Case**: Prevent abuse from single IPs

```csharp
[EnableRateLimiting("api-per-ip")]
```

**Benefit**: Each IP address gets its own quota.

### 7. **`pages` - Razor Pages**
**Strategy**: Fixed Window  
**Limit**: 1000 requests per minute  
**Queue**: 20 requests  
**Use Case**: Razor Pages (more permissive for normal browsing)

```csharp
[EnableRateLimiting("pages")]
```

---

## ??? Applied Policies

### M365AppsReleasesController
```csharp
[EnableRateLimiting("api-sliding")]  // All endpoints
```

**Endpoints**:
- `GET /api/M365AppsReleases` - Get all Office 365 versions
- `GET /api/M365AppsReleases/data` - Get data array
- `GET /api/M365AppsReleases/channel/{channel}` - Get channel versions
- `GET /api/M365AppsReleases/channel/{channel}/latest` - Get latest version

**Justification**: High traffic expected, sliding window provides smooth distribution.

---

### WindowsVersionsController
```csharp
[EnableRateLimiting("api-sliding")]  // Default for all endpoints
[EnableRateLimiting("api-strict")]   // Specific for /refresh
```

**Standard Endpoints** (`api-sliding`):
- `GET /api/WindowsVersions` - Get aggregated data
- `GET /api/WindowsVersions/{edition}` - Get versions by edition
- `GET /api/WindowsVersions/releases` - Get all releases
- `GET /api/WindowsVersions/{edition}/latest` - Get latest version

**Strict Endpoint** (`api-strict`):
- `POST /api/WindowsVersions/refresh` - Manual data refresh (admin only)

**Justification**: Refresh is resource-intensive and should be heavily rate-limited.

---

### SitemapController
```csharp
[EnableRateLimiting("api")]  // Standard rate limiting
```

**Endpoints**:
- `GET /sitemap` - Generate sitemap.xml
- `GET /sitemap/xml` - Generate sitemap.xml

**Justification**: Sitemap generation is moderately expensive but not critical.

---

## ?? HTTP 429 Response

When rate limit is exceeded, clients receive:

```http
HTTP/1.1 429 Too Many Requests
Retry-After: 60
Content-Type: application/json

{
  "error": "Too Many Requests",
  "message": "Rate limit exceeded. Please try again later.",
  "retryAfter": 60
}
```

**Headers**:
- `Retry-After`: Seconds until retry is allowed

---

## ?? Configuration

### Customize Rate Limits (appsettings.json)

Currently, rate limits are **hardcoded** in `Program.cs`. To make them configurable:

```json
{
  "RateLimiting": {
    "ApiGeneral": {
      "PermitLimit": 100,
      "Window": "00:01:00"
    },
    "ApiStrict": {
      "PermitLimit": 20,
      "Window": "00:01:00"
    },
    "ApiPerIp": {
      "PermitLimit": 50,
      "Window": "00:01:00"
    }
  }
}
```

Then update `Program.cs` to read from configuration:

```csharp
builder.Services.AddRateLimiter(options =>
{
    var config = builder.Configuration.GetSection("RateLimiting");
    
    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = config.GetValue<int>("ApiGeneral:PermitLimit", 100);
        opt.Window = config.GetValue<TimeSpan>("ApiGeneral:Window", TimeSpan.FromMinutes(1));
    });
});
```

---

## ?? Monitoring & Metrics

### Application Insights Integration

When Application Insights is enabled, rate limiting events are logged:

```csharp
_logger.LogWarning("Rate limit exceeded for {Endpoint} from IP {IP}", 
    context.HttpContext.Request.Path, 
    context.HttpContext.Connection.RemoteIpAddress);
```

### Recommended Metrics to Track

1. **429 Response Count** - Track rate limit rejections
2. **Top Rate-Limited IPs** - Identify potential abuse
3. **Endpoint Hit Rates** - Optimize limits per endpoint
4. **Average Queue Time** - Monitor request queuing

### Azure Monitor Query Example

```kusto
requests
| where resultCode == "429"
| summarize count() by client_IP, url
| order by count_ desc
| take 20
```

---

## ?? Testing Rate Limits

### Manual Testing with cURL

```bash
# Test API rate limit (100 req/min)
for i in {1..110}; do
  curl -i https://yourapp.azurewebsites.net/api/M365AppsReleases
done

# Expected: First 100 succeed, next 5 queued, rest return 429
```

### Load Testing with Apache Bench

```bash
# Send 200 requests with 10 concurrent
ab -n 200 -c 10 https://yourapp.azurewebsites.net/api/M365AppsReleases
```

### Automated Testing (xUnit)

```csharp
[Fact]
public async Task Api_ShouldReturnTooManyRequests_WhenRateLimitExceeded()
{
    var client = _factory.CreateClient();
    
    // Send 101 requests
    for (int i = 0; i < 101; i++)
    {
        var response = await client.GetAsync("/api/M365AppsReleases");
        
        if (i < 100)
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }
}
```

---

## ?? Best Practices

### 1. **Choose the Right Policy**
- **High-volume, non-critical**: `api-sliding` (smooth distribution)
- **Resource-intensive**: `api-strict` (heavy throttling)
- **Burst-friendly**: `api-burst` (allow occasional spikes)
- **Concurrent-heavy**: `api-concurrent` (limit simultaneous requests)

### 2. **Per-IP Partitioning**
Use `api-per-ip` for public APIs to prevent single-IP abuse:

```csharp
[EnableRateLimiting("api-per-ip")]
```

### 3. **Combine Policies**
Apply different policies to different endpoints:

```csharp
[EnableRateLimiting("api-sliding")]  // Controller-level
public class MyController : ControllerBase
{
    [EnableRateLimiting("api-strict")]  // Action-level override
    [HttpPost("expensive-operation")]
    public IActionResult ExpensiveOperation() { }
}
```

### 4. **Graceful Degradation**
Always provide `Retry-After` header:

```csharp
rateLimiterOptions.OnRejected = async (context, cancellationToken) =>
{
    context.HttpContext.Response.Headers.RetryAfter = "60";
    await context.HttpContext.Response.WriteAsJsonAsync(new
    {
        error = "Too Many Requests",
        retryAfter = 60
    });
};
```

### 5. **Whitelist Internal IPs**
Exclude health checks and internal monitoring:

```csharp
rateLimiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
{
    var ipAddress = context.Connection.RemoteIpAddress?.ToString();
    
    // Whitelist localhost and internal IPs
    if (ipAddress == "127.0.0.1" || ipAddress == "::1")
        return RateLimitPartition.GetNoLimiter("localhost");
    
    return RateLimitPartition.GetFixedWindowLimiter(ipAddress, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = 100,
        Window = TimeSpan.FromMinutes(1)
    });
});
```

---

## ?? Security Considerations

### 1. **IP Spoofing**
Rate limiting by IP is vulnerable to spoofing if behind a proxy. Use `X-Forwarded-For`:

```csharp
var ipAddress = context.Request.Headers["X-Forwarded-For"].FirstOrDefault() 
    ?? context.Connection.RemoteIpAddress?.ToString() 
    ?? "unknown";
```

**Azure App Service**: Automatically sets `X-Forwarded-For`.

### 2. **Distributed Attacks**
Single-IP rate limiting won't stop distributed attacks from botnets. Consider:
- **Azure Front Door** (WAF + DDoS protection)
- **Cloudflare** (Layer 7 protection)

### 3. **API Keys**
For authenticated APIs, rate limit by API key instead of IP:

```csharp
var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault() ?? "anonymous";
return RateLimitPartition.GetFixedWindowLimiter(apiKey, _ => new FixedWindowRateLimiterOptions { ... });
```

---

## ??? Troubleshooting

### Issue: All requests return 429

**Cause**: Clock skew or rate limit too aggressive

**Solution**:
```csharp
// Increase permit limit temporarily
options.PermitLimit = 1000;
```

### Issue: Queue filling up

**Cause**: Backend too slow to process requests

**Solution**:
1. Reduce queue size
2. Optimize endpoint performance
3. Add concurrency limiter

```csharp
options.QueueLimit = 2; // Reduce queue
```

### Issue: Legitimate users blocked

**Cause**: Shared IP (NAT, corporate proxy)

**Solution**:
1. Use API keys for authentication
2. Increase per-IP limit
3. Use token bucket for burst tolerance

---

## ?? References

- [ASP.NET Core Rate Limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit)
- [System.Threading.RateLimiting](https://learn.microsoft.com/en-us/dotnet/api/system.threading.ratelimiting)
- [Azure App Service Rate Limiting](https://learn.microsoft.com/en-us/azure/app-service/app-service-web-configure-an-app#configure-general-settings)

---

**Implemented**: January 2025  
**Last Updated**: January 2025  
**Author**: Office Versions Core Team
