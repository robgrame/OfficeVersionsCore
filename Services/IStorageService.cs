namespace OfficeVersionsCore.Services
{
    /// <summary>
    /// Interface for abstracting storage operations (local or cloud)
    /// </summary>
    public interface IStorageService
    {
        /// <summary>
        /// Reads content from storage
        /// </summary>
        /// <param name="fileName">File name to read</param>
        /// <returns>File content as string</returns>
        Task<string> ReadAsync(string fileName);

        /// <summary>
        /// Writes content to storage
        /// </summary>
        /// <param name="fileName">File name to write</param>
        /// <param name="content">Content to write</param>
        Task WriteAsync(string fileName, string content);

        /// <summary>
        /// Checks if a file exists in storage
        /// </summary>
        /// <param name="fileName">File name to check</param>
        /// <returns>True if file exists</returns>
        Task<bool> ExistsAsync(string fileName);

        /// <summary>
        /// Deletes a file from storage
        /// </summary>
        /// <param name="fileName">File name to delete</param>
        Task DeleteAsync(string fileName);

        /// <summary>
        /// Gets the last modified time of a file
        /// </summary>
        /// <param name="fileName">File name</param>
        /// <returns>DateTime of last modification</returns>
        Task<DateTime?> GetLastModifiedAsync(string fileName);
    }
}
