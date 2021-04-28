﻿using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Docati.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ReportGenerator
{
    [StorageAccount("ReportStore")]
    public class ReportGeneratorActivities
    {
        private readonly ReportGeneratorConfig _reportGenConfig;
        private readonly DocBuilder _docBuilder;
        private readonly HttpClient _httpClient;
        private readonly string _reportStoreConnString;

        public ReportGeneratorActivities(IOptions<ReportGeneratorConfig> reportGenConfig, DocBuilder docBuilder, HttpClient httpClient)
        {
            this._reportGenConfig = reportGenConfig.Value ?? throw new ArgumentNullException(nameof(reportGenConfig));
            this._docBuilder = docBuilder ?? throw new ArgumentNullException(nameof(docBuilder));
            this._httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            _reportStoreConnString = Environment.GetEnvironmentVariable("ReportStore");
        }

        /// <summary>
        /// Http-trigger to pass path to file on url (webbased file or local file)
        /// </summary>
        /// <param name="req"></param>
        /// <param name="client"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName(nameof(HttpStart))]
        public async Task<IActionResult> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req,
            [DurableClient] IDurableOrchestrationClient client,
            ILogger log)
        {
            // parse query parameter
            string dataFile = req.GetQueryParameterDictionary()["data"];

            if (dataFile == null)
            {
                return new BadRequestObjectResult(
                   "Please pass the datafile location on the query string");
            }

            log.LogInformation($"About to start orchestration for {dataFile}");

            string fileData;
            if (dataFile.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var resp = await _httpClient.GetAsync(dataFile);
                resp.EnsureSuccessStatusCode();
                fileData = await resp.Content.ReadAsStringAsync();
            }
            else
            {
                if (dataFile.Contains("..")) throw new InvalidOperationException("Path traversal not allowed");
                fileData = File.ReadAllText(dataFile);
            }

            var students = JsonConvert.DeserializeObject<IEnumerable<Student>>(fileData);
            var orchestrationId = await client.StartNewAsync(nameof(ReportGeneratorOrchestrators.GenerateAndArchiveReports), null, students);

            var payload = client.CreateHttpManagementPayload(orchestrationId);
            return new OkObjectResult(payload);
        }

        /// <summary>
        /// Blob-trigger to pass file through blob-upload to container 'input' on bound storage account
        /// </summary>
        /// <param name="input"></param>
        /// <param name="name"></param>
        /// <param name="client"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName(nameof(BlobTriggered))]
        public async Task BlobTriggered(
            [BlobTrigger("input/{name}")] Stream input,
            string name,
            [DurableClient] IDurableOrchestrationClient client,
            ILogger log)
        {
            try
            {
                if (input != null && name != null)
                {
                    log.LogInformation("Parsing blob {name}", name);
                    var extension = Path.GetExtension(name);

                    // support both json/xlsx
                    if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        using var inputData = new MemoryStream();
                        await input.CopyToAsync(inputData);
                        var fileData = Encoding.UTF8.GetString(inputData.ToArray());
                        var students = JsonConvert.DeserializeObject<IEnumerable<Student>>(fileData);

                        await client.StartNewAsync(nameof(ReportGeneratorOrchestrators.GenerateAndArchiveReports), null, students);
                    }
                    else
                    {
                        log.LogWarning($"Unsupported file extension for: {name}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to process BlobTrigger");
                throw;
            }
        }

        /// <summary>
        /// EventGrid-trigger to pass file through blob-upload, but using the blobstorage change-feed through EventGrid
        /// </summary>
        /// <param name="eventGridEvent"></param>
        /// <param name="input"></param>
        /// <param name="client"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName(nameof(EventGridTriggered))]
        public async Task EventGridTriggered(
            [EventGridTrigger] EventGridEvent eventGridEvent,
            [Blob("{data.url}", FileAccess.Read)] Stream input,
            [DurableClient] IDurableOrchestrationClient client,
            ILogger log)
        {
            try
            {
                if (input != null)
                {
                    // Check metadate for the blob (part of the eventgrid event-payload)
                    var createdEvent = ((JObject)eventGridEvent.Data).ToObject<StorageBlobCreatedEventData>();
                    var extension = Path.GetExtension(createdEvent.Url);

                    // support both json/xlsx
                    if (extension.Equals("json", StringComparison.OrdinalIgnoreCase))
                    {
                        using var inputData = new MemoryStream();
                        await input.CopyToAsync(inputData);
                        var fileData = Encoding.UTF8.GetString(inputData.ToArray());
                        var students = JsonConvert.DeserializeObject<IEnumerable<Student>>(fileData);

                        await client.StartNewAsync(nameof(ReportGeneratorOrchestrators.GenerateAndArchiveReports), null, students);
                    }
                    else
                    {
                        log.LogWarning($"Unsupported file extension for: {createdEvent.Url}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to process EventGrid blob trigger");
                throw;
            }
        }

        /// <summary>
        /// Function that generates and archives test reports (PDF) for all students.
        /// </summary>
        /// <remarks>This single activity/function prevents storing the PDF-contents as activity output in storage (Durable Functions).</remarks>
        /// <param name="student"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName(nameof(GenerateAndArchiveReport))]
        public async Task GenerateAndArchiveReport(
            [ActivityTrigger] Student student,
            ILogger log)
        {
            log.LogInformation("Processing student {student}", student.Name);

            // Generate the report
            var reportData = await GenerateReport(student, log);

            // Archive the report
            _ = await ArchiveReport(new ArchiveReportCommand
            {
                Base64Data = reportData,
                Filename = student.Id + ".pdf",
                Mimetype = "application/pdf",
            }, new BlobContainerClient(_reportStoreConnString, "output"), log);

            log.LogInformation("Done processing student {student}", student.Name);
        }

        /// <summary>
        /// Generate the report PDF using Docati.Api
        /// </summary>
        /// <param name="student"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName(nameof(GenerateReport))]
        public Task<string> GenerateReport(
        [ActivityTrigger] Student student,
        ILogger log)
        {
            log.LogInformation($"Generating report for: {student.Name}");
            var sw = Stopwatch.StartNew();

            try
            {

                var studentAsString = JsonConvert.SerializeObject(student);
                using var dataStream = new MemoryStream(Encoding.UTF8.GetBytes(studentAsString));

                using var outputStream = new MemoryStream(60000); // Initial capacity to prevent many allocations
                _docBuilder.Build(dataStream, DataFormat.Json, outputStream, null, DocumentFileFormat.PDF);
                var result = Convert.ToBase64String(outputStream.ToArray());

                sw.Stop();
                log.LogInformation($"Report generated for {student.Name} in {sw.ElapsedMilliseconds} ms");

                return Task.FromResult(result);
            }
            catch (Exception e)
            {
                log.LogError(e, "Failed to generate report");
                throw;
            }
        }

        /// <summary>
        /// Archive the report PDF to container 'output' on bound storage account
        /// </summary>
        /// <param name="command"></param>
        /// <param name="blobContainerClient"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName(nameof(ArchiveReport))]
        public async Task<string> ArchiveReport(
                    [ActivityTrigger] ArchiveReportCommand command,
                    [Blob("output", FileAccess.Read)] BlobContainerClient blobContainerClient,
                    ILogger log)
        {
            log.LogInformation($"Archive report for: {command.Filename}");
            var sw = Stopwatch.StartNew();

            try
            {
                await blobContainerClient.CreateIfNotExistsAsync();
                var blobClient = blobContainerClient.GetBlobClient(command.Filename);
                using var blobDataStream = new MemoryStream(Convert.FromBase64String(command.Base64Data));
                var blobInfo = await blobClient.UploadAsync(blobDataStream, overwrite: true);
                await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = command.Mimetype });

                sw.Stop();
                log.LogInformation($"Report archived for {command.Filename} in {sw.ElapsedMilliseconds} ms");

                return blobInfo.Value.VersionId;
            }
            catch (Exception e)
            {
                log.LogError(e, "Failed to archive report");
                throw;
            }
        }
    }
}
