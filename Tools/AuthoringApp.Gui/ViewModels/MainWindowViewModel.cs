using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SafetyProto.Domain.Actions;
using SafetyProto.Domain.Capabilities;
using SafetyProto.Domain.Scenarios;

namespace SafetyProto.AuthoringApp.Gui.ViewModels;

/// <summary>
/// Shell view-model: owns the open scenario editor plus all IO (open, catalog, validate,
/// save, deploy). Editing lives in <see cref="ScenarioEditorViewModel"/>; this type wires
/// it to disk and to the device via <c>adb</c>. Parsing/validation always go through the
/// shared <see cref="ScenarioLoader"/>/<see cref="ScenarioValidator"/> so the desktop app
/// accepts/rejects exactly what the engine does.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    // Fixed-name runtime override: the app only ever loads "{scenarioResourceName}.json"
    // (default "default"), so deployment always targets default.json under persistentDataPath.
    private const string AndroidPackage = "com.abnersouza.SafetyProto";
    private const string DeployFileName = "default.json";
    private static readonly string DeployRemotePath =
        $"/sdcard/Android/data/{AndroidPackage}/files/scenarios/{DeployFileName}";

    private static readonly string ProjectRoot = FindProjectRoot();
    private static readonly string DefaultScenarioPath = Path.Combine(ProjectRoot, "Assets", "_SafetyProto", "Resources", "Scenarios", "default.json");
    private static readonly string DefaultCapabilityCatalogPath = Path.Combine(ProjectRoot, "Tools", "AuthoringApp", "capability_catalog.json");
    private static readonly string DefaultActionCatalogPath = Path.Combine(ProjectRoot, "Assets", "_SafetyProto", "Resources", "Actions", "actions.json");

    private static readonly JsonSerializerSettings CatalogSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Ignore,
    };

    private string _scenarioPath = string.Empty;
    private string _catalogPath = string.Empty;
    private string _actionCatalogPath = string.Empty;
    private string _status = "Abra um cenário ou crie um novo para começar.";
    private ScenarioEditorViewModel? _editor;
    private ActionCatalogEditorViewModel? _actionEditor;
    private CapabilityCatalog? _catalog;
    private ActionCatalogDef? _actionCatalog;

    public MainWindowViewModel()
    {
        LoadDefaultAuthoringFiles();
    }

    public string ScenarioPath { get => _scenarioPath; set => SetField(ref _scenarioPath, value); }
    public string CatalogPath { get => _catalogPath; set => SetField(ref _catalogPath, value); }
    public string ActionCatalogPath { get => _actionCatalogPath; set => SetField(ref _actionCatalogPath, value); }
    public string Status { get => _status; set => SetField(ref _status, value); }

    public ScenarioEditorViewModel? Editor
    {
        get => _editor;
        private set
        {
            if (SetField(ref _editor, value)) OnPropertyChanged(nameof(HasScenario));
        }
    }

    public bool HasScenario => _editor != null;

    public ActionCatalogEditorViewModel? ActionEditor
    {
        get => _actionEditor;
        private set
        {
            if (SetField(ref _actionEditor, value)) OnPropertyChanged(nameof(HasActionCatalog));
        }
    }

    public bool HasActionCatalog => _actionEditor != null;

    public string SuggestedScenarioDirectory => Path.GetDirectoryName(DefaultScenarioPath) ?? ProjectRoot;
    public string SuggestedCapabilityCatalogDirectory => Path.GetDirectoryName(DefaultCapabilityCatalogPath) ?? ProjectRoot;
    public string SuggestedActionCatalogDirectory => Path.GetDirectoryName(DefaultActionCatalogPath) ?? ProjectRoot;

    /// <summary>Starts a blank scenario with one group and one equip-set task as a seed.</summary>
    public void NewScenario()
    {
        var seed = new ScenarioDef
        {
            Name = "novo_cenario",
            ParticipantId = "P000",
            Groups =
            {
                new TaskGroupDef
                {
                    groupName = "Novo Grupo",
                    ExecutionModeName = "Sequential",
                    TaskDefs = { new SafetyTaskDef { taskName = "Nova Tarefa" } },
                },
            },
        };

        Editor = new ScenarioEditorViewModel(seed, _catalog, _actionCatalog);
        Editor.SelectedNode = Editor.Groups[0];
        ScenarioPath = string.Empty;
        Status = "Cenário novo criado. Edite e salve.";
    }

    /// <summary>Loads and parses a scenario JSON; on success builds the editor tree.</summary>
    public void LoadScenario(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            Status = $"✗ Falha ao ler o arquivo:\n{ex.Message}";
            return;
        }

        var result = ScenarioLoader.Parse(json);
        if (!result.Success || result.Scenario == null)
        {
            Status = "✗ Cenário inválido (estrutura):\n  - " + string.Join("\n  - ", result.Errors);
            return;
        }

        ScenarioPath = path;
        Editor = new ScenarioEditorViewModel(result.Scenario, _catalog, _actionCatalog);
        Editor.SelectedNode = Editor.Groups.Count > 0 ? Editor.Groups[0] : null;
        Status = $"Cenário carregado: {Editor.Name} " +
                 $"({Editor.Groups.Count} grupos, {Editor.Groups.Sum(g => g.Tasks.Count)} tarefas).";
    }

    /// <summary>Loads a capability catalog and refreshes the editor's option lists.</summary>
    public void LoadCatalog(string path)
    {
        try
        {
            _catalog = JsonConvert.DeserializeObject<CapabilityCatalog>(File.ReadAllText(path), CatalogSettings);
        }
        catch (Exception ex)
        {
            Status = $"✗ Falha ao ler o catálogo:\n{ex.Message}";
            return;
        }

        if (_catalog == null)
        {
            Status = "✗ Catálogo vazio/nulo.";
            return;
        }

        CatalogPath = path;
        Status = $"Catálogo v{_catalog.Version} carregado — {_catalog.ActionIds.Count} ações, " +
                 $"{_catalog.PpeTypes.Count} EPIs, {_catalog.Phases.Count} cenas.";

        // Rebuild the editor so action/PPE dropdowns pick up the catalog options.
        if (_editor != null)
        {
            var current = _editor.ToDef();
            Editor = new ScenarioEditorViewModel(current, _catalog, _actionCatalog);
            Editor.SelectedNode = Editor.Groups.Count > 0 ? Editor.Groups[0] : null;
        }
    }

    /// <summary>Loads the editable logical action catalog (Resources/Actions/actions.json).</summary>
    public void LoadActionCatalog(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            Status = $"✗ Falha ao ler o catálogo de ações:\n{ex.Message}";
            return;
        }

        var result = ActionCatalogLoader.Parse(json);
        if (!result.Success || result.Catalog == null)
        {
            Status = "✗ Catálogo de ações inválido:\n  - " + string.Join("\n  - ", result.Errors);
            return;
        }

        _actionCatalog = result.Catalog;
        ActionEditor = new ActionCatalogEditorViewModel(result.Catalog);
        ActionEditor.SelectedAction = ActionEditor.Actions.FirstOrDefault();
        ActionCatalogPath = path;
        RefreshScenarioEditorOptions();
        Status = $"Catálogo de ações carregado — {ActionEditor.Actions.Count} ações.";
    }

    private void LoadDefaultAuthoringFiles()
    {
        var messages = new List<string>();

        if (File.Exists(DefaultCapabilityCatalogPath))
        {
            LoadCatalog(DefaultCapabilityCatalogPath);
            messages.Add($"Catálogo de capacidades padrão carregado: {DefaultCapabilityCatalogPath}");
        }
        else
        {
            messages.Add($"Catálogo de capacidades padrão não encontrado: {DefaultCapabilityCatalogPath}");
        }

        if (File.Exists(DefaultActionCatalogPath))
        {
            LoadActionCatalog(DefaultActionCatalogPath);
            messages.Add($"Catálogo de ações padrão carregado: {DefaultActionCatalogPath}");
        }
        else
        {
            messages.Add($"Catálogo de ações padrão não encontrado: {DefaultActionCatalogPath}");
        }

        messages.Add($"Cenários padrão: {SuggestedScenarioDirectory}");
        Status = string.Join(Environment.NewLine, messages);
    }

    public void NewActionCatalog()
    {
        _actionCatalog = new ActionCatalogDef();
        ActionEditor = new ActionCatalogEditorViewModel(_actionCatalog);
        ActionCatalogPath = string.Empty;
        Status = "Catálogo de ações novo criado. Adicione ações e salve.";
    }

    public void AddAction()
    {
        if (ActionEditor == null) NewActionCatalog();
        ActionEditor!.AddAction();
        SyncActionCatalogFromEditor();
        RefreshScenarioEditorOptions();
    }

    public void RemoveSelectedAction()
    {
        if (ActionEditor == null) return;
        ActionEditor.RemoveSelected();
        SyncActionCatalogFromEditor();
        RefreshScenarioEditorOptions();
    }

    public void ValidateActionCatalog()
    {
        if (ActionEditor == null)
        {
            Status = "Abra ou crie um catálogo de ações primeiro.";
            return;
        }

        var catalog = ActionEditor.ToDef();
        var errors = ActionCatalogEditor.Validate(catalog);
        if (errors.Count == 0)
        {
            _actionCatalog = catalog;
            RefreshScenarioEditorOptions();
            Status = $"Catálogo de ações: ✓ válido ({catalog.Actions.Count} ações).";
        }
        else
        {
            Status = $"Catálogo de ações: ✗ {errors.Count} problema(s):\n  - " + string.Join("\n  - ", errors);
        }
    }

    public void SaveActionCatalog(string path)
    {
        if (ActionEditor == null)
        {
            Status = "Nada para salvar — abra ou crie um catálogo de ações.";
            return;
        }

        var catalog = ActionEditor.ToDef();
        var errors = ActionCatalogEditor.Validate(catalog);
        if (errors.Count > 0)
        {
            Status = "Salvamento cancelado — corrija o catálogo de ações:\n  - " + string.Join("\n  - ", errors);
            return;
        }

        try
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(catalog, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Status = $"✗ Falha ao salvar catálogo de ações:\n{ex.Message}";
            return;
        }

        _actionCatalog = catalog;
        ActionCatalogPath = path;
        RefreshScenarioEditorOptions();
        Status = $"✓ Catálogo de ações salvo em {path}";
    }

    /// <summary>Runs the two-tier validation on the current in-memory edits.</summary>
    public void Validate()
    {
        if (_editor == null)
        {
            Status = "Abra ou crie um cenário primeiro.";
            return;
        }

        var scenario = _editor.ToDef();
        var sb = new StringBuilder();

        // Tier 1 — structural: re-parse via the shared loader (round-trips through JSON).
        var json = Serialize(scenario);
        var load = ScenarioLoader.Parse(json);
        if (!load.Success || load.Scenario == null)
        {
            sb.AppendLine("Estrutura: ✗ inválida");
            foreach (var e in load.Errors) sb.AppendLine($"  - {e}");
            Status = sb.ToString();
            return;
        }

        int groups = load.Scenario.Groups.Count;
        int tasks = load.Scenario.Groups.Sum(g => g.TaskDefs.Count);
        sb.AppendLine($"Cenário: {load.Scenario.Name} ({groups} grupos, {tasks} tarefas)");
        sb.AppendLine("Estrutura: ✓ válida");

        // Tier 2 — semantic: realizable against the loaded build?
        if (_catalog == null)
        {
            sb.AppendLine();
            sb.AppendLine("⚠ Nenhum catálogo carregado — validação semântica pulada.");
            sb.AppendLine("Abra um capability_catalog.json para checar ações/EPIs contra o build.");
            Status = sb.ToString();
            return;
        }

        var errors = ScenarioValidator.Validate(load.Scenario, _catalog);
        if (errors.Count == 0)
        {
            sb.AppendLine($"Catálogo (v{_catalog.Version}): ✓ VÁLIDO");
        }
        else
        {
            sb.AppendLine($"Catálogo (v{_catalog.Version}): ✗ {errors.Count} problema(s):");
            foreach (var e in errors) sb.AppendLine($"  - {e}");
        }

        Status = sb.ToString();
    }

    private void RefreshScenarioEditorOptions()
    {
        if (_editor == null) return;

        var selected = _editor.SelectedNode;
        var current = _editor.ToDef();
        Editor = new ScenarioEditorViewModel(current, _catalog, _actionCatalog);
        Editor.SelectedNode = Editor.Groups.Count > 0 ? Editor.Groups[0] : selected;
    }

    private void SyncActionCatalogFromEditor()
    {
        if (ActionEditor != null) _actionCatalog = ActionEditor.ToDef();
    }

    /// <summary>Serializes the current edits to <paramref name="path"/> (Indented, matching the Unity baker).</summary>
    public void Save(string path)
    {
        if (_editor == null)
        {
            Status = "Nada para salvar — abra ou crie um cenário.";
            return;
        }

        try
        {
            File.WriteAllText(path, Serialize(_editor.ToDef()));
        }
        catch (Exception ex)
        {
            Status = $"✗ Falha ao salvar:\n{ex.Message}";
            return;
        }

        ScenarioPath = path;
        Status = $"✓ Salvo em {path}";
    }

    /// <summary>
    /// Deploys the current edits to the headset over <c>adb</c>: writes a temp default.json
    /// and pushes it to persistentDataPath. No rebuild needed — the app loads the override
    /// on next start.
    /// </summary>
    public async Task DeployAsync()
    {
        if (_editor == null)
        {
            Status = "Nada para implantar — abra ou crie um cenário.";
            return;
        }

        // Validate before pushing — a broken override silently falls back to the embedded default.
        Validate();
        if (Status.Contains("✗"))
        {
            Status = "Deploy cancelado — corrija os erros de validação antes de implantar.\n\n" + Status;
            return;
        }

        var temp = Path.Combine(Path.GetTempPath(), DeployFileName);
        try
        {
            File.WriteAllText(temp, Serialize(_editor.ToDef()));
        }
        catch (Exception ex)
        {
            Status = $"✗ Falha ao preparar o arquivo de deploy:\n{ex.Message}";
            return;
        }

        Status = "Implantando via adb…";
        var (ok, output) = await RunAdbPushAsync(temp, DeployRemotePath);
        Status = ok
            ? $"✓ Implantado em {DeployRemotePath}\nReinicie o app no headset para carregar o override.\n\n{output}".TrimEnd()
            : $"✗ Falha no adb push:\n{output}\n\nVerifique se o headset está conectado (adb devices) e a depuração USB ativa.";
    }

    private static string Serialize(ScenarioDef scenario) =>
        JsonConvert.SerializeObject(scenario, Formatting.Indented);

    private static async Task<(bool ok, string output)> RunAdbPushAsync(string localPath, string remotePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "adb",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("push");
            psi.ArgumentList.Add(localPath);
            psi.ArgumentList.Add(remotePath);

            using var process = Process.Start(psi);
            if (process == null) return (false, "Não foi possível iniciar o processo 'adb'.");

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var combined = string.Join("\n", new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)))
                                 .Trim();
            return (process.ExitCode == 0, combined);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (false, "'adb' não encontrado no PATH. Instale o Android Platform Tools e reinicie o app.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string FindProjectRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Assets", "_SafetyProto")) &&
                Directory.Exists(Path.Combine(current.FullName, "Tools", "AuthoringApp")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
