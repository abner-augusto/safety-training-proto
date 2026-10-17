using SafetyProto.Domain.Scenarios;

namespace SafetyProto.AuthoringApp.Gui.ViewModels;

/// <summary>Editable wrapper over one <see cref="DistractorDef"/> — a decoy item of an equip
/// task (see <see cref="TaskViewModel.Distractors"/>).</summary>
public sealed class DistractorViewModel : ViewModelBase
{
    private string _id;
    private string _name;
    private string _rejectionAdvice;

    public DistractorViewModel(DistractorDef def)
    {
        _id = def.Id;
        _name = def.Name;
        _rejectionAdvice = def.RejectionAdvice;
    }

    public DistractorViewModel(string id, string name, string rejectionAdvice)
    {
        _id = id;
        _name = name;
        _rejectionAdvice = rejectionAdvice;
    }

    /// <summary>Stable id the scene item's <c>distractorId</c> points at. Unique across the
    /// scenario; changing it detaches the scene item.</summary>
    public string Id { get => _id; set => SetField(ref _id, value); }

    public string Name { get => _name; set => SetField(ref _name, value); }

    /// <summary>Why the item is wrong, shown when the participant tries to equip it.</summary>
    public string RejectionAdvice { get => _rejectionAdvice; set => SetField(ref _rejectionAdvice, value); }

    public DistractorDef ToDef() => new()
    {
        Id = _id?.Trim() ?? string.Empty,
        Name = _name ?? string.Empty,
        RejectionAdvice = _rejectionAdvice ?? string.Empty,
    };
}
