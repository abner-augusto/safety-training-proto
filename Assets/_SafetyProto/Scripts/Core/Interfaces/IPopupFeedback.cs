namespace SafetyProto.Core.Interfaces
{
    public interface IPopupFeedback
    {
        void ShowWarning(string title, string body);
        void Hide();
    }
}
