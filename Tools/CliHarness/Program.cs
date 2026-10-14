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
    private const string CanonicalScenarioRelativePath = "Assets/_SafetyProto/Resources/Scenarios/default.json";

    public static async Task<int> Main(string[] args)
    {
        var positionalArgs = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (positionalArgs.Length > 1 ||
            positionalArgs.Any(a => a.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("Usage: CliHarness [output-dir] [--interactive] [--mode=guiado|avaliacao]");
            Console.Error.WriteLine($"The harness always plays the canonical scenario ({CanonicalScenarioRelativePath}).");
            return 1;
        }
        var outputDir = positionalArgs.Length > 0 ? positionalArgs[0] : "harness-output";
        bool interactive = args.Any(a =>
            string.Equals(a, "--interactive", StringComparison.OrdinalIgnoreCase));

        var modeArg = args.FirstOrDefault(a => a.StartsWith("--mode=", StringComparison.OrdinalIgnoreCase));
        var mode = modeArg != null && modeArg.EndsWith("avaliacao", StringComparison.OrdinalIgnoreCase)
            ? SessionMode.Evaluation
            : SessionMode.Guided;
        SessionModeState.Current = mode;

        var scenarioPath = FindInRepo(CanonicalScenarioRelativePath);
        if (scenarioPath == null)
        {
            Console.Error.WriteLine($"Canonical scenario not found: run from inside the repository ({CanonicalScenarioRelativePath}).");
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
            verboseLogging: false,
            modeSource: () => mode);
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
            delayBetweenTasks: 0f,
            modeSource: () => mode);
        taskManager.Subscribe();

        var sessionLogger = new SessionLoggerCore(
            eventBus: bus,
            outputDirectory: outputDir,
            serialize: SessionLoggerCore.SerializeIndentedOmittingDefaults,
            logger: logger,
            actionNameResolver: BuildActionNameResolver(),
            modeSource: () => mode);
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
    /// Resolves a repo-relative path by walking up from the working directory, then from the
    /// harness binary, so <c>dotnet run --project Tools/CliHarness</c> works from any folder
    /// inside the repository. Returns null outside it.
    /// </summary>
    private static string? FindInRepo(string relativePath)
    {
        foreach (var seed in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = seed; dir != null; dir = Path.GetDirectoryName(dir))
            {
                var candidate = Path.Combine(dir, relativePath);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Best-effort actionId → friendly-name resolver for nicer harness logs, read from the repo's
    /// embedded action catalog. Returns the raw id when the catalog can't be found.
    /// </summary>
    private static Func<string, string> BuildActionNameResolver()
    {
        var catalogPath = FindInRepo("Assets/_SafetyProto/Resources/Actions/actions.json");

        var catalog = catalogPath != null
            ? ActionCatalogLoader.Parse(File.ReadAllText(catalogPath)).Catalog
            : null;

        return id => (catalog != null && catalog.TryGet(id, out var def) && def != null)
            ? def.ResolveLogName()
            : id;
    }
}
