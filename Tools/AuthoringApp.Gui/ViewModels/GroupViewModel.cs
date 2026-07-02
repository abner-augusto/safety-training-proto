using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SafetyProto.Domain.Scenarios;

namespace SafetyProto.AuthoringApp.Gui.ViewModels;

/// <summary>
/// Editable wrapper over a <see cref="TaskGroupDef"/>. Owns its <see cref="TaskViewModel"/>
/// children; <see cref="ToDef"/> rebuilds a fresh def on save/validate.
/// </summary>
public sealed class GroupViewModel : ViewModelBase
{
    private readonly ScenarioEditorViewModel _editor;

    private string _groupName;
    private string _executionMode;
    private float _timeLimit;
    private string _requiredGroupsCsv;

    public GroupViewModel(TaskGroupDef def, ScenarioEditorViewModel editor)
    {
        _editor = editor;
        _groupName = def.groupName;
        _executionMode = def.ExecutionModeName;
        _timeLimit = def.timeLimit;
        _requiredGroupsCsv = string.Join(", ", def.RequiredGroupNames);

        Tasks = new ObservableCollection<TaskViewModel>(
            def.TaskDefs.Select(t => new TaskViewModel(t, this, editor)));
    }

    public ObservableCollection<TaskViewModel> Tasks { get; }

    public IReadOnlyList<string> ExecutionModes => ScenarioEditorViewModel.ExecutionModeNames;

    public string GroupName
    {
        get => _groupName;
        set { if (SetField(ref _groupName, value)) OnPropertyChanged(nameof(DisplayName)); }
    }

    public string ExecutionMode
    {
        get => _executionMode;
        set { if (SetField(ref _executionMode, value)) OnPropertyChanged(nameof(DisplayName)); }
    }

    public float TimeLimit { get => _timeLimit; set => SetField(ref _timeLimit, value); }

    /// <summary>Comma-separated group names this group depends on (resolved by the loader).</summary>
    public string RequiredGroupsCsv { get => _requiredGroupsCsv; set => SetField(ref _requiredGroupsCsv, value); }

    /// <summary>Label shown in the tree.</summary>
    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(_groupName) ? "(grupo sem nome)" : _groupName;
            return $"{name}  ·  {_executionMode}";
        }
    }

    public TaskViewModel AddTask()
    {
        var task = new TaskViewModel(new SafetyTaskDef { taskName = "Nova Tarefa" }, this, _editor);
        Tasks.Add(task);
        return task;
    }

    public TaskGroupDef ToDef() => new()
    {
        groupName = _groupName,
        ExecutionModeName = _executionMode,
        timeLimit = _timeLimit,
        TaskDefs = Tasks.Select(t => t.ToDef()).ToList(),
        RequiredGroupNames = _requiredGroupsCsv
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList(),
    };
}
