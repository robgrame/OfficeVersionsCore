# Office 365 Version Scraper

This project contains a .NET implementation of the Office 365 version scraper that was originally implemented in PowerShell.

## Integration Options

There are three ways to use the Office 365 version scraper:

1. **As a background service in the web application**
2. **As a standalone console application**
3. **As an Azure Function**

### 1. Background Service in Web Application

The `Office365VersionScraper` class is implemented as a .NET `BackgroundService` that can run within the web application. To enable it:

1. Update the `appsettings.json` file:

```json
"Office365Scraper": {
  "Enabled": true,
  "IntervalHours": 6
}
```

2. The scraper is already registered in `Program.cs` and will automatically start when the application starts if enabled.

### 2. Standalone Console Application

A standalone console application is available in the `CommandLine` folder. To use it:

1. Navigate to the `CommandLine` directory
2. Build the project: `dotnet build`
3. Run the application: `dotnet run`

You can also publish it as a standalone application:

```
dotnet publish -c Release -o ./publish
```

### 3. Azure Function

The Azure Functions implementation is in the `Functions` folder. It includes:

1. A timer-triggered function that runs on a schedule (default: every 6 hours)
2. All the necessary configuration files

To deploy it to Azure:

1. Navigate to the `Functions` directory
2. Publish the function to Azure:

```
dotnet publish -c Release
func azure functionapp publish <YourFunctionAppName>
```

Make sure to set up the appropriate connection strings and environment variables in the Azure Function App configuration:

- `securesajson_STORAGE`: Azure Storage connection string
- `SEC_STOR_StorageCon`: Container name (default: "jsonrepository")

## Configuration

The following configuration settings are used:

- `Office365Scraper:Enabled`: Boolean indicating whether the background service should be enabled
- `Office365Scraper:IntervalHours`: Integer indicating how often the scraper should run (in hours)
- `securesajson_STORAGE`: Azure Storage connection string for blob storage
- `SEC_STOR_StorageCon`: Container name for storing the JSON files

## Generated JSON Files

The scraper generates the following JSON files:

1. `m365LatestVersions.json`: Latest versions for all channels
2. `m365releases.json`: All releases for all channels
3. `m365CurrentReleases.json`: All releases for Current Channel
4. `m365MonthlyReleases.json`: All releases for Monthly Enterprise Channel
5. `m365SACReleases.json`: All releases for Semi-Annual Enterprise Channel
6. `m365SACPreviewReleases.json`: All releases for Semi-Annual Enterprise Preview

These files are uploaded to Azure Blob Storage and can be accessed by the web application.