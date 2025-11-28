using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Reflection;
using System.Runtime.InteropServices;

namespace OfficeVersionsCore.Pages
{
    /// <summary>
    /// Page model for displaying application version information
    /// </summary>
    public class VersionModel : PageModel
    {
        private readonly IWebHostEnvironment _env;

        public VersionModel(IWebHostEnvironment env)
        {
            _env = env;
        }

        /// <summary>
        /// Assembly version (Major.Minor.Build.Revision)
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Informational version (includes git commit hash)
        /// </summary>
        public string InformationalVersion { get; set; } = string.Empty;

        /// <summary>
        /// Assembly file version
        /// </summary>
        public string FileVersion { get; set; } = string.Empty;

        /// <summary>
        /// Product name
        /// </summary>
        public string Product { get; set; } = string.Empty;

        /// <summary>
        /// Copyright information
        /// </summary>
        public string Copyright { get; set; } = string.Empty;

        /// <summary>
        /// Product description
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Company name
        /// </summary>
        public string Company { get; set; } = string.Empty;

        /// <summary>
        /// Build configuration (Debug/Release)
        /// </summary>
        public string Configuration { get; set; } = string.Empty;

        /// <summary>
        /// Target framework
        /// </summary>
        public string TargetFramework { get; set; } = string.Empty;

        /// <summary>
        /// Source revision/commit hash
        /// </summary>
        public string SourceRevision { get; set; } = string.Empty;

        /// <summary>
        /// Build date (from file modification time)
        /// </summary>
        public DateTime BuildDate { get; set; }

        /// <summary>
        /// Current environment name (Development/Production)
        /// </summary>
        public string EnvironmentName { get; set; } = string.Empty;

        /// <summary>
        /// Machine name
        /// </summary>
        public string MachineName { get; set; } = string.Empty;

        /// <summary>
        /// Operating system version
        /// </summary>
        public string OSVersion { get; set; } = string.Empty;

        /// <summary>
        /// .NET Runtime version
        /// </summary>
        public string RuntimeVersion { get; set; } = string.Empty;

        public void OnGet()
        {
            // Use GetEntryAssembly() which is more reliable for getting version info in .NET 10
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var assemblyName = assembly.GetName();

            // Version information
            Version = assemblyName.Version?.ToString() ?? "Unknown";
            
            var infoVersionAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            InformationalVersion = infoVersionAttr?.InformationalVersion ?? Version;
            
            var fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            FileVersion = fileVersionAttr?.Version ?? Version;

            // Extract source revision from informational version (after the '+')
            if (InformationalVersion.Contains('+'))
            {
                SourceRevision = InformationalVersion.Split('+')[1];
            }
            else
            {
                SourceRevision = "local";
            }

            // Product information
            var productAttr = assembly.GetCustomAttribute<AssemblyProductAttribute>();
            Product = productAttr?.Product ?? "OfficeVersionsCore";

            var copyrightAttr = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>();
            Copyright = copyrightAttr?.Copyright ?? string.Empty;

            var descriptionAttr = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>();
            Description = descriptionAttr?.Description ?? string.Empty;

            var companyAttr = assembly.GetCustomAttribute<AssemblyCompanyAttribute>();
            Company = companyAttr?.Company ?? string.Empty;

            // Build configuration
#if DEBUG
            Configuration = "Debug";
#else
            Configuration = "Release";
#endif

            // Target framework
            var targetFrameworkAttr = assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>();
            TargetFramework = targetFrameworkAttr?.FrameworkName ?? RuntimeInformation.FrameworkDescription;

            // Build date (from assembly file creation time)
            try
            {
                var assemblyLocation = assembly.Location;
                if (!string.IsNullOrEmpty(assemblyLocation) && System.IO.File.Exists(assemblyLocation))
                {
                    BuildDate = System.IO.File.GetLastWriteTime(assemblyLocation);
                }
                else
                {
                    BuildDate = DateTime.Now;
                }
            }
            catch
            {
                BuildDate = DateTime.Now;
            }

            // System information
            EnvironmentName = _env.EnvironmentName;
            MachineName = Environment.MachineName;
            OSVersion = RuntimeInformation.OSDescription;
            RuntimeVersion = RuntimeInformation.FrameworkDescription;
        }
    }
}
