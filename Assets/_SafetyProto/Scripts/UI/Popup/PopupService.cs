using System.Collections.Generic;
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

        [System.Serializable]
        private struct TitleIllustration
        {
            public string title;
            public Sprite[] frames;
        }

        [Tooltip("Ilustração aplicada a qualquer popup com este título exato, para os avisos " +
                 "abertos por sistemas que não conhecem sprites (EPI incorreto, ordem incorreta…).")]
        [SerializeField] private TitleIllustration[] illustrationsByTitle;

        /// <summary>Reason code of the popup currently shown, echoed on close so an emitter
        /// waiting behind a warning can match it back to its own refusal. Empty for a popup
        /// opened for anything other than a refusal.</summary>
        private string _currentReasonCode = string.Empty;

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

            popupPanel.Hidden += OnPanelHidden;
            popupPanel.Shown += OnPanelShown;

            if (popupPanel.gameObject.activeSelf)
            {
                popupPanel.gameObject.SetActive(false);
                SafetyLog.Info("[PopupService] PopupCanvas deactivated on Start().", this);
            }
        }

        private void OnDestroy()
        {
            if (popupPanel != null)
            {
                popupPanel.Hidden -= OnPanelHidden;
                popupPanel.Shown -= OnPanelShown;
            }

            if (Instance == this)
                Instance = null;
        }

        private void OnPanelHidden()
        {
            SessionPause.Release(PauseSource.Popup);

            PopupEvents.RaisePopupClosed(_currentReasonCode);
            _currentReasonCode = string.Empty;
        }

        /// <summary>Mirrors <see cref="OnPanelHidden"/> on the way up, so systems that must not
        /// reference the UI assembly can bracket the panel's lifetime (hand overlay on the
        /// popup camera) without watching the session pause it also raises.</summary>
        private void OnPanelShown()
        {
            PopupEvents.RaisePopupShown();
        }

        public void Show(PopupData data) => Show(data, reasonCode: string.Empty);

        public void Show(PopupData data, string reasonCode)
        {
            if (popupPanel == null) return;

            _currentReasonCode = reasonCode ?? string.Empty;

            SessionPause.Hold(PauseSource.Popup);

            if (data.illustrationFrames == null || data.illustrationFrames.Length == 0)
                data.illustrationFrames = FindIllustration(data.title);

            popupPanel.Show(data);
        }

        private Sprite[] FindIllustration(string title)
        {
            if (illustrationsByTitle == null || string.IsNullOrEmpty(title)) return null;
            foreach (var entry in illustrationsByTitle)
                if (entry.title == title) return entry.frames;
            return null;
        }

        public void Hide()
        {
            if (popupPanel == null) return;

            popupPanel.Hide();
        }

        public void ShowSuccess(string title, string body)
            => Show(new PopupData { type = PopupType.Normal, title = title, body = body });

        /// <summary>
        /// Shows a transient notice only when no popup is visible, returning false when it is dropped
        /// (the shared panel may hold a prompt the participant still has to answer, such as name entry).
        /// Shows the panel directly instead of going through <see cref="Show"/>,
        /// so no SessionPaused/SessionResumed pair is raised. The participant has nothing to
        /// answer here, and the recenter case fires it with no session running, where that pair
        /// would land in the log with an empty session id.
        /// </summary>
        public bool TryShowIfIdle(string title, string body, float autoCloseSeconds)
        {
            if (popupPanel == null) return false;

            if (popupPanel.IsVisible)
            {
                SafetyLog.Info($"[PopupService] Aviso '{title}' suprimido — outro popup já está visível.", this);
                return false;
            }

            popupPanel.Show(new PopupData
            {
                type = PopupType.Normal,
                title = title,
                body = body,
                autoCloseSeconds = autoCloseSeconds > 0f ? autoCloseSeconds : 5f,
            });
            return true;
        }

        public void ShowNormal(string title, string body)
            => ShowNormal(title, body, 0f);

        public void ShowNormal(string title, string body, float autoCloseSeconds)
            => Show(new PopupData { type = PopupType.Normal, title = title, body = body, autoCloseSeconds = autoCloseSeconds });

        public void ShowWarning(string title, string body)
            => ShowWarning(title, body, 0f);

        public void ShowWarning(string title, string body, float autoCloseSeconds, string reasonCode = "")
            => Show(new PopupData { type = PopupType.Warning, title = title, body = body, autoCloseSeconds = autoCloseSeconds },
                    reasonCode);

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

            data.onActionPressed.AddListener(() => CloseThen(onConfirm));
            data.onSkipPressed.AddListener(() => CloseThen(onCancel));
            Show(data);
        }

        private void CloseThen(UnityAction callback)
        {
            Hide();
            callback?.Invoke();
        }

        public void ShowChoice(string title, string body, IReadOnlyList<IReportOption> options,
                               UnityAction<IReportOption> onChosen)
        {
            var data = new PopupData
            {
                type = PopupType.Interactive,
                title = title,
                body = body,
                choiceOptions = options,
                onChoiceSelected = onChosen,
            };
            Show(data);
        }
    }
}