using UnityEngine.Events;

namespace SafetyProto.Core.Interfaces
{
    public interface IPopupFeedback
    {
        void ShowSuccess(string title, string body);
        void ShowWarning(string title, string body);

        /// <summary>Shows a popup with a single action button (e.g. "Continuar") that invokes
        /// <paramref name="onAction"/> when pressed.</summary>
        void ShowInteractive(string title, string body, string buttonLabel, UnityAction onAction);

        void Hide();
    }
}
