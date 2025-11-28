using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using System.Runtime.InteropServices;

namespace OfficeVersionsCore.Controllers
{
    /// <summary>
    /// API controller for application version information
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class VersionController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<VersionController> _logger;

        public VersionController(IWebHostEnvironment env, ILogger<VersionController> logger)
        {
            _env = env;
            _logger = logger;
        }

        /// <summary>
        /// Get application version information
        /// </summary>
        /// <returns>Version information object</returns>
        /// <response code="200">Returns version information</response>
        [HttpGet]
        [ProducesResponseType(typeof(VersionInfo), StatusCodes.Status200OK)]
        public ActionResult<VersionInfo> GetVersion()
        {
            _logger.LogInformation("Version information requested");

            var assembly = Assembly.GetExecutingAssembly();
            var assemblyName = assembly.GetName();

            var version = assemblyName.Version?.ToString() ?? "Unknown";
            
            var infoVersionAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var informationalVersion = infoVersionAttr?.InformationalVersion ?? version;

            var fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            var fileVersion = fileVersionAttr?.Version ?? version;

            // Extract source revision
            string sourceRevision = "local";
            if (informationalVersion.Contains('+'))
            {
                sourceRevision = informationalVersion.Split('+')[1];
            }

            var productAttr = assembly.GetCustomAttribute<AssemblyProductAttribute>();
            var product = productAttr?.Product ?? "OfficeVersionsCore";

            var copyrightAttr = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>();
            var copyright = copyrightAttr?.Copyright ?? string.Empty;

            var descriptionAttr = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>();
            var description = descriptionAttr?.Description ?? string.Empty;

            var companyAttr = assembly.GetCustomAttribute<AssemblyCompanyAttribute>();
            var company = companyAttr?.Company ?? string.Empty;

#if DEBUG
            var configuration = "Debug";
#else
            var configuration = "Release";
#endif

            var targetFrameworkAttr = assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>();
            var targetFramework = targetFrameworkAttr?.FrameworkName ?? RuntimeInformation.FrameworkDescription;

            DateTime buildDate;
            try
            {
                var assemblyLocation = assembly.Location;
                if (!string.IsNullOrEmpty(assemblyLocation) && System.IO.File.Exists(assemblyLocation))
                {
                    buildDate = System.IO.File.GetLastWriteTime(assemblyLocation);
                }
                else
                {
                    buildDate = DateTime.Now;
                }
            }
            catch
            {
                buildDate = DateTime.Now;
            }

            var versionInfo = new VersionInfo
            {
                Version = version,
                InformationalVersion = informationalVersion,
                FileVersion = fileVersion,
                SourceRevision = sourceRevision,
                Product = product,
                Copyright = copyright,
                Description = description,
                Company = company,
                Configuration = configuration,
                TargetFramework = targetFramework,
                BuildDate = buildDate,
                Environment = _env.EnvironmentName,
                MachineName = Environment.MachineName,
                OSVersion = RuntimeInformation.OSDescription,
                RuntimeVersion = RuntimeInformation.FrameworkDescription
            };

            return Ok(versionInfo);
        }

        /// <summary>
        /// Get only the version number
        /// </summary>
        /// <returns>Simple version string</returns>
        /// <response code="200">Returns version string</response>
        [HttpGet("number")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        public ActionResult<string> GetVersionNumber()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version?.ToString() ?? "Unknown";
            return Ok(version);
        }

        /// <summary>
        /// Get informational version (includes commit hash)
        /// </summary>
        /// <returns>Informational version string</returns>
        /// <response code="200">Returns informational version</response>
        [HttpGet("info")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        public ActionResult<string> GetInformationalVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var infoVersionAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var informationalVersion = infoVersionAttr?.InformationalVersion ?? "Unknown";
            return Ok(informationalVersion);
        }
    }

    /// <summary>
    /// Version information model
    /// </summary>
    public class VersionInfo
    {
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
        /// Source revision/commit hash
        /// </summary>
        public string SourceRevision { get; set; } = string.Empty;

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
        /// Build date
        /// </summary>
        public DateTime BuildDate { get; set; }

        /// <summary>
        /// Current environment name (Development/Production)
        /// </summary>
        public string Environment { get; set; } = string.Empty;

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
    }
}
