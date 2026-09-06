using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.UI
{
    public class PopupService : MonoBehaviour, IPopupFeedback
    {
        public static PopupService Instance { get; private set; }

        [SerializeField] private PopupPanel popupPanel;

        private bool _sessionPausedByUs;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            if (popupPanel == null)
            {
                SafetyLog.Warning("[PopupService] popupPanel not assigned in Inspector.", this);
                return;
            }

            // Resume the session whenever the panel hides, including the close button,
            // which calls PopupPanel.Hide() directly and never routes through Hide() here.
            popupPanel.Hidden += OnPanelHidden;

            if (popupPanel.gameObject.activeSelf)
            {
                popupPanel.gameObject.SetActive(false);
                SafetyLog.Info("[PopupService] PopupCanvas deactivated on Start().", this);
            }
        }

        private void OnDestroy()
        {
            if (popupPanel != null)
                popupPanel.Hidden -= OnPanelHidden;

            if (Instance == this)
                Instance = null;
        }

        private void OnPanelHidden()
        {
            if (_sessionPausedByUs)
            {
                SessionEvents.RaiseSessionResumed();
                _sessionPausedByUs = false;
            }

            // Announced for every close (button, dismiss, auto-close) so gameplay objects can
            // wait for a warning to have been read. Deliberately not tied to the pause pair —
            // a transient notice pauses nothing but still closes.
            EventBus.Instance?.Publish(new PopupClosedEventArgs());
        }

        public void Show(PopupData data)
        {
            if (popupPanel == null) return;

            if (!_sessionPausedByUs)
            {
                SessionEvents.RaiseSessionPaused();
                _sessionPausedByUs = true;
            }

            popupPanel.Show(data);
        }

        public void Hide()
        {
            if (popupPanel == null) return;

            // PopupPanel.Hide() raises Hidden, which OnPanelHidden uses to resume the session.
            popupPanel.Hide();
        }

        public void ShowSuccess(string title, string body)
            => Show(new PopupData { type = PopupType.Normal, title = title, body = body });

        /// <summary>
        /// Transient notice: shows the panel directly instead of going through <see cref="Show"/>,
        /// so no SessionPaused/SessionResumed pair is raised. The participant has nothing to
        /// answer here, and the recenter case fires it with no session running, where that pair
        /// would land in the log with an empty session id.
        /// </summary>
        public void ShowTransient(string title, string body, float autoCloseSeconds)
        {
            if (popupPanel == null) return;

            // There is a single shared panel, so showing this would replace whatever is already
            // up — including the name-entry dialog, which is open during exactly the case the
            // recenter targets. A courtesy notice must never destroy a prompt the participant
            // still has to answer, so yield instead.
            if (popupPanel.IsVisible)
            {
                SafetyLog.Info($"[PopupService] Aviso '{title}' suprimido — outro popup já está visível.", this);
                return;
            }

            popupPanel.Show(new PopupData
            {
                type = PopupType.Normal,
                title = title,
                body = body,
                autoCloseSeconds = autoCloseSeconds > 0f ? autoCloseSeconds : 5f,
            });
        }

        public void ShowNormal(string title, string body)
            => ShowNormal(title, body, 0f);

        public void ShowNormal(string title, string body, float autoCloseSeconds)
            => Show(new PopupData { type = PopupType.Normal, title = title, body = body, autoCloseSeconds = autoCloseSeconds });

        public void ShowWarning(string title, string body)
            => ShowWarning(title, body, 0f);

        public void ShowWarning(string title, string body, float autoCloseSeconds)
            => Show(new PopupData { type = PopupType.Warning, title = title, body = body, autoCloseSeconds = autoCloseSeconds });

        public void ShowInteractive(string title, string body, string buttonLabel, UnityAction onAction)
        {
            var data = new PopupData
            {
                type              = PopupType.Interactive,
                title             = title,
                body              = body,
                actionButtonLabel = buttonLabel,
                onActionPressed   = new UnityEvent()
            };
            data.onActionPressed.AddListener(onAction);
            Show(data);
        }

        public void ShowConfirmation(string title, string body, string confirmLabel, string cancelLabel,
                                     UnityAction onConfirm, UnityAction onCancel = null)
        {
            var data = new PopupData
            {
                type              = PopupType.Interactive,
                title             = title,
                body              = body,
                actionButtonLabel = confirmLabel,
                onActionPressed   = new UnityEvent(),
                showSkipButton    = true,
                skipButtonLabel   = cancelLabel,
                onSkipPressed     = new UnityEvent(),
            };

            // Both choices close the popup first, then run the caller's callback so a callback
            // that opens another popup isn't immediately hidden by this one.
            data.onActionPressed.AddListener(() => { Hide(); onConfirm?.Invoke(); });
            data.onSkipPressed.AddListener(() => { Hide(); onCancel?.Invoke(); });
            Show(data);
        }
    }
}