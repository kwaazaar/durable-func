using Docati.Api;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(ReportGenerator.Startup))]

namespace ReportGenerator
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var reportGenConfig = builder.GetContext().Configuration.GetSection("ReportGenerator")?.Get<ReportGeneratorConfig>() ?? new ReportGeneratorConfig();
            builder.Services.AddSingleton(reportGenConfig);
            
            builder.Services.AddSingleton((s) =>
            {

                // In this sample we will use the free license, which is limited to 20 paragraphs and low performance.
                // If you want to evaluate Docati without these limitations, please don't hesitate to contact us
                // at support@docati.com and request a trial license.
                License.ApplyLicense("free"); // Check https://www.docati.com/pricing for more licensing details

                // Template is an embedded resource
                var resourceProvider = new EmbeddedResourceProvider(typeof(ReportGeneratorActivities).Assembly);

                // Singleton DocBuilder (thread-safe) which is reused for every document
                var builder = new DocBuilder(reportGenConfig?.Template ?? "Template.docx", resourceProvider);
                return builder;
            });
        }
    }
}