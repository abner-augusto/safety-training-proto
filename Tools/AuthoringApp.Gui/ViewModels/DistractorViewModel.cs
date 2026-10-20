using SafetyProto.Domain.Scenarios;

namespace SafetyProto.AuthoringApp.Gui.ViewModels;

/// <summary>Editable wrapper over one <see cref="DistractorDef"/> — a decoy item in the
/// scenario's distractor catalog. Tasks hold references to these, so an edit here shows up
/// in every task that links it.</summary>
public sealed class DistractorViewModel : ViewModelBase
{
    private string _id;
    private string _name;
    private string _rejectionAdvice;
    private string _usedBy = string.Empty;

    public DistractorViewModel(DistractorDef def)
        : this(def.Id, def.Name, def.RejectionAdvice)
    {
    }

    public DistractorViewModel(string id, string name, string rejectionAdvice)
    {
        _id = id ?? string.Empty;
        _name = name ?? string.Empty;
        _rejectionAdvice = rejectionAdvice ?? string.Empty;
    }

    /// <summary>Stable id the scene item's <c>distractorId</c> points at. Unique across the
    /// scenario; changing it detaches the scene item.</summary>
    public string Id
    {
        get => _id;
        set { if (SetField(ref _id, value)) OnPropertyChanged(nameof(DisplayName)); }
    }

    public string Name
    {
        get => _name;
        set { if (SetField(ref _name, value)) OnPropertyChanged(nameof(DisplayName)); }
    }

    /// <summary>Why the item is wrong, shown when the participant tries to equip it.</summary>
    public string RejectionAdvice { get => _rejectionAdvice; set => SetField(ref _rejectionAdvice, value); }

    /// <summary>Names of the tasks linking this decoy, kept current by the scenario editor.</summary>
    public string UsedBy { get => _usedBy; set => SetField(ref _usedBy, value); }

    /// <summary>Label for lists and the task dropdown.</summary>
    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(_name) ? "(distrator sem nome)" : _name;
            return string.IsNullOrWhiteSpace(_id) ? name : $"{name}  ·  {_id.Trim()}";
        }
    }

    public override string ToString() => DisplayName;

    public DistractorDef ToDef() => new()
    {
        Id = _id?.Trim() ?? string.Empty,
        Name = _name ?? string.Empty,
        RejectionAdvice = _rejectionAdvice ?? string.Empty,
    };
}
