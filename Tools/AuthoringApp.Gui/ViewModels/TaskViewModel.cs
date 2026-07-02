using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SafetyProto.Domain.Scenarios;

namespace SafetyProto.AuthoringApp.Gui.ViewModels;

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

    private string _taskName;
    private string _taskDescription;
    private string _actionId;
    private int _successPoints;
    private int _failurePenalty;
    private int _ppePenalty;
    private string _hintText;
    private string _failureAdvice;
    private string _ppeAdvice;

    public TaskViewModel(SafetyTaskDef def, GroupViewModel group, ScenarioEditorViewModel editor)
    {
        Group = group;
        _editor = editor;

        _taskName = def.taskName;
        _taskDescription = def.taskDescription;
        _actionId = def.ActionId ?? string.Empty;
        _successPoints = def.successPoints;
        _failurePenalty = def.failurePenalty;
        _ppePenalty = def.ppePenalty;
        _hintText = def.hintText;
        _failureAdvice = def.failureAdvice;
        _ppeAdvice = def.ppeAdvice;

        var selected = new HashSet<string>(def.RequiredPpeNames, System.StringComparer.OrdinalIgnoreCase);
        PpeOptions = new ObservableCollection<PpeToggleViewModel>(
            editor.PpeOptionNames.Select(n => new PpeToggleViewModel(n, selected.Contains(n))));
    }

    /// <summary>The group this task belongs to (for add/remove and re-parenting).</summary>
    public GroupViewModel Group { get; }

    public IReadOnlyList<string> ActionOptions => _editor.ActionOptions;

    public ObservableCollection<PpeToggleViewModel> PpeOptions { get; }

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

    public int SuccessPoints { get => _successPoints; set => SetField(ref _successPoints, value); }
    public int FailurePenalty { get => _failurePenalty; set => SetField(ref _failurePenalty, value); }
    public int PpePenalty { get => _ppePenalty; set => SetField(ref _ppePenalty, value); }
    public string HintText { get => _hintText; set => SetField(ref _hintText, value); }
    public string FailureAdvice { get => _failureAdvice; set => SetField(ref _failureAdvice, value); }
    public string PpeAdvice { get => _ppeAdvice; set => SetField(ref _ppeAdvice, value); }

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
        taskName = _taskName,
        taskDescription = _taskDescription,
        ActionId = _actionId,
        successPoints = _successPoints,
        failurePenalty = _failurePenalty,
        ppePenalty = _ppePenalty,
        hintText = _hintText,
        failureAdvice = _failureAdvice,
        ppeAdvice = _ppeAdvice,
        RequiredPpeNames = PpeOptions.Where(p => p.IsSelected).Select(p => p.Name).ToList(),
    };
}
