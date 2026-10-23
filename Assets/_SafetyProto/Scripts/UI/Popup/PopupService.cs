using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Feedback;
using SafetyProto.Runtime.Feedback;
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

        [Header("Narração")]
        [Tooltip("Canal de voz compartilhado (o mesmo do onboarding e das DICAs). Vazio = popups sem narração.")]
        [SerializeField] private NarrationVoice narrationVoice;

        [Tooltip("Locução de cada popup narrado, pela chave que o emissor passa (NarrationKeys): " +
                 "'fase_concluida', 'inspecao_concluida', 'conecte_se_primeiro', 'distractor:<distractorId>', " +
                 "'report:<taskId>'. A narração começa quando o popup abre e para quando ele fecha.")]
        [SerializeField] private NarrationClipTable narrationClips = new();

        [Tooltip("Folga (s) após o fim da locução antes do auto-fechamento de um aviso narrado. " +
                 "O auto-fechamento nunca corta a locução.")]
        [SerializeField, Min(0f)] private float narrationCloseTail = 1f;

        /// <summary>Clip narrating the popup on screen; stopped when the popup closes.</summary>
        private AudioClip _popupClip;

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
            StopPopupNarration();
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

            var clip = ResolveNarration(data.narrationKey);
            if (clip != null && narrationVoice != null)
                data.autoCloseSeconds = NarrationTiming.AutoCloseSeconds(
                    data.autoCloseSeconds, WaitBeforeNarration(), clip.length, narrationCloseTail);

            popupPanel.Show(data);
            StartPopupNarration(clip);
        }

        private AudioClip ResolveNarration(string narrationKey)
        {
            if (string.IsNullOrEmpty(narrationKey)) return null;

            var clip = narrationClips.Find(narrationKey);
            if (clip == null)
                SafetyLog.Warning($"[PopupService] Não há narração atribuída ao popup '{narrationKey}'.", this);
            return clip;
        }

        /// <summary>Seconds a popup line will wait for the line playing now: a DICA or the onboarding
        /// is never cut, an info board or the previous popup's line is.</summary>
        private float WaitBeforeNarration()
        {
            if (narrationVoice.CanCutCurrent || narrationVoice.IsPlaying(_popupClip)) return 0f;
            return narrationVoice.RemainingSeconds;
        }

        private void StartPopupNarration(AudioClip clip)
        {
            if (narrationVoice == null) return;

            // The popup this one replaced takes its line with it; another surface's line is left alone.
            var previous = _popupClip;
            _popupClip = clip;

            if (clip == null)
            {
                narrationVoice.StopIfCurrent(previous);
                return;
            }

            if (narrationVoice.IsPlaying(previous))
                narrationVoice.Play(clip);
            else
                narrationVoice.PlayAfterCurrent(clip);
        }

        private void StopPopupNarration()
        {
            if (narrationVoice != null) narrationVoice.StopIfCurrent(_popupClip);
            _popupClip = null;
        }

        private Sprite[] FindIllustration(string title)
        {
            if (illustrationsByTitle == null || string.IsNullOrEmpty(title)) return null;
            foreach (var entry in illustrationsByTitle)
                if (entry.title == title) return entry.frames;
            return null;
        }

        public bool IsDisplayed => popupPanel != null && popupPanel.gameObject.activeSelf;

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

        public void ShowWarning(string title, string body, float autoCloseSeconds, string reasonCode = "",
                                string narrationKey = null)
            => Show(new PopupData { type = PopupType.Warning, title = title, body = body, autoCloseSeconds = autoCloseSeconds,
                                    narrationKey = narrationKey },
                    reasonCode);

        public void ShowInteractive(string title, string body, string buttonLabel, UnityAction onAction,
                                    string narrationKey = null)
        {
            var data = new PopupData
            {
                type              = PopupType.Interactive,
                title             = title,
                body              = body,
                actionButtonLabel = buttonLabel,
                onActionPressed   = new UnityEvent(),
                narrationKey      = narrationKey
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
                               UnityAction<IReportOption> onChosen, string narrationKey = null)
        {
            var data = new PopupData
            {
                type = PopupType.Interactive,
                title = title,
                body = body,
                choiceOptions = options,
                onChoiceSelected = onChosen,
                narrationKey = narrationKey,
            };
            Show(data);
        }
    }
}