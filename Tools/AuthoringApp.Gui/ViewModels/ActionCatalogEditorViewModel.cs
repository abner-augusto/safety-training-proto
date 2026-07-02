using System.Collections.ObjectModel;
using System.Linq;
using SafetyProto.Domain.Actions;

namespace SafetyProto.AuthoringApp.Gui.ViewModels;

public sealed class ActionCatalogEditorViewModel : ViewModelBase
{
    private string _version;
    private ActionViewModel? _selectedAction;

    public ActionCatalogEditorViewModel(ActionCatalogDef catalog)
    {
        _version = catalog.Version;
        Actions = new ObservableCollection<ActionViewModel>(
            catalog.Actions.Select(a => new ActionViewModel(a)));
    }

    public string Version { get => _version; set => SetField(ref _version, value); }

    public ObservableCollection<ActionViewModel> Actions { get; }

    public ActionViewModel? SelectedAction { get => _selectedAction; set => SetField(ref _selectedAction, value); }

    public ActionViewModel AddAction()
    {
        var action = new ActionViewModel(ActionCatalogEditor.CreateDefault());
        var baseId = action.ActionId;
        var suffix = 2;
        while (Actions.Any(a => string.Equals(a.ActionId, action.ActionId, System.StringComparison.OrdinalIgnoreCase)))
        {
            action.ActionId = $"{baseId}_{suffix++}";
            action.TelemetryName = action.ActionId;
        }

        Actions.Add(action);
        SelectedAction = action;
        return action;
    }

    public void RemoveSelected()
    {
        if (SelectedAction == null) return;
        Actions.Remove(SelectedAction);
        SelectedAction = Actions.FirstOrDefault();
    }

    public ActionCatalogDef ToDef() => new()
    {
        Version = string.IsNullOrWhiteSpace(Version) ? "1" : Version.Trim(),
        Actions = Actions.Select(a => a.ToDef()).ToList(),
    };
}
