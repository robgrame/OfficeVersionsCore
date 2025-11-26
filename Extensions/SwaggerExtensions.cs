using System.Reflection;
using Microsoft.OpenApi;

namespace OfficeVersionsCore
{
    /// <summary>
    /// Extension methods for configuring Swagger/OpenAPI
    /// </summary>
    public static class SwaggerExtensions
    {
        /// <summary>
        /// Configures Swagger/OpenAPI with standard settings for this application
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection ConfigureSwagger(this IServiceCollection services)
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Office Versions API",
                    Version = "v1",
                    Description = "API for Office 365 version tracking",
                    Contact = new OpenApiContact
                    {
                        Name = "Office Versions Core",
                        Url = new Uri("https://github.com/robgrame/OfficeVersionsCore")
                    }
                });

                // Include XML comments if available
                try
                {
                    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                    if (File.Exists(xmlPath))
                    {
                        c.IncludeXmlComments(xmlPath);
                    }
                }
                catch
                {
                    // Ignore XML comment errors
                }

                // Set operation IDs based on controller and action name
                c.CustomOperationIds(apiDesc =>
                {
                    return apiDesc.ActionDescriptor.RouteValues.TryGetValue("action", out var actionName) 
                        ? actionName 
                        : null;
                });

                // Enable annotations
                c.EnableAnnotations();
            });

            return services;
        }

        /// <summary>
        /// Configures the Swagger middleware
        /// </summary>
        /// <param name="app">The application builder</param>
        /// <returns>The application builder for chaining</returns>
        public static IApplicationBuilder UseConfiguredSwagger(this IApplicationBuilder app)
        {
            app.UseSwagger(c =>
            {
                c.RouteTemplate = "swagger/{documentName}/swagger.json";
            });

            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Office Versions API v1");
                c.RoutePrefix = "swagger";
                c.DocumentTitle = "Office Versions API";
                c.EnableDeepLinking();
                c.DisplayRequestDuration();
            });

            return app;
        }
    }
}