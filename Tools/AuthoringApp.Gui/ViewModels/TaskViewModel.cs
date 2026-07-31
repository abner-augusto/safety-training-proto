using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SafetyProto.Core;
using SafetyProto.Domain.Scenarios;

namespace SafetyProto.AuthoringApp.Gui.ViewModels;

/// <summary>
/// One graded step of a risk axis, with the criterion spelled out. The specialist picks from
/// the criteria, not from bare numbers — the grade is the artefact, the wording is what makes
/// it reproducible (and is what NR-01 1.5.4.4.2.2 asks to be documented).
/// </summary>
public sealed record RiskGradeOption(int Grade, string Name, string Criterion)
{
    public override string ToString() => $"{Grade} — {Name}: {Criterion}";
}

/// <summary>
/// Editable wrapper over a <see cref="SafetyTaskDef"/>. Holds live values as the user
/// types; <see cref="ToDef"/> rebuilds a fresh def on save/validate so the VMs stay the
/// single source of truth. Action and PPE choices come from the parent editor's
/// catalog-derived option lists (dropdowns/checkboxes, not free text).
/// </summary>
public sealed class TaskViewModel : ViewModelBase
{
    /// <summary>Display sentinel for an equip-set task (empty <c>actionId</c>).</summary>
    public const string NoActionLabel = "— sem ação (equip-set) —";

    private readonly ScenarioEditorViewModel _editor;

    private string _taskId;
    private string _taskName;
    private string _taskDescription;
    private string _actionId;
    private RiskGradeOption _severity;
    private RiskGradeOption _probability;
    private string _hintText;
    private string _failureAdvice;
    private string _ppeAdvice;
    private string _omissionAdvice;

    public TaskViewModel(SafetyTaskDef def, GroupViewModel group, ScenarioEditorViewModel editor)
    {
        Group = group;
        _editor = editor;

        _taskId = def.RawId;
        _taskName = def.taskName;
        _taskDescription = def.taskDescription;
        _actionId = def.ActionId ?? string.Empty;
        // A pre-matrix task carries only a level token; seed the grades from the closest
        // pair so opening an old scenario shows a coherent classification instead of blanks.
        var seeded = def.Grades != null
            ? RiskAssessment.FromGrades(def.Grades.Severity, def.Grades.Probability)
            : def.risk;
        _severity = GradeFor(SeverityOptions, seeded.HasGrades ? seeded.Severity : SeedSeverity(seeded.Level));
        _probability = GradeFor(ProbabilityOptions, seeded.HasGrades ? seeded.Probability : SeedProbability(seeded.Level));
        _hintText = def.hintText;
        _failureAdvice = def.failureAdvice;
        _ppeAdvice = def.ppeAdvice;
        _omissionAdvice = def.omissionAdvice;

        var selected = new HashSet<string>(def.RequiredPpeNames, System.StringComparer.OrdinalIgnoreCase);
        PpeOptions = new ObservableCollection<PpeToggleViewModel>(
            editor.PpeOptionNames.Select(n => new PpeToggleViewModel(n, selected.Contains(n))));
    }

    /// <summary>The group this task belongs to (for add/remove and re-parenting).</summary>
    public GroupViewModel Group { get; }

    public IReadOnlyList<string> ActionOptions => _editor.ActionOptions;

    public ObservableCollection<PpeToggleViewModel> PpeOptions { get; }

    /// <summary>
    /// Stable, language-independent id (e.g. "equip_helmet"). Optional: when left blank the
    /// runtime and session log fall back to <see cref="TaskName"/>, so existing scenarios keep
    /// working, but authoring a real id keeps analysis keys stable across copy edits.
    /// </summary>
    public string TaskId { get => _taskId; set => SetField(ref _taskId, value); }

    public string TaskName
    {
        get => _taskName;
        set { if (SetField(ref _taskName, value)) OnPropertyChanged(nameof(DisplayName)); }
    }

    public string TaskDescription { get => _taskDescription; set => SetField(ref _taskDescription, value); }

