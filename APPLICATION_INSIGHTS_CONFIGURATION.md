# Application Insights Configuration Guide

## Overview

This document describes the complete Application Insights telemetry configuration for OfficeVersionsCore, including both **server-side** and **client-side** tracking.

---

## Architecture

### Dual Tracking Strategy

```
???????????????????????????????????????????????????????????
?                    Browser (Client)                      ?
?  ?????????????????????????????????????????????????????? ?
?  ?  Application Insights JavaScript SDK v3             ? ?
?  ?  - Page Views                                        ? ?
?  ?  - User Behavior (clicks, scrolls)                  ? ?
?  ?  ?  - AJAX/Fetch Tracking                            ? ?
?  ?  - Performance Metrics (load times)                 ? ?
?  ?  - Client-side Exceptions (JavaScript errors)       ? ?
?  ?  - Route Tracking (SPA navigation)                  ? ?
?  ?????????????????????????????????????????????????????? ?
???????????????????????????????????????????????????????????
                            ?
                            ? HTTPS POST
                            ?
         ???????????????????????????????????????
         ?   Azure Application Insights         ?
         ?   (Ingestion Endpoint)               ?
         ???????????????????????????????????????
                            ?
                            ? Telemetry SDK
                            ?
???????????????????????????????????????????????????????????
?                  ASP.NET Core Server                     ?
?  ?????????????????????????????????????????????????????? ?
?  ?  Application Insights Server SDK                    ? ?
?  ?  - HTTP Requests                                    ? ?
?  ?  - Dependencies (external APIs, database)          ? ?
?  ?  - Server Exceptions                               ? ?
?  ?  - Custom Events                                   ? ?
?  ?  - Trace Logging (via Serilog)                     ? ?
?  ?????????????????????????????????????????????????????? ?
???????????????????????????????????????????????????????????
```

---

## Server-Side Configuration

### 1. NuGet Packages

Ensure the following packages are installed:

```xml
<PackageReference Include="Microsoft.ApplicationInsights.AspNetCore" Version="2.22.0" />
<PackageReference Include="Serilog.Sinks.ApplicationInsights" Version="4.0.0" />
```

### 2. Program.cs Setup

```csharp
// Application Insights Telemetry (conditional)
var aiConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(aiConnectionString))
{
    Console.WriteLine("[STARTUP] Application Insights: ENABLED");
    builder.Services.AddApplicationInsightsTelemetry(options =>
    {
        options.ConnectionString = aiConnectionString;
    });
}
else
{
    Console.WriteLine("[STARTUP] Application Insights: DISABLED (no connection string found)");
}
```

### 3. Serilog Integration

Application Insights sink is configured for Production environment:

```csharp
if (builder.Environment.IsProduction() && !string.IsNullOrWhiteSpace(aiConnectionString))
{
    Log.Logger = new LoggerConfiguration()
        .WriteTo.ApplicationInsights(
            new TelemetryConfiguration { ConnectionString = aiConnectionString },
            TelemetryConverter.Traces)
        .CreateLogger();
}
```

### 4. Environment Variable

Set the connection string in Azure App Service Configuration or `appsettings.json`:

**Azure App Service:**
```
Configuration > Application settings
Name: APPLICATIONINSIGHTS_CONNECTION_STRING
Value: InstrumentationKey=...;IngestionEndpoint=https://...
```

**appsettings.json (local development):**
```json
{
  "APPLICATIONINSIGHTS_CONNECTION_STRING": "InstrumentationKey=...;IngestionEndpoint=https://..."
}
```

---

## Client-Side Configuration

### 1. JavaScript SDK Loader

File: `wwwroot/js/appinsights-loader.js`

This file contains the **Application Insights JavaScript SDK v3** snippet that:
- Loads the SDK from Azure CDN (`https://js.monitor.azure.com/scripts/b/ai.3.gbl.min.js`)
- Initializes with connection string from `window.appInsightsConnectionString`
- Auto-tracks page views, AJAX calls, fetch requests, and unhandled exceptions

### 2. Layout Integration

File: `Pages/Shared/_Layout.cshtml`

```razor
@{
    var aiConnectionString = Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
}
@if (!string.IsNullOrWhiteSpace(aiConnectionString))
{
    <script>
    // Pass Application Insights connection string to loader script
    window.appInsightsConnectionString = '@Html.Raw(aiConnectionString)';
    </script>
    <script src="~/js/appinsights-loader.js" defer></script>
}
else
{
    <script>
    console.log('[Application Insights] Client-side tracking disabled (no connection string)');
    </script>
}
```

