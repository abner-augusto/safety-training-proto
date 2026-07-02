namespace SafetyProto.AuthoringApp.Gui.ViewModels;

/// <summary>A single PPE checkbox in the task editor: a catalog PPE name plus whether the task requires it.</summary>
public sealed class PpeToggleViewModel : ViewModelBase
{
    private bool _isSelected;

    public PpeToggleViewModel(string name, bool isSelected)
    {
        Name = name;
        _isSelected = isSelected;
    }

    public string Name { get; }

    public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
}
