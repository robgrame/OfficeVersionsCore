# OfficeVersionsCore

Open-source .NET 8 Razor Pages web app and lightweight API for tracking Microsoft 365 (Office 365) client versions. It provides a modern UI, endpoints for programmatic access, and operational telemetry/logging.

- Live API docs: navigate to `/swagger` when running locally
- GitHub repository: https://github.com/robgrame/OfficeVersionsCore


## Features

- Razor Pages frontend (ASP.NET Core, .NET 8)
- API with Swagger UI documentation at `/swagger`
- Azure Storage (Blobs) integration for data persistence
- First-class Azure auth using `Azure.Identity` (Managed Identity or Service Principal)
- Telemetry with Application Insights
- Structured logging with Serilog (console/file/Application Insights)
- Responsive UI using Bootstrap 5 and DataTables.js


## Tech stack

- .NET 8, C# 12
- ASP.NET Core Razor Pages
- Azure SDKs: `Azure.Storage.Blobs`, `Microsoft.Extensions.Azure`, `Azure.Identity`
- Observability: `Microsoft.ApplicationInsights.AspNetCore`
- Logging: `Serilog.*`
- Swagger UI: `Swashbuckle.AspNetCore.SwaggerUI` (static `swagger.json` generated at build)


## Getting started

### Prerequisites

- .NET 8 SDK
- Optional: Visual Studio 2022 (17.8+) or VS Code + C# Dev Kit
- Optional for cloud integration: Azure subscription (for Blob Storage and Application Insights)

### Clone

- git clone https://github.com/robgrame/OfficeVersionsCore.git
- cd OfficeVersionsCore

### Build and run

- dotnet restore
- dotnet build
- dotnet run

By default ASP.NET Core shows the listening URLs in the console. Open the HTTPS address in your browser, then navigate to `/swagger` for the API UI.


## Configuration

Configuration sources follow standard ASP.NET Core order: `appsettings.json`, environment-specific files, environment variables, and Azure-hosted providers.

Common settings you may use:

- Application Insights
  - `ApplicationInsights:ConnectionString`
- Azure Storage (choose one approach)
  - Connection string: `Azure:Storage:ConnectionString`
  - Or Managed Identity/Service Principal via `Azure.Identity` (no connection string required)
  - Optionally: `Azure:Storage:Container`
- Serilog (optional overrides)
  - `Serilog:MinimumLevel`
  - `Serilog:WriteTo` sinks (Console, File, ApplicationInsights)

Example `appsettings.Development.json` snippet:

```json
{
  "ApplicationInsights": {
    "ConnectionString": "<app-insights-connection-string>"
  },
  "Azure": {
    "Storage": {
      "ConnectionString": "<storage-connection-string>",
      "Container": "<container-name>"
    }
  },
  "Serilog": {
    "Using": [
      "Serilog.Sinks.Console",
      "Serilog.Sinks.File",
      "Serilog.Sinks.ApplicationInsights"
    ],
    "MinimumLevel": "Information",
    "WriteTo": [
      { "Name": "Console" },
      { "Name": "File", "Args": { "path": "logs/log-.txt", "rollingInterval": "Day" } }
    ],
    "Enrich": [ "FromLogContext", "WithMachineName", "WithThreadId" ]
  }
}
```

Environment variables for `Azure.Identity` (only if using a Service Principal):

- `AZURE_TENANT_ID`
- `AZURE_CLIENT_ID`
- `AZURE_CLIENT_SECRET`

If running in Azure with Managed Identity, no secrets are required.


## API and Swagger

- API docs/UI: `/swagger`
- A minimal `swagger.json` is generated at build and copied to output/publish. The UI reads that file so API docs are available even without a live generator.


## Project structure (high-level)

- `Pages/` — Razor Pages UI (`.cshtml` and PageModels)
- `wwwroot/` — static assets and `swagger` folder
- `Program.cs` — application startup
- `appsettings*.json` — configuration
- `OfficeVersionsCore.csproj` — project definition and Swagger generation target


## Development tips

- Use `ASPNETCORE_ENVIRONMENT=Development` for local work
- Prefer environment variables or user secrets for credentials; avoid committing secrets
- Logs are written to console during development; file sink can be enabled via Serilog config


## Deployment

- Any Azure Web App / container host that supports .NET 8
- Ensure required configuration is provided via environment variables or Azure App Settings
- For Azure Storage access, prefer Managed Identity when possible
- Application Insights connection string can be set as an app setting


## Contributing

- Fork the repo
- Create a feature branch from `master`
- Commit with clear messages
- Open a pull request describing the change and testing steps

Issues and feature requests are tracked here: https://github.com/robgrame/OfficeVersionsCore/issues


## Support and contact

- General/support: info@office365versions.com
- Privacy: privacy@officeversions.com
- Twitter/X: https://twitter.com/office365ver
- LinkedIn: https://www.linkedin.com/company/office365versions

Security disclosures: please do not file public issues for sensitive reports. Contact the team via email.


## License

This project is licensed under the terms described in the `LICENSE` file in this repository.