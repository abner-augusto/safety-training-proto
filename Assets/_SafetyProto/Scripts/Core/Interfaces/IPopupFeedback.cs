namespace SafetyProto.Core.Interfaces
{
    public interface IPopupFeedback
    {
        void ShowSuccess(string title, string body);
        void ShowWarning(string title, string body);
        void Hide();
    }
}
