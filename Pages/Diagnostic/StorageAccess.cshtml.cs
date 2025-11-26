using OfficeVersionsCore.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace OfficeVersionsCore.Pages.Diagnostic
{
    public class StorageAccessModel : PageModel
    {
        private readonly IStorageService _storageService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StorageAccessModel> _logger;

        public List<string> FileNames { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public string StoragePath { get; set; } = string.Empty;
        public string OfficeVersionsPath { get; set; } = string.Empty;
        public string WindowsVersionsPath { get; set; } = string.Empty;

        public StorageAccessModel(
            IStorageService storageService,
            IConfiguration configuration,
            ILogger<StorageAccessModel> logger)
        {
            _storageService = storageService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
            _logger.LogInformation("Testing local storage access");
            
            OfficeVersionsPath = _configuration["Office365:StoragePath"] ?? "officeversions";
            WindowsVersionsPath = _configuration["WindowsVersions:StoragePath"] ?? "windowsversions";
            StoragePath = "wwwroot/content/";

            try
            {
                // List files in both storage directories
                var officeFiles = await ListStorageFilesAsync($"{OfficeVersionsPath}");
                var windowsFiles = await ListStorageFilesAsync($"{WindowsVersionsPath}");
                
                FileNames.AddRange(officeFiles.Select(f => $"Office/{f}"));
                FileNames.AddRange(windowsFiles.Select(f => $"Windows/{f}"));
                
                if (FileNames.Count > 0)
                {
                    _logger.LogInformation("Found {Count} files in storage directories", FileNames.Count);
                }
                else
                {
                    _logger.LogWarning("No files found in storage directories");
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error accessing local storage: {ex.Message}";
                _logger.LogError(ex, "Error testing storage access");
            }
        }
        
        private async Task<List<string>> ListStorageFilesAsync(string directoryPath)
        {
            var files = new List<string>();
            
            try
            {
                // Try to list files by checking for known file patterns
                var knownFiles = new[]
                {
                    "m365LatestVersions.json",
                    "m365CurrentReleases.json",
                    "m365MonthlyReleases.json",
                    "m365SACReleases.json",
                    "m365SACPreviewReleases.json",
                    "m365releases.json",
                    "windows10-versions.json",
                    "windows10-updates.json",
                    "windows10-feature-updates.json",
                    "windows11-versions.json",
                    "windows11-updates.json",
                    "windows11-feature-updates.json",
                    "last-update.json"
                };
                
                foreach (var fileName in knownFiles)
                {
                    var fullPath = $"{directoryPath}/{fileName}";
                    if (await _storageService.ExistsAsync(fullPath))
                    {
                        files.Add(fileName);
                    }
                }
                
                _logger.LogInformation("Found {Count} files in path: {Path}", files.Count, directoryPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing files in path: {Path}", directoryPath);
            }
            
            return files;
        }
    }
}