**Why separate file?**
The Application Insights JavaScript snippet contains characters (`@`, `.concat()`) that conflict with Razor syntax parser. Keeping it in a separate `.js` file avoids compilation errors.

### 3. Enabled Features

The client-side SDK is configured with the following features:

| Feature | Enabled | Description |
|---------|---------|-------------|
| `enableAutoRouteTracking` | ? | Tracks SPA route changes as page views |
| `enableCorsCorrelation` | ? | Correlates client and server telemetry via headers |
| `enableRequestHeaderTracking` | ? | Tracks AJAX/fetch request headers |
| `enableResponseHeaderTracking` | ? | Tracks AJAX/fetch response headers |
| `disableExceptionTracking` | ? (false) | Tracks uncaught JavaScript exceptions |
| `disableFetchTracking` | ? (false) | Tracks fetch() API calls |
| `disableAjaxTracking` | ? (false) | Tracks XMLHttpRequest calls |
| `autoTrackPageVisitTime` | ? | Automatically measures page visit duration |
| `enableUnhandledPromiseRejectionTracking` | ? | Tracks unhandled promise rejections |

---

## Telemetry Data Collected

### Client-Side (Browser)

1. **Page Views**
   - Page URL, title, referrer
   - Custom properties (e.g., browser, device type)
   - Automatically tracked on page load and route changes

2. **Performance Metrics**
   - Page load time
   - AJAX/fetch request duration
   - Network timing (DNS, TCP, SSL)

3. **User Actions**
   - Custom events (via `appInsights.trackEvent()`)
   - Click tracking (if Click Analytics plugin enabled)

4. **Exceptions**
   - JavaScript errors (stack trace, line number)
   - Unhandled promise rejections

5. **AJAX/Fetch Calls**
   - URL, method, status code, duration
   - Request/response headers (if enabled)

### Server-Side (ASP.NET Core)

1. **HTTP Requests**
   - Request URL, method, status code
   - Response time, server name
   - User agent, client IP (via X-Forwarded-For)

2. **Dependencies**
   - HTTP calls to external APIs
   - Database queries (if using Entity Framework)
   - Duration, success/failure status

3. **Exceptions**
   - Unhandled exceptions with full stack trace
   - Custom logged exceptions

4. **Custom Events**
   - Application-specific events (e.g., scraping completed)
   - Custom metrics (e.g., data refresh duration)

5. **Trace Logs**
   - Serilog log entries (Information, Warning, Error, Critical)
   - Structured logging with properties

---

## Verification & Testing

### 1. Local Development

1. Set `APPLICATIONINSIGHTS_CONNECTION_STRING` in `appsettings.Development.json`
2. Run the application
3. Check browser console for:
   ```
   [Application Insights] Client-side tracking enabled
   ```
4. Check server console for:
   ```
   [STARTUP] Application Insights: ENABLED
   ```

### 2. Browser DevTools

Open **Network** tab and filter by `monitor.azure.com`:
- You should see POST requests to Application Insights ingestion endpoint
- Payload contains telemetry data (page views, events, etc.)

### 3. Azure Portal

Navigate to **Azure Portal > Application Insights Resource**:

1. **Live Metrics**: Real-time telemetry (page views, requests, failures)
2. **Transaction Search**: Individual telemetry items
3. **Application Map**: Visualize dependencies between components
4. **Performance**: Analyze slow requests and operations
5. **Failures**: View exceptions and failed requests

---

## Querying Telemetry Data

### Kusto Query Language (KQL) Examples

**Page Views in last 24 hours:**
```kql
pageViews
| where timestamp > ago(24h)
| summarize count() by bin(timestamp, 1h), name
| render timechart
```

**Top 10 slowest pages:**
```kql
pageViews
| where timestamp > ago(7d)
| summarize avg(duration), count() by name
| top 10 by avg_duration desc
```

**JavaScript exceptions:**
```kql
exceptions
| where client_Type == "Browser"
| where timestamp > ago(24h)
| project timestamp, problemId, outerMessage, innermostMessage, client_Browser
| order by timestamp desc
```

**AJAX calls to API endpoints:**
```kql
dependencies
| where type == "Ajax"
| where timestamp > ago(1h)
| summarize count(), avg(duration) by target
| order by count_ desc
```

