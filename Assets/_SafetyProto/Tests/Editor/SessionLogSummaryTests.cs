using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Domain.Sessions;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Locks the shape of the session log's summary. These files are the collected data of the
    /// study, so the field names and the outcome tokens are a contract: a rename here silently
    /// invalidates every analysis script pointed at the logs already gathered.
    /// </summary>
    public class SessionLogSummaryTests
    {
        private FakeEventBus _bus = null!;
        private string _outputDir = string.Empty;

        [SetUp]
        public void Setup()
        {
            _bus = new FakeEventBus();
            _outputDir = Path.Combine(Path.GetTempPath(), "safetyproto_logtests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_outputDir)) Directory.Delete(_outputDir, recursive: true);
        }

        [Test]
        public void CompletedSession_WritesPerTaskOutcomeBlock()
        {
            using var logger = new SessionLoggerCore(
                _bus, _outputDir, SessionLoggerCore.SerializeIndentedOmittingDefaults);
            logger.Subscribe();

            _bus.Publish(new SessionStartedEventArgs { SessionId = "S1", TotalTasks = 3, TimestampMs = 1000L });

            var summary = new SessionCompletedEventArgs(
                totalElapsedTime: 120f,
                totalScore: 300,
                tasksCompleted: 1,
                totalTasks: 3,
                orderViolationCount: 0,
                taskOutcomes: new[]
                {
                    Outcome("equip_helmet", "ppe_selection", TaskState.CompletedSuccess, 4, 3),
                    Outcome("equip_boots", "ppe_selection", TaskState.NotPerformed, 3, 2),
                    Outcome("connect_harness", "inspection", TaskState.CompletedSuccessButUnsafe, 5, 4)
                })
            { SessionId = "S1", TimestampMs = 2000L };

            _bus.Publish(summary);

            var tasks = ReadWrittenSummary()["tasks"] as JArray;
            Assert.IsNotNull(tasks, "The summary must carry the per-task block.");
            Assert.AreEqual(3, tasks!.Count);

            // Outcome tokens are the contract; they intentionally do not track the enum names.
            Assert.AreEqual("completed", (string?)tasks[0]["outcome"]);
            Assert.AreEqual("not_performed", (string?)tasks[1]["outcome"]);
            Assert.AreEqual("completed_unsafe", (string?)tasks[2]["outcome"]);

            // The grading travels with the outcome so adherence can be weighted by risk without
            // joining the log back to the scenario it ran from.
            Assert.AreEqual("equip_boots", (string?)tasks[1]["taskId"]);
            Assert.AreEqual("ppe_selection", (string?)tasks[1]["groupId"]);
            Assert.AreEqual(3, (int?)tasks[1]["riskSeverity"]);
            Assert.AreEqual(2, (int?)tasks[1]["riskProbability"]);
            Assert.IsNotEmpty((string?)tasks[1]["riskLevel"] ?? string.Empty);
        }

        [Test]
        public void AbandonedSession_WritesSummaryWithoutTaskBlock()
        {
            // A reset/abandoned run has no task list to report. The fallback summary must still
            // be written, and must not fake an all-pending block that never happened.
            using var logger = new SessionLoggerCore(
                _bus, _outputDir, SessionLoggerCore.SerializeIndentedOmittingDefaults);
            logger.Subscribe();

            _bus.Publish(new SessionStartedEventArgs { SessionId = "S1", TotalTasks = 3, TimestampMs = 1000L });
            logger.WriteLogAsync().GetAwaiter().GetResult();

            var written = ReadWrittenSummary();
            Assert.IsNull(written["tasks"]);
            Assert.AreEqual(3, (int?)written["totalTasks"]);
        }

        private static TaskOutcome Outcome(string id, string groupId, TaskState state, int severity, int probability) =>
            new TaskOutcome
            {
                TaskId = id,
                TaskName = id,
                GroupId = groupId,
                GroupName = groupId,
                State = state,
                Risk = RiskAssessment.FromGrades(severity, probability),
                CompletionTime = 10f
            };

        /// <summary>
        /// SessionCompleted triggers the write without awaiting it, so the file lands a moment
        /// after Publish returns. Poll rather than sleep a fixed amount: the read also has to
        /// survive catching the file mid-write, which shows up as a parse failure.
        /// </summary>
        private JToken ReadWrittenSummary()
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            Exception? last = null;

            while (DateTime.UtcNow < deadline)
            {
                var files = Directory.Exists(_outputDir)
                    ? Directory.GetFiles(_outputDir, "session_log_*.json")
                    : Array.Empty<string>();

                if (files.Length == 1)
                {
                    try
                    {
                        var summary = JObject.Parse(File.ReadAllText(files[0]))["summary"];
                        if (summary != null) return summary;
                    }
                    catch (Exception ex) { last = ex; }
                }
                else if (files.Length > 1)
                {
                    Assert.Fail($"Exactly one log file expected, found {files.Length}.");
                }

                System.Threading.Thread.Sleep(20);
            }

            Assert.Fail($"No readable session log with a summary was written. Last error: {last?.Message ?? "(none)"}");
            throw new InvalidOperationException("unreachable");
        }
    }
}
