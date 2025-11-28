# Content Directory

This directory contains auto-generated JSON files created by background web scraping services.

## Structure

```
content/
??? officeversions/          # Office 365 version data
?   ??? .gitkeep             # Ensures directory is tracked in Git
?   ??? m365LatestVersions.json           # Latest version per channel (aggregated)
?   ??? m365CurrentReleases.json          # Full release history - Current Channel
?   ??? m365MonthlyReleases.json          # Full release history - Monthly Enterprise
?   ??? m365SACReleases.json              # Full release history - Semi-Annual Channel
?   ??? m365SACPreviewReleases.json       # Full release history - Semi-Annual Preview
?
??? windowsversions/         # Windows version data
    ??? .gitkeep             # Ensures directory is tracked in Git
    ??? windowsLatestVersions.json        # Latest Windows versions
    ??? windows10-versions.json           # Windows 10 version data
    ??? windows10-updates.json            # Windows 10 update history
    ??? windows10-feature-updates.json    # Windows 10 feature updates
    ??? windows11-versions.json           # Windows 11 version data
    ??? windows11-updates.json            # Windows 11 update history
    ??? windows11-feature-updates.json    # Windows 11 feature updates
    ??? last-update.json                  # Last update timestamp
```

## Background Services

### Office365VersionScraper
- **Enabled**: Configured in `appsettings.json` ? `Office365Scraper:Enabled`
- **Interval**: Configured in `appsettings.json` ? `Office365Scraper:intervalMinutes` (default: 5 minutes)
- **Output**: Writes JSON files to `wwwroot/content/officeversions/`
- **Source**: Scrapes Microsoft Office release data from official sources

### WindowsVersionsScraper
- **Enabled**: Configured in `appsettings.json` ? `WindowsScraper:Enabled`
- **Interval**: Configured in `appsettings.json` ? `WindowsScraper:intervalMinutes` (default: 60 minutes)
- **Output**: Writes JSON files to `wwwroot/content/windowsversions/`
- **Source**: Scrapes Windows release data from official Microsoft sources

## Git Ignore Rules

All JSON files in these directories are **excluded from version control** via `.gitignore`:

```gitignore
# Web scraping output - Office 365 versions JSON files
wwwroot/content/officeversions/*.json
!wwwroot/content/officeversions/.gitkeep

# Web scraping output - Windows versions JSON files
wwwroot/content/windowsversions/*.json
!wwwroot/content/windowsversions/.gitkeep

# Any other JSON files in content directories (catch-all)
wwwroot/content/**/*.json
!wwwroot/content/**/.gitkeep
```

## Why Exclude These Files?

1. **Auto-generated**: These files are automatically created and updated by background services
2. **Frequently updated**: They change every few minutes/hours based on scraper intervals
3. **Large size**: JSON files can become large over time with historical data
4. **Environment-specific**: Each deployment generates its own data based on live scraping
5. **Not source code**: These are data files, not application source code

## First Run

On first application startup:
- Directories are automatically created by `LocalStorageService`
- Background scrapers will populate the directories with JSON files
- `.gitkeep` files ensure empty directories are tracked in Git

## Azure Deployment

When deploying to Azure:
- These directories are created automatically on first run
- Background scrapers populate them with fresh data
- No need to manually upload JSON files
- Data is stored in Azure App Service local storage (ephemeral)

## Storage Service

The `LocalStorageService` manages file operations:
- **Base Path**: `wwwroot/content/` (configured in `LocalStorageService.cs`)
- **Security**: Prevents directory traversal attacks
- **Logging**: All file operations are logged via Serilog
- **Auto-create**: Directories are created automatically if missing

## Configuration

### appsettings.json

```json
{
  "Office365": {
    "StoragePath": "content/officeversions",
    "LatestVersionsFile": "m365LatestVersions.json",
    "CurrentChannelFile": "m365CurrentReleases.json",
    "MonthlyEnterpriseChannelFile": "m365MonthlyReleases.json",
    "SemiAnnualChannelFile": "m365SACReleases.json",
    "SemiAnnualPreviewChannelFile": "m365SACPreviewReleases.json",
    "UseLocalStorage": true
  },
  "Office365Scraper": {
    "Enabled": true,
    "intervalMinutes": 5
  },
  "WindowsVersions": {
    "StoragePath": "content/windowsversions",
    "LatestVersionsFile": "windowsLatestVersions.json",
    "UseLocalStorage": true
  },
  "WindowsScraper": {
    "Enabled": true,
    "intervalMinutes": 60
  }
}
```

## Troubleshooting

### Files Not Being Created
1. Check if scrapers are enabled in `appsettings.json`
2. Check logs in `Logs/` directory for errors
3. Visit `/Diagnostic/StorageAccess` page to verify file creation
4. Ensure write permissions exist for the `wwwroot/content/` directory

### Files Not Being Updated
1. Check scraper interval settings
2. Verify background services are running (check startup logs)
3. Check for exceptions in Serilog output
4. Review Application Insights telemetry (if enabled)

### Missing Directories
- Directories are auto-created by `LocalStorageService` on first access
- If missing, restart the application to trigger auto-creation
- Check file system permissions

## Monitoring

### Health Checks
The application includes health checks for storage:
- **Endpoint**: `/health`
- **Check**: `StorageHealthCheck` - Verifies storage access
- **Check**: `DataFreshnessHealthCheck` - Verifies data is up-to-date

### Diagnostic Page
Visit `/Diagnostic/StorageAccess` to view:
- Current storage paths
- List of files in each directory
- Scraper status and configuration

---

**Note**: Never commit JSON files from these directories to Git. They are regenerated automatically and are excluded via `.gitignore`.
