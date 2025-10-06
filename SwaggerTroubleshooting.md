# Swagger Generation Troubleshooting Guide

This document explains how to solve common issues with Swagger generation for the Office Versions Core API.

## Common Issues and Solutions

### 1. System.Runtime Version Compatibility Error

**Error:**
```
Unhandled exception. System.IO.FileNotFoundException: Could not load file or assembly 'System.Runtime, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a'. The system cannot find the file specified.
```

**Solution:**
This error occurs when the Swashbuckle CLI tool version (9.0.5) is looking for System.Runtime version 9.0.0.0 (for .NET 9), but your project targets .NET 8. 

We've updated the scripts to use Swashbuckle version 6.5.0, which is compatible with .NET 8.

If the error persists, we provide an alternative approach that doesn't rely on the Swashbuckle CLI tool.

### 2. Alternative Swagger Generation Approach

If you continue to experience issues with the Swashbuckle CLI tool:

1. Use our alternative PowerShell-based Swagger generator:

```cmd
GenerateSwaggerAlternative.bat
```

This approach:
- Creates a comprehensive Swagger JSON file directly using PowerShell
- Doesn't rely on the Swashbuckle CLI tool
- Includes all known API endpoints and schemas
- Places the file in the correct location for your application

### 3. Swashbuckle CLI Tool Installation (Standard Approach)

To ensure you have the correct version installed:

1. Run the `Install-SwaggerCLI.ps1` script, which will:
   - Check for any existing Swashbuckle CLI tool installation
   - Uninstall incompatible versions (like 9.0.5)
   - Install version 6.5.0 which is compatible with .NET 8

```powershell
.\Install-SwaggerCLI.ps1
```

### 4. Generating Swagger Documentation (Standard Approach)

After installing the correct CLI tool version:

1. Run the `Generate-Swagger.ps1` script or `GenerateSwagger.bat`:

```powershell
.\Generate-Swagger.ps1
```

or

```cmd
GenerateSwagger.bat
```

The script includes fallback mechanisms to ensure a basic Swagger file is created even if the CLI tool fails.

### 5. Manual Troubleshooting Steps

If issues persist:

1. **Check .NET SDK version**:
   ```
   dotnet --info
   ```
   Ensure you're using .NET 8.

2. **Verify Swashbuckle CLI tool version**:
   ```
   dotnet tool list --global
   ```
   Look for `swashbuckle.aspnetcore.cli` with version `6.5.0`.

3. **Verify project Swashbuckle package versions**:
   Check that the project file references Swashbuckle version 6.5.0 packages.

4. **Use the alternative approach**:
   Run `GenerateSwaggerAlternative.bat` as described above.

## Additional Information

- Swashbuckle version 6.5.0 is the latest stable release that fully supports .NET 8
- Swashbuckle version 9.0.0+ targets .NET 9 and isn't backwards compatible with .NET 8
- Always ensure your Swashbuckle CLI tool version matches your project's Swashbuckle package version
- The alternative approach provides a reliable way to generate Swagger documentation without the CLI tool

If you need additional help, please refer to the [Swashbuckle documentation](https://github.com/domaindrivendev/Swashbuckle.AspNetCore) or create an issue in the project repository.