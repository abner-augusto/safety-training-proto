using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SafetyProto.Domain.Capabilities;
using SafetyProto.Domain.Scenarios;

namespace SafetyProto.AuthoringApp.Gui.ViewModels;

/// <summary>
/// Editable model of a whole scenario. Wraps a <see cref="ScenarioDef"/> as a tree of
/// <see cref="GroupViewModel"/>/<see cref="TaskViewModel"/>. Action/PPE option lists are
/// derived once from the (optional) capability catalog unioned with whatever the scenario
/// already uses, so nothing is lost when authoring without a catalog. <see cref="ToDef"/>
/// rebuilds a plain <see cref="ScenarioDef"/> for validation and serialization.
/// </summary>
public sealed class ScenarioEditorViewModel : ViewModelBase
{
    public static readonly string[] ExecutionModeNames = { "Sequential", "FreeOrder" };

    // Preserved verbatim across the edit cycle (Unity ignores it; the CLI harness consumes it).
    private readonly List<ScriptStepDef> _script;

    private string _name;
    private string _participantId;
    private object? _selectedNode;

    public ScenarioEditorViewModel(ScenarioDef def, CapabilityCatalog? catalog)
    {
        _name = def.Name;
        _participantId = def.ParticipantId;
        _script = def.Script ?? new List<ScriptStepDef>();

        BuildOptions(def, catalog);

        Groups = new ObservableCollection<GroupViewModel>(
            def.Groups.Select(g => new GroupViewModel(g, this)));
    }

    public ObservableCollection<GroupViewModel> Groups { get; }

    /// <summary>Action dropdown source: equip-set sentinel + catalog ids ∪ ids already used.</summary>
    public List<string> ActionOptions { get; private set; } = new();

    /// <summary>PPE checkbox source: catalog PPE ∪ PPE already used.</summary>
    public List<string> PpeOptionNames { get; private set; } = new();

    public string Name { get => _name; set => SetField(ref _name, value); }
    public string ParticipantId { get => _participantId; set => SetField(ref _participantId, value); }

    /// <summary>Two-way bound to the TreeView selection; drives the detail panel.</summary>
    public object? SelectedNode { get => _selectedNode; set => SetField(ref _selectedNode, value); }

    public GroupViewModel AddGroup()
    {
        var group = new GroupViewModel(new TaskGroupDef { groupName = "Novo Grupo" }, this);
        Groups.Add(group);
        SelectedNode = group;
        return group;
    }

    /// <summary>Adds a task to the selected group (or the selected task's group; falls back to the first group).</summary>
    public TaskViewModel? AddTask()
    {
        var group = SelectedNode as GroupViewModel
                    ?? (SelectedNode as TaskViewModel)?.Group
                    ?? Groups.FirstOrDefault();
        if (group == null) return null;

        var task = group.AddTask();
        SelectedNode = task;
        return task;
    }

    public void RemoveSelected()
    {
        switch (SelectedNode)
        {
            case TaskViewModel task:
                task.Group.Tasks.Remove(task);
                SelectedNode = null;
                break;
            case GroupViewModel group:
                Groups.Remove(group);
                SelectedNode = null;
                break;
        }
    }

    public ScenarioDef ToDef() => new()
    {
        Name = _name,
        ParticipantId = _participantId,
        Groups = Groups.Select(g => g.ToDef()).ToList(),
        Script = _script,
    };

    private void BuildOptions(ScenarioDef def, CapabilityCatalog? catalog)
    {
        var actions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var ppe = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        if (catalog != null)
        {
            foreach (var a in catalog.ActionIds ?? Enumerable.Empty<string>()) actions.Add(a);
            foreach (var p in catalog.PpeTypes ?? Enumerable.Empty<string>()) ppe.Add(p);
        }

        foreach (var group in def.Groups)
        {
            foreach (var task in group.TaskDefs)
            {
                if (!string.IsNullOrWhiteSpace(task.ActionId)) actions.Add(task.ActionId);
                foreach (var name in task.RequiredPpeNames) ppe.Add(name);
            }
        }

        ActionOptions = new List<string> { TaskViewModel.NoActionLabel };
        ActionOptions.AddRange(actions);
        PpeOptionNames = ppe.ToList();
    }
}
