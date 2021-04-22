using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReportGenerator
{
    public static class ReportGeneratorOrchestrators
    {
        /// <summary>
        /// Generates and archives test reports (PDF) for all students
        /// </summary>
        /// <remarks>May take a lot of time for many students (and since Docati.Api unlicensed (free) has time penalty) </remarks>
        /// <param name="ctx"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName(nameof(GenerateAndArchiveReports))]
        public static async Task<object> GenerateAndArchiveReports(
            [OrchestrationTrigger] IDurableOrchestrationContext ctx,
            ILogger log)
        {
            log = ctx.CreateReplaySafeLogger(log);

            var students = ctx.GetInput<IEnumerable<Student>>();

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
                    Error = "Failed to process list of students: " + e.Message,
                    Duration = (end - start).TotalSeconds,
                };
            }
        }

        /// <summary>
        /// Generates and archives test reports (PDF) for a single student
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="log"></param>
        /// <returns></returns>
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
    }
}