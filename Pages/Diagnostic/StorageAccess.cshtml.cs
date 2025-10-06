using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OfficeVersionsCore.Pages.Diagnostic
{
    public class StorageAccessModel : PageModel
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StorageAccessModel> _logger;
        private readonly IWebHostEnvironment _environment;

        public List<string> BlobNames { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public string? ContainerName { get; set; }
        public bool ContainerExists { get; set; }
        public string StorageAccountName { get; set; } = string.Empty;
        public bool IsUsingAzurite { get; set; }
        public string BlobEndpoint { get; set; } = string.Empty;

        public StorageAccessModel(
            BlobServiceClient blobServiceClient,
            IConfiguration configuration,
            ILogger<StorageAccessModel> logger,
            IWebHostEnvironment environment)
        {
            _blobServiceClient = blobServiceClient;
            _configuration = configuration;
            _logger = logger;
            _environment = environment;
        }

        public async Task OnGetAsync()
        {
            // Determine if using Azurite
            IsUsingAzurite = _environment.IsDevelopment() && 
                             _configuration.GetValue<bool>("AzuriteStorage:UseAzurite", false);

            // Get storage account and container name from configuration
            StorageAccountName = _configuration["StorageAccountName"] ?? 
                                (IsUsingAzurite ? "devstoreaccount1" : "officeversionscorestrg");
            ContainerName = _configuration["SEC_STOR_StorageCon"] ?? "jsonrepository";
            BlobEndpoint = IsUsingAzurite ? 
                _configuration["AzuriteStorage:BlobEndpoint"] ?? "http://127.0.0.1:10000/devstoreaccount1" :
                $"https://{StorageAccountName}.blob.core.windows.net";

            try
            {
                if (IsUsingAzurite)
                {
                    _logger.LogInformation("Testing Azurite local storage access");
                }
                else
                {
                    _logger.LogInformation("Testing Azure Storage access with managed identity");
                }
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
                
                // For Azurite, try to create the container if it doesn't exist
                if (IsUsingAzurite)
                {
                    await containerClient.CreateIfNotExistsAsync();
                }
                
                ContainerExists = await containerClient.ExistsAsync();
                
                if (ContainerExists)
                {
                    _logger.LogInformation("Container {Container} exists in storage account {Account}", 
                        ContainerName, StorageAccountName);
                    
                    await foreach (var blob in containerClient.GetBlobsAsync())
                    {
                        BlobNames.Add(blob.Name);
                        _logger.LogInformation("Found blob: {BlobName}", blob.Name);
                    }
                    
                    // For Azurite, create a sample blob if none exist
                    if (BlobNames.Count == 0 && IsUsingAzurite)
                    {
                        _logger.LogInformation("No blobs found in Azurite container. Creating a sample blob...");
                        
                        var sampleBlobClient = containerClient.GetBlobClient("sample.json");
                        var sampleJson = "{ \"message\": \"This is a sample blob created by the diagnostic page\" }";
                        
                        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(sampleJson));
                        await sampleBlobClient.UploadAsync(ms, overwrite: true);
                        
                        _logger.LogInformation("Created sample blob 'sample.json'");
                        BlobNames.Add("sample.json");
                    }
                    else if (BlobNames.Count == 0)
                    {
                        _logger.LogWarning("Container exists but no blobs were found");
                    }
                }
                else
                {
                    ErrorMessage = $"Container '{ContainerName}' does not exist in storage account '{StorageAccountName}'";
                    _logger.LogWarning(ErrorMessage);
                    
                    // Try to create it for Azurite
                    if (IsUsingAzurite)
                    {
                        _logger.LogInformation("Attempting to create container '{ContainerName}' in Azurite", ContainerName);
                        await containerClient.CreateAsync();
                        ContainerExists = true;
                        ErrorMessage = null;
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error accessing storage: {ex.Message}";
                _logger.LogError(ex, "Error testing storage access");
            }
        }
    }
}