    /// <summary>Bound to the action ComboBox; maps the equip-set sentinel to/from an empty id.</summary>
    public string SelectedAction
    {
        get => string.IsNullOrEmpty(_actionId) ? NoActionLabel : _actionId;
        set
        {
            var id = value == NoActionLabel ? string.Empty : value;
            if (SetField(ref _actionId, id, nameof(SelectedAction))) OnPropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>Severity gradation — NR-01 1.5.4.4.4, magnitude of the worst possible
    /// consequence (1.5.4.4.4.1: when several are possible, take the greatest).</summary>
    public static IReadOnlyList<RiskGradeOption> SeverityOptions { get; } = new[]
    {
        new RiskGradeOption(1, "Desprezível",   "lesão sem afastamento"),
        new RiskGradeOption(2, "Marginal",      "afastamento < 15 dias, sem sequela"),
        new RiskGradeOption(3, "Moderada",      "afastamento > 15 dias, recuperação completa"),
        new RiskGradeOption(4, "Crítica",       "incapacidade permanente parcial"),
        new RiskGradeOption(5, "Catastrófica",  "óbito ou incapacidade permanente total"),
    };

    /// <summary>Probability gradation — NR-01 1.5.4.4.5; for accident-borne injuries
    /// 1.5.4.4.5.4 grades it by exposure to the hazard and the effectiveness of the prevention
    /// measures in place. It is NOT the chance a worker skips the item.</summary>
    public static IReadOnlyList<RiskGradeOption> ProbabilityOptions { get; } = new[]
    {
        new RiskGradeOption(1, "Improvável", "exposição rara, medidas redundantes eficazes"),
        new RiskGradeOption(2, "Remota",     "exposição ocasional, medida presente e eficaz"),
        new RiskGradeOption(3, "Ocasional",  "exposição recorrente, medida de eficácia parcial"),
        new RiskGradeOption(4, "Provável",   "exposição contínua, medida ausente ou ineficaz"),
        new RiskGradeOption(5, "Frequente",  "exposição contínua sem nenhuma medida interposta"),
    };

    public RiskGradeOption SelectedSeverity
    {
        get => _severity;
        set { if (SetField(ref _severity, value)) RaiseRiskChanged(); }
    }

    public RiskGradeOption SelectedProbability
    {
        get => _probability;
        set { if (SetField(ref _probability, value)) RaiseRiskChanged(); }
    }

    private RiskAssessment Risk => RiskAssessment.FromGrades(_severity.Grade, _probability.Grade);

    /// <summary>Read-only readout: the classification the two grades produce.</summary>
    public string RiskLevelDisplay => $"{RiskLevels.DisplayName(Risk.Level)}  ({_severity.Grade} × {_probability.Grade} = {Risk.Index})";

    /// <summary>What the classification obliges, per NR-01 1.5.4.4.3 — shown so the
    /// specialist sees the consequence of the grade, not just its name.</summary>
    public string RiskDecisionHint => RiskLevels.DecisionHint(Risk.Level);

    private void RaiseRiskChanged()
    {
        OnPropertyChanged(nameof(RiskLevelDisplay));
        OnPropertyChanged(nameof(RiskDecisionHint));
    }
    public string HintText { get => _hintText; set => SetField(ref _hintText, value); }
    public string FailureAdvice { get => _failureAdvice; set => SetField(ref _failureAdvice, value); }
    public string PpeAdvice { get => _ppeAdvice; set => SetField(ref _ppeAdvice, value); }
    public string OmissionAdvice { get => _omissionAdvice; set => SetField(ref _omissionAdvice, value); }

    /// <summary>Label shown in the tree.</summary>
    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(_taskName) ? "(tarefa sem nome)" : _taskName;
            var kind = string.IsNullOrEmpty(_actionId) ? "equip-set" : _actionId;
            return $"{name}  ·  {kind}";
        }
    }

    public SafetyTaskDef ToDef() => new()
    {
        RawId = _taskId?.Trim() ?? string.Empty,
        taskName = _taskName,
        taskDescription = _taskDescription,
        ActionId = _actionId,
        Grades = new RiskGrades { Severity = _severity.Grade, Probability = _probability.Grade },
        hintText = _hintText,
        failureAdvice = _failureAdvice,
        ppeAdvice = _ppeAdvice,
        omissionAdvice = _omissionAdvice,
        RequiredPpeNames = PpeOptions.Where(p => p.IsSelected).Select(p => p.Name).ToList(),
    };

    private static RiskGradeOption GradeFor(IReadOnlyList<RiskGradeOption> options, int grade) =>
        options.FirstOrDefault(o => o.Grade == grade) ?? options[2];

    // Seeds for a scenario that declared a level with no grades behind it: the lowest pair
    // that lands in that band, so the readout agrees with the level the file already had.
    private static int SeedSeverity(RiskLevel level) => level switch
    {
        RiskLevel.Trivial => 2,
        RiskLevel.Tolerable => 2,
        RiskLevel.Moderate => 3,
        RiskLevel.Substantial => 4,
        RiskLevel.Intolerable => 5,
        _ => 3
    };

    private static int SeedProbability(RiskLevel level) => level switch
    {
        RiskLevel.Trivial => 2,
        RiskLevel.Tolerable => 4,
        RiskLevel.Moderate => 4,
        RiskLevel.Substantial => 4,
        RiskLevel.Intolerable => 5,
        _ => 4
    };
}
