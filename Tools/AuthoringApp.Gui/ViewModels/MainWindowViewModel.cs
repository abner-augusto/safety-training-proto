using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using SafetyProto.Domain.Capabilities;
using SafetyProto.Domain.Scenarios;

namespace SafetyProto.AuthoringApp.Gui.ViewModels;

/// <summary>
/// First slice — Open + View + Validate. Read-only. Loads a scenario JSON and an optional
/// capability catalog, renders the scenario as an indented tree, and runs the same two-tier
/// validation the runtime/CLI use (structural <see cref="ScenarioLoader"/> + semantic
/// <see cref="ScenarioValidator"/>). No editing yet — that is the next slice.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private string _scenarioPath = string.Empty;
    private string _catalogPath = string.Empty;
    private string _scenarioSummary = string.Empty;
    private string _validationOutput = string.Empty;

    // The last successfully-parsed scenario/catalog, reused by Validate without re-reading disk.
    private ScenarioDef? _scenario;
    private CapabilityCatalog? _catalog;

    public string ScenarioPath { get => _scenarioPath; set => SetField(ref _scenarioPath, value); }
    public string CatalogPath { get => _catalogPath; set => SetField(ref _catalogPath, value); }
    public string ScenarioSummary { get => _scenarioSummary; set => SetField(ref _scenarioSummary, value); }
    public string ValidationOutput { get => _validationOutput; set => SetField(ref _validationOutput, value); }

    /// <summary>Loads and parses a scenario JSON; renders its tree or shows structural errors.</summary>
    public void LoadScenario(string path)
    {
        ScenarioPath = path;
        _scenario = null;
        ScenarioSummary = string.Empty;

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            ValidationOutput = $"✗ Falha ao ler o arquivo:\n{ex.Message}";
            return;
        }

        var result = ScenarioLoader.Parse(json);
        if (!result.Success || result.Scenario == null)
        {
            ValidationOutput = "✗ Cenário inválido (estrutura):\n  - " +
                               string.Join("\n  - ", result.Errors);
            return;
        }

        _scenario = result.Scenario;
        ScenarioSummary = BuildTree(_scenario);
        ValidationOutput = "Cenário carregado. Clique em Validar para checar contra o catálogo.";
    }

    /// <summary>Loads a capability catalog JSON (optional; enables tier-2 validation).</summary>
    public void LoadCatalog(string path)
    {
        CatalogPath = path;
        _catalog = null;
        try
        {
            _catalog = JsonConvert.DeserializeObject<CapabilityCatalog>(File.ReadAllText(path));
            ValidationOutput = _catalog == null
                ? "✗ Catálogo vazio/nulo."
                : $"Catálogo v{_catalog.Version} carregado — {_catalog.ActionIds.Count} ações, " +
                  $"{_catalog.PpeTypes.Count} EPIs, {_catalog.Phases.Count} cenas.";
        }
        catch (Exception ex)
        {
            ValidationOutput = $"✗ Falha ao ler o catálogo:\n{ex.Message}";
        }
    }

    /// <summary>Runs the two-tier validation and writes a human-readable verdict.</summary>
    public void Validate()
    {
        if (_scenario == null)
        {
            ValidationOutput = "Abra um cenário válido primeiro.";
            return;
        }

        var sb = new StringBuilder();
        int groups = _scenario.Groups.Count;
        int tasks = _scenario.Groups.Sum(g => g.TaskDefs.Count);
        sb.AppendLine($"Cenário: {_scenario.Name} ({groups} grupos, {tasks} tarefas)");
        sb.AppendLine("Estrutura: ✓ válida");

        if (_catalog == null)
        {
            sb.AppendLine();
            sb.AppendLine("⚠ Nenhum catálogo carregado — validação semântica pulada.");
            sb.AppendLine("Abra um capability_catalog.json para checar ações/EPIs contra o build.");
            ValidationOutput = sb.ToString();
            return;
        }

        var errors = ScenarioValidator.Validate(_scenario, _catalog);
        if (errors.Count == 0)
        {
            sb.AppendLine($"Catálogo (v{_catalog.Version}): ✓ VÁLIDO");
        }
        else
        {
            sb.AppendLine($"Catálogo (v{_catalog.Version}): ✗ {errors.Count} problema(s):");
            foreach (var e in errors) sb.AppendLine($"  - {e}");
        }

        ValidationOutput = sb.ToString();
    }

    private static string BuildTree(ScenarioDef scenario)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{scenario.Name}  (participante: {scenario.ParticipantId})");
        foreach (var g in scenario.Groups)
        {
            var reqs = g.RequiredGroupNames.Count > 0
                ? $"  [requer: {string.Join(", ", g.RequiredGroupNames)}]"
                : string.Empty;
            sb.AppendLine($"├─ {g.groupName}  ({g.ExecutionModeName}, {g.timeLimit:0}s){reqs}");
            foreach (var t in g.TaskDefs)
            {
                var action = string.IsNullOrWhiteSpace(t.ActionId) ? "equip-set" : t.ActionId;
                var ppe = t.RequiredPpeNames.Count > 0 ? string.Join("/", t.RequiredPpeNames) : "—";
                sb.AppendLine($"│    • {t.taskName}");
                sb.AppendLine($"│        ação: {action}  |  pts: {t.successPoints}  |  EPI: {ppe}");
            }
        }
        return sb.ToString();
    }
}
