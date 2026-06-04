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

        public void ShowNormal(string title, string body)
            => Show(new PopupData { type = PopupType.Normal, title = title, body = body });

        public void ShowWarning(string title, string body)
            => Show(new PopupData { type = PopupType.Warning, title = title, body = body });

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
    }
}