**Correlated client + server telemetry:**
```kql
union pageViews, requests
| where timestamp > ago(1h)
| where operation_Id != ""
| project timestamp, itemType, name, operation_Id, operation_Name
| order by timestamp desc
```

---

## Best Practices

### ? Do

- **Use connection string** (not instrumentation key, deprecated after March 31, 2025)
- **Enable CORS correlation** to link client and server telemetry
- **Set sampling** for high-traffic applications to reduce costs
- **Add custom properties** to telemetry for better filtering
- **Monitor quota** and alerting in Azure Portal

### ? Don't

- **Don't hardcode connection strings** in source code (use environment variables)
- **Don't log PII** (Personally Identifiable Information) in custom events
- **Don't disable exception tracking** unless absolutely necessary
- **Don't ignore sampling** for cost optimization in production

---

## Troubleshooting

### Problem: No telemetry in Azure Portal

**Checks:**
1. Verify `APPLICATIONINSIGHTS_CONNECTION_STRING` is set correctly
2. Check browser console for errors
3. Verify firewall/proxy allows traffic to `*.monitor.azure.com`
4. Check Network tab for failed POST requests
5. Enable `loggingLevelConsole: 2` in SDK config for debugging

### Problem: Client-side script not loading

**Checks:**
1. Verify `wwwroot/js/appinsights-loader.js` exists and is served correctly
2. Check `_Layout.cshtml` includes the script tag
3. Verify no CSP (Content Security Policy) blocking the script
4. Check browser console for `[Application Insights]` messages

### Problem: Missing page view tracking

**Checks:**
1. Ensure `enableAutoRouteTracking` is `true`
2. Verify `trackPageView()` is called in snippet
3. Check if ad blockers are blocking telemetry
4. Verify connection string is valid

---

## Performance Impact

### Client-Side

- **Script size**: ~35 KB (minified, gzipped)
- **Load time**: Async loading with `defer` attribute (non-blocking)
- **Telemetry overhead**: < 5 KB per page view
- **Impact**: Negligible on page load performance

### Server-Side

- **Memory**: ~10-20 MB for telemetry SDK
- **CPU**: < 1% overhead for typical workloads
- **Network**: Batched telemetry uploads (every 30 seconds or 500 items)

---

## Cost Optimization

### Sampling

For high-traffic applications, enable adaptive sampling to reduce ingestion costs:

```csharp
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = aiConnectionString;
    options.EnableAdaptiveSampling = true; // Default: true
});
```

**Client-side sampling:**
```javascript
{
    "cfg": {
        "samplingPercentage": 50, // Sample 50% of telemetry
        ...
    }
}
```

### Data Retention

- **Default**: 90 days
- **Extended**: Up to 730 days (additional cost)
- Configure in **Azure Portal > Application Insights > Usage and estimated costs**

---

## Security Considerations

1. **Connection String Protection**
   - Store in Azure Key Vault for production
   - Use managed identity to retrieve secrets
   - Never commit to source control

2. **Data Privacy**
   - Configure IP address masking
   - Filter sensitive data using telemetry initializers
   - Comply with GDPR/data residency requirements

3. **CORS Configuration**
   - Ensure `enableCorsCorrelation` is enabled for accurate tracing
   - Configure CORS headers on API endpoints

---

## Related Documentation

- [Microsoft Docs: Application Insights JavaScript SDK](https://learn.microsoft.com/en-us/azure/azure-monitor/app/javascript-sdk)
- [Microsoft Docs: ASP.NET Core Application Insights](https://learn.microsoft.com/en-us/azure/azure-monitor/app/asp-net-core)
- [Serilog Application Insights Sink](https://github.com/serilog-contrib/serilog-sinks-applicationinsights)
- [Application Insights Pricing](https://azure.microsoft.com/en-us/pricing/details/monitor/)

---

## Changelog

| Date | Change | Author |
|------|--------|--------|
| 2025-01-XX | Initial implementation of client-side tracking | Office365Versions Team |
| 2025-01-XX | Server-side telemetry with Serilog integration | Office365Versions Team |
| 2025-01-XX | Conditional loading based on connection string | Office365Versions Team |

---

## Support

For questions or issues related to Application Insights configuration, contact:
- **Email**: info@office365versions.com
- **GitHub Issues**: https://github.com/robgrame/OfficeVersionsCore/issues
