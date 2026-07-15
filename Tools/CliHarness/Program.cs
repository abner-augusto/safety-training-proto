using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Actions;
using SafetyProto.Domain.Safety;
using SafetyProto.Domain.Scenarios;
using SafetyProto.Domain.Scoring;
using SafetyProto.Domain.Sessions;
using SafetyProto.Domain.Tasks;

namespace SafetyProto.CliHarness;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: CliHarness <scenario.json> [output-dir] [--interactive] [--mode=guiado|avaliacao]");
            return 1;
        }

        var scenarioPath = args[0];
        var positionalArgs = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        var outputDir = positionalArgs.Length > 0 ? positionalArgs[0] : "harness-output";
        bool interactive = args.Any(a =>
            string.Equals(a, "--interactive", StringComparison.OrdinalIgnoreCase));

        var modeArg = args.FirstOrDefault(a => a.StartsWith("--mode=", StringComparison.OrdinalIgnoreCase));
        SessionModeState.Current = modeArg != null && modeArg.EndsWith("avaliacao", StringComparison.OrdinalIgnoreCase)
            ? SessionMode.Evaluation
            : SessionMode.Guided;

        if (!File.Exists(scenarioPath))
        {
            Console.Error.WriteLine($"Scenario file not found: {scenarioPath}");
            return 1;
        }

        var loadResult = ScenarioLoader.Parse(File.ReadAllText(scenarioPath));
        if (!loadResult.Success || loadResult.Scenario == null)
        {
            Console.Error.WriteLine($"Failed to load scenario '{scenarioPath}':");
            foreach (var err in loadResult.Errors)
            {
                Console.Error.WriteLine($"  - {err}");
            }
            return 1;
        }

        var scenario = loadResult.Scenario;
        Console.WriteLine($"=== SafetyProto CLI Harness ===");
        Console.WriteLine($"Scenario: {scenario.Name}");
        Console.WriteLine($"Participant: {scenario.ParticipantId}");
        Console.WriteLine($"Groups: {scenario.Groups.Count}");
        Console.WriteLine();

        var bus = new HarnessEventBus();
        var timer = new StopwatchTimerSource();
        var logger = new ConsoleHarnessLogger();
        var scoreService = new ScoreService();

        var taskGroups = (IReadOnlyList<ITaskGroup>)scenario.Groups;

        var ruleEngine = new SafetyRuleEngineCore(
            bus: bus,
            timer: timer,
            logger: logger,
            verboseLogging: false);
        ruleEngine.Subscribe();

        var scoreRuleEngine = new ScoreRuleEngineCore(
            bus: bus,
            scoreService: scoreService,
            logger: logger,
            config: scenario.Scoring);
        scoreRuleEngine.Subscribe();

        scoreService.ScoreChanged += (newScore, delta, reason, taskId) =>
            bus.Publish(new ScoreChangedEventArgs(newScore, delta) { TaskId = taskId, Reason = reason });

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
            serialize: SessionLoggerCore.SerializeIndentedOmittingDefaults,
            logger: logger,
            actionNameResolver: BuildActionNameResolver(scenarioPath));
        sessionLogger.Subscribe();

        var transcript = new TranscriptRecorder(bus);
        transcript.Subscribe();

        EventContext.StartSession(
            sessionId: Guid.NewGuid().ToString(),
            playerId: scenario.ParticipantId,
            scenarioId: scenario.Name);

        var totalTasks = scenario.Groups.Sum(g => g.TaskDefs.Count);
        bus.Publish(new SessionStartedEventArgs { TotalTasks = totalTasks });
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
            var actor = new ScriptedActor(bus, scenario.Script ?? new List<ScriptStepDef>(), taskManager);
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
        EventContext.Clear();
        SessionModeState.Reset();

        return 0;
    }

    /// <summary>
    /// Best-effort actionId → friendly-name resolver for nicer harness logs. Walks up from the
    /// scenario file and the working directory looking for the repo's embedded action catalog
    /// (<c>Assets/_SafetyProto/Resources/Actions/actions.json</c>). Returns the raw id when the
    /// catalog can't be found, so the harness works standalone.
    /// </summary>
    private static Func<string, string> BuildActionNameResolver(string scenarioPath)
    {
        const string relative = "Assets/_SafetyProto/Resources/Actions/actions.json";
        var seeds = new[]
        {
            Path.GetDirectoryName(Path.GetFullPath(scenarioPath)),
            Directory.GetCurrentDirectory()
        };

        string? catalogPath = null;
        foreach (var seed in seeds)
        {
            for (var dir = seed; dir != null; dir = Path.GetDirectoryName(dir))
            {
                var candidate = Path.Combine(dir, relative);
                if (File.Exists(candidate)) { catalogPath = candidate; break; }
            }
            if (catalogPath != null) break;
        }

        var catalog = catalogPath != null
            ? ActionCatalogLoader.Parse(File.ReadAllText(catalogPath)).Catalog
            : null;

        return id => (catalog != null && catalog.TryGet(id, out var def) && def != null)
            ? def.ResolveLogName()
            : id;
    }
}
