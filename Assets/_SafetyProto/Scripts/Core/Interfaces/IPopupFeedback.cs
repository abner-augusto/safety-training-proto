using UnityEngine.Events;

namespace SafetyProto.Core.Interfaces
{
    public interface IPopupFeedback
    {
        void ShowSuccess(string title, string body);
        void ShowWarning(string title, string body);

        /// <summary>Shows a non-interactive notice that dismisses itself after
        /// <paramref name="autoCloseSeconds"/>. Unlike the other Show* methods this one does NOT
        /// pause the session: there is nothing for the participant to answer, so pausing would
        /// only add noise to the session log — including when no session is running at all
        /// (e.g. recentering from the menu).</summary>
        void ShowTransient(string title, string body, float autoCloseSeconds);

        /// <summary>Shows a popup with a single action button (e.g. "Continuar") that invokes
        /// <paramref name="onAction"/> when pressed.</summary>
        void ShowInteractive(string title, string body, string buttonLabel, UnityAction onAction);

        /// <summary>Shows a confirmation popup with a confirm and a cancel button. Reusable for any
        /// "are you sure?" flow (quit, reset, discard, …). The popup auto-closes on either choice;
        /// <paramref name="onConfirm"/> runs on confirm, <paramref name="onCancel"/> on cancel/dismiss.</summary>
        void ShowConfirmation(string title, string body, string confirmLabel, string cancelLabel,
                              UnityAction onConfirm, UnityAction onCancel = null);

        void Hide();
    }
}
