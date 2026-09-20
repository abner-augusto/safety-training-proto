using SafetyProto.Domain.Scenarios;

namespace SafetyProto.AuthoringApp.Gui.ViewModels;

/// <summary>Editable wrapper over one <see cref="ReportOptionDef"/> — a hazard-classification
/// choice offered by a task's report popup (see <see cref="TaskViewModel.ReportOptions"/>).</summary>
public sealed class ReportOptionViewModel : ViewModelBase
{
    private string _id;
    private string _label;
    private bool _correct;

    public ReportOptionViewModel(ReportOptionDef def)
    {
        _id = def.Id;
        _label = def.Label;
        _correct = def.Correct;
    }

    public ReportOptionViewModel(string id, string label, bool correct)
    {
        _id = id;
        _label = label;
        _correct = correct;
    }

    /// <summary>Stable per-option id — the analysis key. Keep it once authored; a reorder
    /// or relabel must not change it.</summary>
    public string Id { get => _id; set => SetField(ref _id, value); }

    public string Label { get => _label; set => SetField(ref _label, value); }

    public bool Correct { get => _correct; set => SetField(ref _correct, value); }

    public ReportOptionDef ToDef() => new() { Id = _id?.Trim() ?? string.Empty, Label = _label ?? string.Empty, Correct = _correct };
}
