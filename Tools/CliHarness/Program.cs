using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            Console.Error.WriteLine("Usage: CliHarness <scenario.json> [output-dir] [--interactive]");
            return 1;
        }

        var scenarioPath = args[0];
        var positionalArgs = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        var outputDir = positionalArgs.Length > 0 ? positionalArgs[0] : "harness-output";
        bool interactive = args.Any(a =>
            string.Equals(a, "--interactive", StringComparison.OrdinalIgnoreCase));

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

        var scoreRuleEngine = new ScoreRuleEngineCore(
            bus: bus,
            scoreService: scoreService,
            logger: logger);
        scoreRuleEngine.Subscribe();

        scoreService.ScoreChanged += (newScore, delta, reason) =>
            bus.Publish(new ScoreChangedEventArgs(newScore, delta));

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

        if (interactive)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("[Interactive mode — type your choices at each prompt]");
            Console.ResetColor();
            var actor = new InteractiveActor(bus, taskGroups);
            await actor.PlayAsync();
        }
        else
        {
            var actor = new ScriptedActor(bus, scenario.script ?? new List<ScriptedActor.Step>());
            await actor.PlayAsync();
        }

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
        scoreRuleEngine.Dispose();
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
        public float timeLimit { get; set; } = 0f;
        public List<string> requiredGroups { get; set; } = new();
        public List<ScenarioTask> tasks { get; set; } = new();
    }

    private sealed class ScenarioTask
    {
        public string name { get; set; } = "unnamed";
        public string actionId { get; set; } = string.Empty;
        public int successPoints { get; set; } = 100;
        public int failurePenalty { get; set; } = 0;
        public int ppePenalty { get; set; } = 20;
        public List<string> requiredPPE { get; set; } = new();
        public string hintText { get; set; } = string.Empty;
        public string failureAdvice { get; set; } = string.Empty;
        public string ppeAdvice { get; set; } = string.Empty;
        public string taskDescription { get; set; } = string.Empty;
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

    private static TaskExecutionModeShared ParseExecutionMode(string value, string groupName)
    {
        if (Enum.TryParse<TaskExecutionModeShared>(value, ignoreCase: true, out var mode))
            return mode;

        var valid = string.Join(", ", Enum.GetNames(typeof(TaskExecutionModeShared)));
        throw new InvalidDataException(
            $"Unknown executionMode '{value}' in group '{groupName}'. Valid values: {valid}");
    }

    private static IReadOnlyList<ITaskGroup> BuildTaskGroups(Scenario scenario)
    {
        var result = new List<ITaskGroup>();
        var groupMap = new Dictionary<string, InMemoryTaskGroup>();

        foreach (var g in scenario.groups)
        {
            var group = new InMemoryTaskGroup
            {
                groupName = g.name,
                executionMode = ParseExecutionMode(g.executionMode, g.name),
                timeLimit = g.timeLimit,
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
                    else
                    {
                        var valid = string.Join(", ", Enum.GetNames(typeof(PPEType)));
                        throw new InvalidDataException(
                            $"Unknown PPE type '{ppeStr}' in task '{t.name}' (group '{g.name}'). " +
                            $"Valid values: {valid}");
                    }
                }

                group.tasks.Add(new InMemorySafetyTask
                {
                    taskName = t.name,
                    taskDescription = t.taskDescription,
                    ExpectedActionId = t.actionId,
                    successPoints = t.successPoints,
                    failurePenalty = t.failurePenalty,
                    ppePenalty = t.ppePenalty,
                    requiredPPE = ppeList,
                    hintText = t.hintText,
                    failureAdvice = t.failureAdvice,
                    ppeAdvice = t.ppeAdvice
                });
            }
            result.Add(group);
            groupMap[g.name] = group;
        }

        foreach (var g in scenario.groups)
        {
            if (g.requiredGroups == null || g.requiredGroups.Count == 0) continue;
            if (!groupMap.TryGetValue(g.name, out var group)) continue;
            foreach (var reqName in g.requiredGroups)
            {
                if (groupMap.TryGetValue(reqName, out var reqGroup))
                {
                    group.requiredGroups.Add(reqGroup);
                }
            }
        }

        return result;
    }
}
