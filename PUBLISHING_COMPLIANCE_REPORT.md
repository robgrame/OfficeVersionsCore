# Publishing Configuration Compliance Report

**Generated:** 2025-01-15  
**Project:** OfficeVersionsCore  
**Target:** Azure App Service (.NET 10, x64)

---

## ? **CONFORMITÀ VERIFICATA**

### ?? **Summary**

| Componente | Stato | Note |
|------------|-------|------|
| **OfficeVersionsCore.csproj** | ? CONFORME | Configurazione corretta |
| **PROD Publish Profile** | ? CORRETTO | Ora in Release mode |
| **DEV Publish Profile** | ? CONFORME | Già corretto |
| **File CSV** | ? PRESENTE | gsc-export.csv incluso |
| **Program.cs** | ? RESILIENTE | Try-catch globale |

---

## ?? **1. OfficeVersionsCore.csproj**

### ? **Configurazione Base**
```xml
<TargetFramework>net10.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<GenerateDocumentationFile>true</GenerateDocumentationFile>
```
**Status:** ? Corretto per .NET 10

### ? **Configurazione Azure (Release)**
```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <PlatformTarget>x64</PlatformTarget>
  <SelfContained>false</SelfContained>
  <PublishReadyToRun>false</PublishReadyToRun>
</PropertyGroup>
```
**Status:** ? Ottimizzato per Azure App Service x64

### ? **File CSV Incluso**
```xml
<ItemGroup>
  <None Include="wwwroot\data\gsc-export.csv">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
  </None>
</ItemGroup>
```
**Status:** ? File CSV sarà pubblicato su Azure  
**Location:** `wwwroot/data/gsc-export.csv`  
**Size:** 600+ queries, 62.4 KB

### ? **Swagger Files**
```xml
<Content Update="wwwroot\**\swagger.json" CopyToPublishDirectory="PreserveNewest" />
```
**Status:** ? Swagger docs inclusi

---

## ?? **2. PRODUCTION Publish Profile**

**File:** `Properties/PublishProfiles/OfficeVersions - Web Deploy.pubxml`

### ?? **Correzioni Applicate**

| Proprietà | Prima (?) | Dopo (?) | Motivo |
|-----------|-----------|----------|---------|
| **LastUsedBuildConfiguration** | Debug | **Release** | Production deve usare Release |
| **LastUsedPlatform** | Any CPU | **x64** | Match con Azure x64 |
| **RuntimeIdentifier** | win-x64 | *(removed)* | Framework-Dependent Deploy |
| **SkipExtraFilesOnServer** | false | **true** | Pulisce vecchi file |

### ? **Configurazione Finale PRODUCTION**
```xml
<PropertyGroup>
  <WebPublishMethod>MSDeploy</WebPublishMethod>
  <LastUsedBuildConfiguration>Release</LastUsedBuildConfiguration>
  <LastUsedPlatform>x64</LastUsedPlatform>
  <SkipExtraFilesOnServer>true</SkipExtraFilesOnServer>
  <SelfContained>false</SelfContained>
  <TargetFramework>net10.0</TargetFramework>
  <EnableMsDeployAppOffline>true</EnableMsDeployAppOffline>
  <EnableMSDeployBackup>true</EnableMSDeployBackup>
</PropertyGroup>
```

**URL:** `https://officeversions.azurewebsites.net`  
**Resource Group:** OfficeVersionsCore  
**Deploy Method:** Web Deploy (MSDeploy)

---

## ?? **3. DEV Publish Profile**

**File:** `Properties/PublishProfiles/OfficeVersionsCore-DEV - Zip Deploy.pubxml`

### ? **Configurazione (Già Corretta)**
```xml
<LastUsedBuildConfiguration>Release</LastUsedBuildConfiguration>
<LastUsedPlatform>x64</LastUsedPlatform>
<RuntimeIdentifier></RuntimeIdentifier>
<SelfContained>false</SelfContained>
```

