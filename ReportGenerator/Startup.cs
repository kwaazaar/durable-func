using Docati.Api;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.IO;

[assembly: FunctionsStartup(typeof(ReportGenerator.Startup))]

namespace ReportGenerator
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var config = new ConfigurationBuilder()
                               .SetBasePath(Directory.GetCurrentDirectory()) // Environment.CurrentDirectory does not work on Azure
                               .AddJsonFile("appsettings.json", true)
                               //.AddUserSecrets(Assembly.GetExecutingAssembly(), false)
                               .AddEnvironmentVariables()
                               .Build();

            builder.Services.Configure<ReportGeneratorConfig>(config.GetSection("ReportGenerator"));
            builder.Services.AddOptions();

            builder.Services.AddSingleton((s) =>
            {
                // In this sample we will use the free license, which is limited to 20 paragraphs and low performance(2+ secs per doc).
                // If you want to evaluate Docati without these limitations, please don't hesitate to contact us
                // at support@docati.com and request a trial license.

                // Check https://www.docati.com/pricing for more licensing details
                var license = Environment.GetEnvironmentVariable("DocatiLicense") ?? "free";
                License.ApplyLicense(license);

                // Template is an embedded resource
                var resourceProvider = new EmbeddedResourceProvider(typeof(ReportGeneratorActivities).Assembly);

                // Singleton DocBuilder (thread-safe) which is reused for every document
                var reportGenConfig = s.GetService<IOptions<ReportGeneratorConfig>>();
                var builder = new DocBuilder(reportGenConfig?.Value?.Template ?? "Template.docx", resourceProvider);
                return builder;
            });

            builder.Services.AddHttpClient<ReportGeneratorActivities>();
        }
    }
}