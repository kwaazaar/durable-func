using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Docati.Api;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ReportGenerator
{
    public class ReportGeneratorActivities
    {
        private readonly ReportGeneratorConfig _reportGenConfig;
        private readonly DocBuilder _docBuilder;

        public ReportGeneratorActivities(ReportGeneratorConfig reportGenConfig, DocBuilder docBuilder)
        {
            this._reportGenConfig = reportGenConfig ?? throw new ArgumentNullException(nameof(reportGenConfig));

            // Correct outputfolder
            var outputFolder = _reportGenConfig.OutputFolder ?? "./";
            if (!outputFolder.EndsWith('/')) outputFolder += '/';
            this._reportGenConfig.OutputFolder = outputFolder;

            this._docBuilder = docBuilder ?? throw new ArgumentNullException(nameof(docBuilder));
        }

        [FunctionName(nameof(ImportDataFile))]
        public async Task<IEnumerable<Student>> ImportDataFile(
            [ActivityTrigger] string dataFile,
            ILogger log)
        {
            log.LogInformation($"Importing {dataFile}");

            // simulate doing the activity
            await Task.Delay(1000);

            return new Student[]
            {
                new Student { Id = "A12876771", Name = "Robert", Score = 5.5m },
                new Student { Id = "F12177894", Name = "Richard", Score = 5.49m },
            };
        }

        [FunctionName(nameof(GenerateReport))]
        public Task<string> GenerateReport(
            [ActivityTrigger] Student student,
            ILogger log)
        {
            log.LogInformation($"Generating report for: {student.Name}");

            var studentAsString = JsonConvert.SerializeObject(student);

            using var outputStream = new MemoryStream();
            using var dataStream = new MemoryStream(Encoding.UTF8.GetBytes(studentAsString));
            _docBuilder.Build(dataStream, DataFormat.Json, outputStream, null, DocumentFileFormat.PDF);
            return Task.FromResult(Convert.ToBase64String(outputStream.ToArray()));
        }

        [FunctionName(nameof(ArchiveReport))]
        public async Task<object> ArchiveReport(
            [ActivityTrigger] ArchiveReportCommand command,
            ILogger log)
        {
            log.LogInformation($"Archive report for: {command.Filename}");

            try
            {
                var outputFile = Path.Combine(_reportGenConfig.OutputFolder, command.Filename);
                await File.WriteAllBytesAsync(outputFile, Convert.FromBase64String(command.Base64Data));
                return null;
            }
            catch (Exception e)
            {
                return new
                {
                    Error = "Failed to archive report: " + e.Message,
                };
            }
        }
    }
}
