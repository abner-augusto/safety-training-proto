using System;
using SafetyProto.Domain.Scenarios;

namespace SafetyProto.AuthoringApp.Gui.ViewModels;

/// <summary>Editable wrapper over one <see cref="ReportOptionDef"/> — a button the participant
/// can press on a task's report popup (see <see cref="TaskViewModel.ReportOptions"/>).</summary>
public sealed class ReportOptionViewModel : ViewModelBase
{
    private string _id;
    private string _label;
    private bool _correct;

    public ReportOptionViewModel(ReportOptionDef def)
        : this(def.Id, def.Label, def.Correct)
    {
    }

    public ReportOptionViewModel(string id, string label, bool correct)
    {
        _id = id ?? string.Empty;
        _label = label ?? string.Empty;
        _correct = correct;
    }

    /// <summary>Raised when this option becomes the correct one, so the task can clear the
    /// flag on its siblings — the popup accepts exactly one correct answer.</summary>
    public event Action<ReportOptionViewModel>? MarkedCorrect;

    /// <summary>Stable per-option id — the analysis key. Optional while authoring: a blank id
    /// is derived from the label on save. Keep it once authored; a reorder or relabel must not
    /// change it.</summary>
    public string Id { get => _id; set => SetField(ref _id, value); }

    public string Label { get => _label; set => SetField(ref _label, value); }

    public bool Correct
    {
        get => _correct;
        set
        {
            if (SetField(ref _correct, value) && value) MarkedCorrect?.Invoke(this);
        }
    }

    public ReportOptionDef ToDef(string id) => new() { Id = id, Label = _label ?? string.Empty, Correct = _correct };
}
