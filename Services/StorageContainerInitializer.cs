using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OfficeVersionsCore.Services
{
    /// <summary>
    /// Service that runs at startup to ensure required storage containers exist
    /// </summary>
    public class StorageContainerInitializer : IHostedService
    {
        private readonly ILogger<StorageContainerInitializer> _logger;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public StorageContainerInitializer(
            ILogger<StorageContainerInitializer> logger,
            BlobServiceClient blobServiceClient,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            _logger = logger;
            _blobServiceClient = blobServiceClient;
            _configuration = configuration;
            _environment = environment;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Only initialize containers for Azurite in development
            bool useAzurite = _environment.IsDevelopment() && 
                             _configuration.GetValue<bool>("AzuriteStorage:UseAzurite", false);
            
            if (!useAzurite)
            {
                _logger.LogInformation("Skipping container initialization (not using Azurite)");
                return;
            }
            
            _logger.LogInformation("Initializing storage containers for Azurite");
            
            try
            {
                string containerName = _configuration["SEC_STOR_StorageCon"] ?? "jsonrepository";
                
                // Create the container if it doesn't exist
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                
                _logger.LogInformation("Storage container '{ContainerName}' initialized", containerName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing storage containers");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}