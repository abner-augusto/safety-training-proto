using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Data.Enums;
using SafetyProto.Gameplay.Safety;
using SafetyProto.Gameplay.Task;
using SafetyProto.Utils;

namespace SafetyProto.CliHarness;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: CliHarness <scenario.json> [output-dir]");
            return 1;
        }

        var scenarioPath = args[0];
        var outputDir = args.Length >= 2 ? args[1] : "harness-output";

        if (!File.Exists(scenarioPath))
        {
            Console.Error.WriteLine($"Scenario file not found: {scenarioPath}");
            return 1;
        }

        var scenario = LoadScenario(scenarioPath);
        Console.WriteLine($"=== SafetyProto CLI Harness ===");
        Console.WriteLine($"Scenario: {scenario.name}");
        Console.WriteLine($"Participant: {scenario.participantId}");
        Console.WriteLine($"Groups: {scenario.groups.Count}");
        Console.WriteLine();

        var bus = new HarnessEventBus();
        var timer = new StopwatchTimerSource();
        var logger = new ConsoleHarnessLogger();
        var scoreService = new ScoreService();

        var taskGroups = BuildTaskGroups(scenario);

        var ppeManager = new HarnessPPEManager(bus);
        ppeManager.Subscribe();

        var ruleEngine = new SafetyRuleEngineCore(
            bus: bus,
            ppeChecker: ppeManager,
            timer: timer,
            logger: logger,
            verboseLogging: false);
        ruleEngine.Subscribe();

        scoreService.ScoreChanged += (newScore, delta, reason) =>
            bus.Publish(new ScoreChangedEventArgs(newScore, delta));

        bus.Subscribe<TaskEventArgs>(args =>
        {
            if (args.Phase != TaskPhase.Completed || args.Task == null) return;
            scoreService.AddPoints(args.Task.successPoints, $"Task '{args.Task.taskName}' completed");
        });

        var taskManager = new TaskManagerCore(
            bus: bus,
            scoreService: scoreService,
            taskGroups: taskGroups,
            timer: timer,
            scheduler: null,
            logger: logger,
            delayBetweenTasks: 0f);
        taskManager.Subscribe();

        var sessionLogger = new SessionLoggerCore(
            eventBus: bus,
            outputDirectory: outputDir,
            serialize: log => JsonSerializer.Serialize(log, new JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = true
            }),
            logger: logger);
        sessionLogger.Subscribe();

        var transcript = new TranscriptRecorder(bus);
        transcript.Subscribe();

        EventContext.StartSession(
            sessionId: Guid.NewGuid().ToString(),
            playerId: scenario.participantId,
            scenarioId: scenario.name);

        bus.Publish(new SessionStartedEventArgs());
        Console.WriteLine("--- Transcript ---");
        taskManager.StartSession();

        var scriptedActor = new ScriptedActor(bus, scenario.script);
        await scriptedActor.PlayAsync();

        await Task.Delay(200);

        Console.WriteLine("------------------");
        Console.WriteLine();

        var summary = taskManager.LastSessionSummary;
        if (summary.HasValue)
        {
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Session summary: {0}/{1} tasks, score {2}, {3:F2}s",
                summary.Value.tasksCompleted,
                summary.Value.totalTasks,
                summary.Value.totalScore,
                summary.Value.totalElapsedTime));
        }
        else
        {
            Console.WriteLine("(Session did not complete — not all tasks attempted)");
        }

        var logFile = await sessionLogger.WriteLogAsync();
        if (!string.IsNullOrEmpty(logFile))
        {
            Console.WriteLine($"Log written to: {logFile}");
        }

        transcript.Dispose();
        sessionLogger.Dispose();
        taskManager.Dispose();
        ruleEngine.Dispose();
        ppeManager.Dispose();
        EventContext.Clear();

        return 0;
    }

    private sealed class Scenario
    {
        public string name { get; set; } = "unnamed";
        public string participantId { get; set; } = "P000";
        public List<ScenarioGroup> groups { get; set; } = new();
        public List<ScriptedActor.Step> script { get; set; } = new();
    }

    private sealed class ScenarioGroup
    {
        public string name { get; set; } = "unnamed";
        public string executionMode { get; set; } = "Sequential";
        public List<ScenarioTask> tasks { get; set; } = new();
    }

    private sealed class ScenarioTask
    {
        public string name { get; set; } = "unnamed";
        public string actionId { get; set; } = string.Empty;
        public int successPoints { get; set; } = 100;
        public List<string> requiredPPE { get; set; } = new();
    }

    private static Scenario LoadScenario(string path)
    {
        var json = File.ReadAllText(path);
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true
        };
        var scenario = JsonSerializer.Deserialize<Scenario>(json, opts)
            ?? throw new InvalidDataException($"Failed to parse scenario: {path}");
        return scenario;
    }

    private static IReadOnlyList<ITaskGroup> BuildTaskGroups(Scenario scenario)
    {
        var result = new List<ITaskGroup>();
        foreach (var g in scenario.groups)
        {
            var group = new InMemoryTaskGroup
            {
                groupName = g.name,
                executionMode = Enum.TryParse<TaskExecutionModeShared>(g.executionMode, ignoreCase: true, out var m)
                    ? m
                    : TaskExecutionModeShared.Sequential,
            };
            foreach (var t in g.tasks)
            {
                var ppeList = new List<PPEType>();
                foreach (var ppeStr in t.requiredPPE)
                {
                    if (Enum.TryParse<PPEType>(ppeStr, ignoreCase: true, out var ppe))
                    {
                        ppeList.Add(ppe);
                    }
                }

                group.tasks.Add(new InMemorySafetyTask
                {
                    taskName = t.name,
                    ExpectedActionId = t.actionId,
                    successPoints = t.successPoints,
                    requiredPPE = ppeList
                });
            }
            result.Add(group);
        }
        return result;
    }
}
