using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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
        private readonly HttpClient _httpClient;

        public ReportGeneratorActivities(ReportGeneratorConfig reportGenConfig, DocBuilder docBuilder, HttpClient httpClient)
        {
            this._reportGenConfig = reportGenConfig ?? throw new ArgumentNullException(nameof(reportGenConfig));

            // Correct outputfolder
            var outputFolder = _reportGenConfig.OutputFolder ?? "./";
            if (!outputFolder.EndsWith('/')) outputFolder += '/';
            this._reportGenConfig.OutputFolder = outputFolder;

            this._docBuilder = docBuilder ?? throw new ArgumentNullException(nameof(docBuilder));
            this._httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        [FunctionName(nameof(ImportDataFile))]
        public async Task<IEnumerable<Student>> ImportDataFile(
            [ActivityTrigger] string dataFile,
            ILogger log)
        {
            log.LogInformation($"Importing {dataFile}");

            string fileData = await LoadFile(dataFile);

            var students = JsonConvert.DeserializeObject<IEnumerable<Student>>(fileData);
            return students;            
        }

        private async Task<string> LoadFile(string dataFile)
        {
            if (dataFile.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var resp = await _httpClient.GetAsync(dataFile);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync();
            }
            else
            {
                if (dataFile.Contains("..")) throw new InvalidOperationException("Path traversal not allowed");
                return File.ReadAllText(dataFile);
            }
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