**URL:** `https://officeversionscore-dev.azurewebsites.net`  
**Resource Group:** OfficeVersionsCore-DEV-RG  
**Deploy Method:** Zip Deploy

---

## ?? **4. Deployment Type Analysis**

### Framework-Dependent Deployment (FDD) ?

**Configurazione:**
- ? **NO** RuntimeIdentifier specified
- ? **SelfContained=false**
- ? **PlatformTarget=x64** (solo Release)

**Vantaggi:**
- ? Package size ridotto (~50 MB invece di ~150 MB)
- ? Deploy più veloce
- ? Usa .NET runtime di Azure (già installato)
- ? Aggiornamenti runtime automatici da Azure

**Requisiti Azure:**
- ? Azure App Service deve avere .NET 10 runtime
- ? Stack setting: .NET 10 x64

---

## ?? **5. File Pubblicati**

### File Essenziali che SARANNO Pubblicati

| File/Cartella | Tipo | Configurazione | Status |
|---------------|------|----------------|--------|
| **wwwroot/data/gsc-export.csv** | Data | None Include + CopyToPublish | ? Incluso |
| **wwwroot/swagger/v1/swagger.json** | Docs | Content Update | ? Incluso |
| **appsettings.json** | Config | Default | ? Incluso |
| **Program.cs** | Code | Compiled | ? Incluso |
| **wwwroot/css/** | Static | Default | ? Incluso |
| **wwwroot/js/** | Static | Default | ? Incluso |

### File/Cartelle ESCLUSE dal Publishing

| File/Cartella | Motivo |
|---------------|--------|
| **CommandLine/** | Removed in .csproj |
| **Functions/** | Removed in .csproj |
| **Logs/** | Removed in .csproj |
| **bin/Debug/** | Only Release is published |
| **obj/** | Build artifacts excluded |

---

## ?? **6. Sicurezza e Best Practices**

### ? **Security Headers** (Program.cs)
```csharp
- Strict-Transport-Security: max-age=31536000
- X-Content-Type-Options: nosniff
- X-Frame-Options: DENY
- X-XSS-Protection: 1; mode=block
- Referrer-Policy: strict-origin-when-cross-origin
```

### ? **Error Handling**
- Global try-catch in Program.cs
- Exception logging to Application Insights
- Custom Error.cshtml page
- Diagnostic endpoints: `/api/Diagnostics/health`

### ? **Logging**
- Serilog configured
- Application Insights sink (Production)
- Console output for startup diagnostics
- File logging: `Logs/log-*.log` (7 days retention)

---

## ?? **7. Deployment Commands**

### **Production Deploy (Web Deploy)**
```bash
# Via CLI
dotnet publish -c Release /p:PublishProfile="OfficeVersions - Web Deploy"

# Via Visual Studio
Right-click project ? Publish ? Select "OfficeVersions - Web Deploy" ? Publish
```

### **Development Deploy (Zip Deploy)**
```bash
# Via CLI
dotnet publish -c Release /p:PublishProfile="OfficeVersionsCore-DEV - Zip Deploy"

# Via Visual Studio
Right-click project ? Publish ? Select "OfficeVersionsCore-DEV - Zip Deploy" ? Publish
```

### **Verification Post-Deploy**
```bash
# 1. Health Check
curl https://officeversions.azurewebsites.net/health

# 2. Diagnostic Endpoint
curl https://officeversions.azurewebsites.net/api/Diagnostics/health

# 3. Check if CSV loaded
curl https://officeversions.azurewebsites.net/api/M365AppsReleases
# Look for "dataForNerds.lastUpdatedUTC"

# 4. Check Swagger
curl https://officeversions.azurewebsites.net/swagger
```

---

## ?? **8. Azure App Service Configuration Required**

### **Application Settings** (Azure Portal)
```json
{
  "ApplicationInsights__ConnectionString": "<your-connection-string>",
  "ASPNETCORE_ENVIRONMENT": "Production",
  "Office365Scraper__Enabled": "false",
  "WindowsScraper__Enabled": "false"
}
```

### **Stack Settings**
- **Stack:** .NET
- **Version:** .NET 10
- **Platform:** 64-bit (x64)
- **Always On:** Enabled (recommended)

### **Deployment Settings**
- **SCM Basic Auth:** Enabled
- **Deployment Center:** GitHub / Local Git / Web Deploy

---

## ?? **9. Performance Optimization**

### ? **Enabled Features**
- Response Compression (GZIP)
- Rate Limiting (100 requests/minute)
- In-Memory Caching (IMemoryCache)
- Static File Caching

### ?? **Optional Optimizations**
```xml
<!-- Can be added to Release PropertyGroup if needed -->
<PublishReadyToRun>true</PublishReadyToRun>  <!-- AOT compilation -->
<PublishTrimmed>false</PublishTrimmed>  <!-- Don't trim for Razor Pages -->
```

---

## ?? **10. Checklist Pre-Deployment**

### Before Publishing to PRODUCTION:

- [x] Build configuration is **Release** ?
- [x] Platform is **x64** ?
- [x] CSV file exists: `wwwroot/data/gsc-export.csv` ?
- [x] No RuntimeIdentifier in publish profile ?
- [x] SelfContained=false (Framework-Dependent) ?
- [x] SkipExtraFilesOnServer=true ?
- [x] Application Insights configured ?
- [x] Global exception handling in place ?
- [x] Diagnostic endpoint available ?
- [x] Security headers configured ?

### After Deployment:

- [ ] Verify `/health` returns 200 OK
- [ ] Check `/api/Diagnostics/health` shows Storage.CanWrite=true
- [ ] Verify CSV file loaded: check API response for GSC data
- [ ] Monitor Application Insights for errors
- [ ] Check Log Stream for `[STARTUP]` messages
- [ ] Test main pages: `/`, `/Current`, `/Windows/Index`

---

## ?? **11. Troubleshooting**

### If HTTP 500 After Deploy:

1. **Check Azure Log Stream**
   ```
   Azure Portal ? App Service ? Log stream
   Look for: [FATAL] or [STARTUP] messages
   ```

2. **Use Diagnostic Endpoint**
   ```
   https://officeversions.azurewebsites.net/api/Diagnostics/health
   ```

3. **Verify CSV File**
   ```
   Kudu Console: https://officeversions.scm.azurewebsites.net/DebugConsole
   Navigate to: site/wwwroot/wwwroot/data/
   Check: gsc-export.csv exists
   ```

4. **Check Application Insights**
   ```
   Azure Portal ? Application Insights ? Failures
   Time range: Last 1 hour
   ```

### Common Issues:

| Issue | Cause | Fix |
|-------|-------|-----|
| HTTP 500 on startup | CSV not found | Check diagnostic endpoint |
| Slow startup | AppInsights timeout | Already handled with try-catch |
| Old files remain | SkipExtraFiles=false | Now set to true ? |
| Wrong .NET version | Stack setting mismatch | Set to .NET 10 x64 |

---

## ?? **12. Conclusion**

### ? **All Compliance Issues RESOLVED**

1. ? **PRODUCTION Profile:** Fixed Debug ? Release, Any CPU ? x64
2. ? **CSV File:** Now correctly included in publish
3. ? **Deployment Type:** Framework-Dependent (FDD) configured
4. ? **Error Handling:** Global exception catching
5. ? **Diagnostics:** Health endpoints available

### ?? **Ready for Production Deployment**

The project is now **fully compliant** with Azure App Service best practices for .NET 10 x64 deployment.

**Next Step:** Redeploy to Azure using the corrected publish profile.

---

**Document Version:** 1.0  
**Last Updated:** 2025-01-15  
**Author:** GitHub Copilot  
**Project:** OfficeVersionsCore
