namespace SafetyProto.AuthoringApp.Gui.ViewModels;

/// <summary>Editable wrapper for one scenario-level finish-screen tip.</summary>
public sealed class FinishTipViewModel : ViewModelBase
{
    private string _text;

    public FinishTipViewModel(string text) => _text = text ?? string.Empty;

    public string Text { get => _text; set => SetField(ref _text, value); }
}
