using Newtonsoft.Json;
using SafetyProto.Domain.Capabilities;
using SafetyProto.Domain.Scenarios;

namespace SafetyProto.AuthoringApp;

/// <summary>
/// Desktop authoring tool for safety-training scenarios. Runs entirely outside Unity,
/// linking <c>SafetyProto.Shared</c> — the same model, loader, and validator the engine
/// uses. This is the engine-agnostic foundation: a safety specialist authors against a
/// build's <c>capability_catalog.json</c> and the scenario is validated here before it
/// ever reaches the headset. A future GUI sits on top of these same calls.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        return args[0].ToLowerInvariant() switch
        {
            "validate" => Validate(args),
            "info" => Info(args),
            _ => UnknownCommand(args[0]),
        };
    }

    private static int Validate(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: AuthoringApp validate <scenario.json> [catalog.json]");
            return 1;
        }

        var scenarioPath = args[1];
        var catalogPath = args.Length > 2 ? args[2] : DefaultCatalogPath();

        if (!File.Exists(scenarioPath))
        {
            Console.Error.WriteLine($"Scenario not found: {scenarioPath}");
            return 1;
        }

        // Tier 1 — structural: does it parse and bind?
        var load = ScenarioLoader.Parse(File.ReadAllText(scenarioPath));
        if (!load.Success || load.Scenario == null)
        {
            Console.WriteLine($"INVALID — {scenarioPath}");
            Console.WriteLine("Structural errors:");
            foreach (var e in load.Errors) Console.WriteLine($"  - {e}");
            return 2;
        }

        Console.WriteLine($"Scenario: {load.Scenario.Name} " +
                          $"({load.Scenario.Groups.Count} groups, " +
                          $"{load.Scenario.Groups.Sum(g => g.TaskDefs.Count)} tasks)");

        // Tier 2 — semantic: is it realizable against a concrete build?
        var catalog = TryLoadCatalog(catalogPath);
        if (catalog == null)
        {
            Console.WriteLine($"WARN — no capability catalog at '{catalogPath}'; skipped catalog validation.");
            Console.WriteLine("VALID (structure only).");
            return 0;
        }

        var errors = ScenarioValidator.Validate(load.Scenario, catalog);
        if (errors.Count > 0)
        {
            Console.WriteLine($"INVALID against catalog '{catalogPath}':");
            foreach (var e in errors) Console.WriteLine($"  - {e}");
            return 2;
        }

        Console.WriteLine($"VALID against catalog (v{catalog.Version}).");
        return 0;
    }

    private static int Info(string[] args)
    {
        var catalogPath = args.Length > 1 ? args[1] : DefaultCatalogPath();
        var catalog = TryLoadCatalog(catalogPath);
        if (catalog == null)
        {
            Console.Error.WriteLine($"No capability catalog at '{catalogPath}'.");
            return 1;
        }

        Console.WriteLine($"Capability catalog (v{catalog.Version}) — {catalogPath}");
        Console.WriteLine($"  Actions ({catalog.ActionIds.Count}): {string.Join(", ", catalog.ActionIds)}");
        Console.WriteLine($"  PPE ({catalog.PpeTypes.Count}): {string.Join(", ", catalog.PpeTypes)}");
        Console.WriteLine($"  Scenes ({catalog.Phases.Count}): {string.Join(", ", catalog.Phases)}");
        return 0;
    }

    private static CapabilityCatalog? TryLoadCatalog(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonConvert.DeserializeObject<CapabilityCatalog>(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Failed to read catalog '{path}': {ex.Message}");
            return null;
        }
    }

    private static string DefaultCatalogPath() =>
        Path.Combine(AppContext.BaseDirectory, "capability_catalog.json") is var local && File.Exists(local)
            ? local
            : Path.Combine(Directory.GetCurrentDirectory(), "capability_catalog.json");

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: {cmd}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("SafetyProto Authoring App");
        Console.WriteLine("Commands:");
        Console.WriteLine("  validate <scenario.json> [catalog.json]  Parse + catalog-validate a scenario");
        Console.WriteLine("  info [catalog.json]                       Print the capability catalog");
    }
}
