using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReportGenerator
{
    public static class ReportGeneratorOrchestrators
    {
        [FunctionName(nameof(ProcessDataFile))]
        public static async Task<object> ProcessDataFile(
            [OrchestrationTrigger] IDurableOrchestrationContext ctx,
            ILogger log)
        {
            log = ctx.CreateReplaySafeLogger(log);
            var dataFile = ctx.GetInput<string>();

            var students = await ctx.CallActivityAsync<IEnumerable<Student>>(nameof(ReportGeneratorActivities.ImportDataFile), dataFile);

            var start = ctx.CurrentUtcDateTime;

            try
            {
                var studentTasks = students
                    .Select(s => ctx.CallSubOrchestratorAsync<object>(nameof(GenerateAndArchiveReport), s));

                await Task.WhenAll(studentTasks);

                var end = ctx.CurrentUtcDateTime;

                return new
                {
                    Count = students.Count(),
                    Duration = (end - start).TotalSeconds,
                };
            }
            catch (Exception e)
            {
                var end = ctx.CurrentUtcDateTime;

                return new
                {
                    Error = "Failed to process datafile: " + e.Message,
                    Duration = (end - start).TotalSeconds,
                };
            }
        }

        [FunctionName(nameof(GenerateAndArchiveReport))]
        public static async Task<object> GenerateAndArchiveReport(
            [OrchestrationTrigger] IDurableOrchestrationContext ctx,
            ILogger log)
        {
            log = ctx.CreateReplaySafeLogger(log);

            var student = ctx.GetInput<Student>();

            log.LogInformation("Processing student {student}", student.Name);

            try
            {
                var reportData = await ctx.CallActivityAsync<string>(nameof(ReportGeneratorActivities.GenerateReport), student);
                var result = await ctx.CallActivityAsync<string>(nameof(ReportGeneratorActivities.ArchiveReport), new ArchiveReportCommand
                {
                    Base64Data = reportData,
                    Filename = student.Name + ".pdf",
                    Mimetype = "application/pdf",
                });

                log.LogInformation("Done processing student {student}", student.Name);

                return new { }; // We need this on success?
            }
            catch (Exception e)
            {
                return new
                {
                    Error = $"Failed to generate and archive report for student {student.Name}: " + e.Message,
                };
            }
        }


        [FunctionName(nameof(HttpStart))]
        public static async Task<IActionResult> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req,
            [DurableClient] IDurableOrchestrationClient starter,
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

            var orchestrationId = await starter.StartNewAsync(nameof(ProcessDataFile), null, dataFile);

            var payload = starter.CreateHttpManagementPayload(orchestrationId);

            return new OkObjectResult(payload);
        }
    }
}