using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SafetyProto.Domain.Actions;
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
    private DistractorViewModel? _selectedDistractor;

    public ScenarioEditorViewModel(ScenarioDef def, CapabilityCatalog? catalog, ActionCatalogDef? actionCatalog = null)
    {
        _name = def.Name;
        _participantId = def.ParticipantId;
        _script = def.Script ?? new List<ScriptStepDef>();
        FinishTips = new ObservableCollection<FinishTipViewModel>(
            (def.FinishTips ?? new List<string>()).Select(tip => new FinishTipViewModel(tip)));

        // Before the groups: each task resolves its distractorIds against this catalog.
        Distractors = new ObservableCollection<DistractorViewModel>(
            (def.Distractors ?? new List<DistractorDef>()).Select(d => new DistractorViewModel(d)));

        BuildOptions(def, catalog, actionCatalog);

        Groups = new ObservableCollection<GroupViewModel>(
            def.Groups.Select(g => new GroupViewModel(g, this)));

        RefreshDistractorUsage();
        _selectedDistractor = Distractors.FirstOrDefault();
    }

    public ObservableCollection<GroupViewModel> Groups { get; }
    public ObservableCollection<FinishTipViewModel> FinishTips { get; }

    /// <summary>The scenario's decoy catalog, edited on its own tab and linked from equip tasks.</summary>
    public ObservableCollection<DistractorViewModel> Distractors { get; }

    public DistractorViewModel? SelectedDistractor
    {
        get => _selectedDistractor;
        set { if (SetField(ref _selectedDistractor, value)) RefreshDistractorUsage(); }
    }

    public DistractorViewModel AddDistractor()
    {
        var distractor = new DistractorViewModel(string.Empty, string.Empty, string.Empty);
        Distractors.Add(distractor);
        SelectedDistractor = distractor;
        return distractor;
    }

    /// <summary>Removes the decoy from the catalog and from every task that offered it.</summary>
    public void RemoveSelectedDistractor()
    {
        if (_selectedDistractor is not { } doomed) return;

        foreach (var task in AllTasks()) task.LinkedDistractors.Remove(doomed);
        Distractors.Remove(doomed);
        SelectedDistractor = Distractors.FirstOrDefault();
    }

    /// <summary>
    /// Resolves a task's distractor id against the catalog. An id with no entry gets a blank
    /// one registered under it, so a dangling link survives the round trip and shows up on the
    /// Distratores tab (and in validation) instead of vanishing on save.
    /// </summary>
    public DistractorViewModel FindOrRegisterDistractor(string id)
    {
        var wanted = id?.Trim() ?? string.Empty;
        var found = Distractors.FirstOrDefault(d => string.Equals(d.Id?.Trim(), wanted, StringComparison.Ordinal));
        if (found != null) return found;

        var placeholder = new DistractorViewModel(wanted, string.Empty, string.Empty);
        Distractors.Add(placeholder);
        return placeholder;
    }

    /// <summary>Recomputes each decoy's "used by" caption from the current task links.</summary>
    public void RefreshDistractorUsage()
    {
        var tasks = AllTasks().ToList();
        foreach (var distractor in Distractors)
        {
            var names = tasks.Where(t => t.LinkedDistractors.Contains(distractor)).Select(t => t.TaskName).ToList();
            distractor.UsedBy = names.Count == 0 ? "nenhuma tarefa" : string.Join(", ", names);
        }
    }

    private IEnumerable<TaskViewModel> AllTasks() => Groups.SelectMany(g => g.Tasks);

    /// <summary>Action dropdown source: equip-set sentinel + catalog ids ∪ ids already used.</summary>
    public List<string> ActionOptions { get; private set; } = new();

    /// <summary>PPE checkbox source: catalog PPE ∪ PPE already used.</summary>
    public List<string> PpeOptionNames { get; private set; } = new();

    public string Name { get => _name; set => SetField(ref _name, value); }
    public string ParticipantId { get => _participantId; set => SetField(ref _participantId, value); }

    public void AddFinishTip() => FinishTips.Add(new FinishTipViewModel(string.Empty));

    public void RemoveFinishTip(FinishTipViewModel tip)
    {
        if (tip != null) FinishTips.Remove(tip);
    }

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

        RefreshDistractorUsage();
    }

    public ScenarioDef ToDef() => new()
    {
        Name = _name,
        ParticipantId = _participantId,
        Groups = Groups.Select(g => g.ToDef()).ToList(),
        FinishTips = FinishTips.Select(tip => tip.Text ?? string.Empty).ToList(),
        Distractors = Distractors.Select(d => d.ToDef()).ToList(),
        Script = _script,
    };

    private void BuildOptions(ScenarioDef def, CapabilityCatalog? catalog, ActionCatalogDef? actionCatalog)
    {
        var actions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var ppe = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        if (catalog != null)
        {
            foreach (var a in catalog.ActionIds ?? Enumerable.Empty<string>()) actions.Add(a);
            foreach (var p in catalog.PpeTypes ?? Enumerable.Empty<string>()) ppe.Add(p);
        }

        if (actionCatalog != null)
        {
            foreach (var a in actionCatalog.Actions ?? Enumerable.Empty<ActionDef>())
            {
                if (!string.IsNullOrWhiteSpace(a.ActionId)) actions.Add(a.ActionId.Trim());
            }
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
