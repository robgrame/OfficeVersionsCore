namespace OfficeVersionsCore.Services
{
    /// <summary>
    /// Local file system storage service
    /// </summary>
    public class LocalStorageService : IStorageService
    {
        private readonly string _basePath;
        private readonly ILogger<LocalStorageService> _logger;

        public LocalStorageService(IWebHostEnvironment env, ILogger<LocalStorageService> logger)
        {
            _basePath = Path.Combine(env.WebRootPath, "content");
            _logger = logger;

            // Ensure base directory exists
            if (!Directory.Exists(_basePath))
            {
                Directory.CreateDirectory(_basePath);
                _logger.LogInformation("Created base storage path: {BasePath}", _basePath);
            }
        }

        public async Task<string> ReadAsync(string fileName)
        {
            try
            {
                var filePath = GetFullPath(fileName);

                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("File not found: {FileName}", fileName);
                    throw new FileNotFoundException($"File not found: {fileName}");
                }

                var content = await File.ReadAllTextAsync(filePath);
                _logger.LogDebug("Read file successfully: {FileName}", fileName);
                return content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading file: {FileName}", fileName);
                throw;
            }
        }

        public async Task WriteAsync(string fileName, string content)
        {
            try
            {
                var filePath = GetFullPath(fileName);
                var directory = Path.GetDirectoryName(filePath);

                // Ensure directory exists
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.LogInformation("Created directory: {Directory}", directory);
                }

                await File.WriteAllTextAsync(filePath, content);
                _logger.LogDebug("Wrote file successfully: {FileName}", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing file: {FileName}", fileName);
                throw;
            }
        }

        public Task<bool> ExistsAsync(string fileName)
        {
            try
            {
                var filePath = GetFullPath(fileName);
                var exists = File.Exists(filePath);
                
                if (exists)
                {
                    _logger.LogDebug("File exists: {FileName}", fileName);
                }
                else
                {
                    _logger.LogDebug("File does not exist: {FileName}", fileName);
                }

                return Task.FromResult(exists);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking file existence: {FileName}", fileName);
                throw;
            }
        }

        public Task DeleteAsync(string fileName)
        {
            try
            {
                var filePath = GetFullPath(fileName);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogInformation("Deleted file: {FileName}", fileName);
                }
                else
                {
                    _logger.LogWarning("File not found for deletion: {FileName}", fileName);
                }

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting file: {FileName}", fileName);
                throw;
            }
        }

        public Task<DateTime?> GetLastModifiedAsync(string fileName)
        {
            try
            {
                var filePath = GetFullPath(fileName);

                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("File not found for getting modification date: {FileName}", fileName);
                    return Task.FromResult<DateTime?>(null);
                }

                var fileInfo = new FileInfo(filePath);
                var lastModified = fileInfo.LastWriteTimeUtc;

                _logger.LogDebug("Got last modified time for {FileName}: {LastModified}", fileName, lastModified);
                return Task.FromResult<DateTime?>(lastModified);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting file modification date: {FileName}", fileName);
                throw;
            }
        }

        /// <summary>
        /// Gets the full file path, ensuring it stays within the base directory
        /// </summary>
        private string GetFullPath(string fileName)
        {
            // Security: prevent directory traversal
            var safeName = Path.GetFileName(Path.GetDirectoryName(fileName) ?? string.Empty);
            var name = Path.GetFileName(fileName);

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Invalid file name", nameof(fileName));
            }

            var fullPath = string.IsNullOrEmpty(safeName)
                ? Path.Combine(_basePath, name)
                : Path.Combine(_basePath, safeName, name);

            // Ensure the path is within the base directory
            var fullBasePath = Path.GetFullPath(_basePath);
            var resolvedPath = Path.GetFullPath(fullPath);

            if (!resolvedPath.StartsWith(fullBasePath))
            {
                throw new ArgumentException("Path traversal attempt detected", nameof(fileName));
            }

            return resolvedPath;
        }
    }
}
