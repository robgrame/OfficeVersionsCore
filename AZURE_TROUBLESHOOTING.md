# Azure HTTP 500 Troubleshooting Guide

## Fixes Applied

### 1. **Enhanced Error Logging in Error.cshtml.cs**
- Now logs full exception details to Application Insights
- Captures status code and original path for diagnostics

### 2. **GSC Initializer Made Resilient**
- Changed from `GetRequiredService` to `GetService` (optional)
- Wrapped in try-catch with detailed logging
- Startup continues even if GSC initialization fails

### 3. **Global Exception Handler in Program.cs**
- Entire app wrapped in try-catch block
- Fatal errors logged to console with full stack trace
- Inner exceptions captured

### 4. **Startup Diagnostic Logging**
- Console output for every startup phase
- Environment details logged
- GSC initialization status reported

### 5. **New Diagnostic Endpoints**
```
GET /api/Diagnostics/health   - Full diagnostic information
GET /api/Diagnostics/env      - Environment variables (safe)
GET /api/Diagnostics/crash    - Force crash (Dev only)
```

## How to Diagnose HTTP 500 on Azure

### Step 1: Check Application Logs
1. Go to Azure Portal ? App Service ? **Log stream**
2. Look for `[STARTUP]` messages
3. Look for `[FATAL]` messages

### Step 2: Check Application Insights
1. Azure Portal ? App Service ? Application Insights
2. Navigate to **Failures**
3. Filter by **Last 24 hours**
4. Look for exceptions during startup

### Step 3: Use Diagnostic Endpoint
Visit: `https://officeversions.azurewebsites.net/api/Diagnostics/health`

This will show:
- Environment configuration
- Storage access status
- Required files presence
- Configuration values

### Step 4: Check Specific Issues

#### Issue: CSV File Missing
**Symptom:** GSC initialization fails
**Fix:** Ensure `wwwroot/data/gsc-export.csv` is included in publish
**Check:** `/api/Diagnostics/health` ? Files section

#### Issue: Application Insights Misconfigured
**Symptom:** Serilog configuration fails
**Fix:** Already handled with try-catch in Program.cs
**Check:** `/api/Diagnostics/health` ? Configuration.HasApplicationInsights

#### Issue: Storage Access Failed
**Symptom:** LocalStorageService can't write files
**Fix:** Check App Service file system permissions
**Check:** `/api/Diagnostics/health` ? Storage section

#### Issue: .NET 10 Runtime Not Available
**Symptom:** App won't start at all
**Fix:** Ensure Azure App Service has .NET 10 runtime
**Check:** Azure Portal ? App Service ? Configuration ? Stack settings

## Common HTTP 500 Causes in Azure

### 1. Missing Dependencies
- **Solution:** All packages are in .csproj, should auto-restore
- **Check:** Build logs in Azure Deployment Center

### 2. File Not Found
- **Solution:** Ensure Content files are marked `CopyToPublishDirectory="PreserveNewest"`
- **Check:** Look for file paths in error logs

### 3. Configuration Missing
- **Solution:** Add App Settings in Azure Portal
- **Required:** ApplicationInsights:ConnectionString (optional but recommended)

### 4. Background Services Crash
- **Solution:** Disabled by default in appsettings.json
- **To Enable:** Set `Office365Scraper:Enabled` to `true` in App Settings

## Quick Diagnostic Commands

### Via Kudu Console (Advanced)
1. Go to: `https://officeversions.scm.azurewebsites.net/DebugConsole`
2. Navigate to: `site/wwwroot`
3. Check files exist:
   ```cmd
   dir wwwroot\data\gsc-export.csv
   dir content\officeversions
   dir content\windowsversions
   ```

### Via Azure CLI
```bash
# View logs
az webapp log tail --name OfficeVersions --resource-group OfficeVersionsCore

# Download logs
az webapp log download --name OfficeVersions --resource-group OfficeVersionsCore --log-file app-logs.zip
```

## Expected Startup Output

If working correctly, you should see:
```
[STARTUP] Starting OfficeVersionsCore at 2025-01-XX XX:XX:XX UTC
[STARTUP] Environment: Production
[STARTUP] Content Root: D:\home\site\wwwroot
[STARTUP] Web Root: D:\home\site\wwwroot\wwwroot
[STARTUP] Application built successfully
[STARTUP] Is Development: False
[STARTUP] Initializing Google Search Console data...
[STARTUP] GSC initialization: SUCCESS
[STARTUP] Starting web server...
```

## Emergency Rollback

If all else fails, temporarily disable features:

### Disable GSC Integration
In Azure App Settings, add:
```
GoogleSearchConsole:Enabled = false
```

### Disable Background Scrapers
Already disabled in appsettings.json:
```json
"Office365Scraper": { "Enabled": false },
"WindowsScraper": { "Enabled": false }
```

### Minimal Mode
Set all optional features to false and redeploy.

## Contact Points

- **Application Logs:** Azure Portal ? Log Stream
- **Application Insights:** Azure Portal ? Application Insights
- **Diagnostic Health:** `/api/Diagnostics/health`
- **Standard Health Check:** `/health`

## Next Steps After Fixing

1. Test locally with `dotnet run`
2. Publish to Azure
3. Check `/api/Diagnostics/health`
4. Monitor Application Insights for 5-10 minutes
5. Test main pages: `/`, `/Current`, `/Windows/Index`
