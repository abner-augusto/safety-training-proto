using System;
using System.Collections;
using System.Collections.Generic;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using TMPro;
using UnityEngine;

namespace SafetyProto.Runtime.Practice
{
    /// <summary>
    /// The practice area between onboarding steps 4 and 5: the participant fades to a neutral
    /// space and tries each interaction of the training on primitive props, with the session clock
    /// held. Nothing there scores or advances a task; the props are practice-only components.
    /// It closes on "Continuar", when the countdown runs out, or on the evaluator's command, and
    /// fades the participant back to where the training starts.
    /// </summary>
    public sealed class PracticeArea : MonoBehaviour
    {
        [Serializable]
        public sealed class Interaction
        {
            [Tooltip("Stable id recorded in the log's PracticeEnded 'tried' list.")]
            public string id;
            [Tooltip("Checklist line shown on the panel.")]
            public string label;
        }

        /// <summary>The area while it is open or opening; null otherwise.</summary>
        public static PracticeArea Active { get; private set; }

        [SerializeField] private RecenterService recenterService;
        [Tooltip("Floor point at the centre of the practice area, facing the panel.")]
        [SerializeField] private Transform practiceAnchor;
        [Tooltip("Where the participant goes back to: the training's start point.")]
        [SerializeField] private Transform returnAnchor;
        [Tooltip("Shown only while the area is open: the area itself and the practice slots on the body.")]
        [SerializeField] private GameObject[] shownWhileOpen;

        [SerializeField, Range(30f, 600f)] private float durationSeconds = 150f;
        [SerializeField] private Interaction[] interactions;

        [Header("Panel")]
        [SerializeField] private TMP_Text countdownText;
        [SerializeField] private TMP_Text checklistText;

        [Header("Fade")]
        [SerializeField, Min(0f)] private float fadeOutSeconds = 0.4f;
        [SerializeField, Min(0f)] private float holdBlackSeconds = 0.3f;
        [SerializeField, Min(0f)] private float fadeInSeconds = 0.4f;

        private readonly List<string> _tried = new List<string>();
        private readonly List<IPracticeResettable> _resettables = new List<IPracticeResettable>();
        private Action _onClosed;
        private float _openedAt;
        private float _secondsLeft;
        private bool _counting;
        private bool _closing;
        private string _pendingClose;
        private int _shownSecond = -1;

        /// <summary>Whether the participant is in the area (or on the way in or out).</summary>
        public bool IsOpen => Active == this;

        public float SecondsLeft => _secondsLeft;

        private void Awake() => SetShown(false);

        private void OnDisable()
        {
            if (Active != this) return;
            SessionPause.Release(PauseSource.Practice);
            Active = null;
        }

        /// <summary>
        /// Opens the area; <paramref name="onClosed"/> runs once the participant is back at the
        /// start point. Runs it at once when a scene reference is missing, so onboarding never
        /// stalls on it. The session simulator starts sessions without onboarding and never gets here.
        /// </summary>
        public void Begin(Action onClosed)
        {
            if (IsOpen) return;
            if (recenterService == null || practiceAnchor == null || returnAnchor == null)
            {
                SafetyLog.Warning("[PracticeArea] Área de prática não aberta: referência ausente na cena.", this);
                onClosed?.Invoke();
                return;
            }

            Active = this;
            _onClosed = onClosed;
            _tried.Clear();
            _pendingClose = null;
            _closing = false;
            _secondsLeft = durationSeconds;
            _shownSecond = -1;
            SessionPause.Hold(PauseSource.Practice);
            InteractionEvents.RaisePracticeStarted();
            StartCoroutine(OpenRoutine());
        }

        /// <summary>The panel's "Continuar" button.</summary>
        public void Continue() => Close("continue");

        /// <summary>Ends the practice: <c>continue</c>, <c>timeout</c> or <c>evaluator</c>.</summary>
        public void Close(string reason)
        {
            if (!IsOpen || _closing) return;
            if (!_counting)
            {
                _pendingClose = reason;
                return;
            }
            StartCoroutine(CloseRoutine(reason));
        }

        /// <summary>A prop reports that the participant did its interaction.</summary>
        public void MarkDone(string interactionId)
        {
            if (!IsOpen || string.IsNullOrEmpty(interactionId) || _tried.Contains(interactionId)) return;
            _tried.Add(interactionId);
            RefreshChecklist();
        }

        private IEnumerator OpenRoutine()
        {
            yield return recenterService.RecenterTo(practiceAnchor, Fade(() =>
            {
                CollectResettables();
                foreach (var prop in _resettables) prop.ResetForPractice();
                SetShown(true);
                RefreshChecklist();
                RefreshCountdown();
            }));

            _openedAt = Time.time;
            _counting = true;
            if (_pendingClose != null) Close(_pendingClose);
        }

        private IEnumerator CloseRoutine(string reason)
        {
            _closing = true;
            _counting = false;
            float seconds = Time.time - _openedAt;
            InteractionEvents.RaisePracticeEnded(reason, seconds, _tried.ToArray());
            SafetyLog.Info($"[PracticeArea] Área de prática encerrada ({reason}) após {seconds:0} s.", this);

            yield return recenterService.RecenterTo(returnAnchor, Fade(() =>
            {
                foreach (var prop in _resettables) prop.ResetForPractice();
                SetShown(false);
            }));

            SessionPause.Release(PauseSource.Practice);
            Active = null;
            _closing = false;
            var onClosed = _onClosed;
            _onClosed = null;
            onClosed?.Invoke();
        }

        private void Update()
        {
            if (!_counting) return;
            _secondsLeft = Mathf.Max(0f, durationSeconds - (Time.time - _openedAt));
            RefreshCountdown();
            if (_secondsLeft <= 0f) Close("timeout");
        }

        private RecenterOptions Fade(Action onBlackout) => new RecenterOptions
        {
            FadeOutDuration = fadeOutSeconds,
            HoldBlackDuration = holdBlackSeconds,
            FadeInDuration = fadeInSeconds,
            SuspendPoseBroadcast = true,
            LocomotorHandling = LocomotorMode.ToggleEnabled,
            OnBlackout = onBlackout
        };

        private void CollectResettables()
        {
            _resettables.Clear();
            foreach (var root in shownWhileOpen)
                if (root != null)
                    _resettables.AddRange(root.GetComponentsInChildren<IPracticeResettable>(true));
        }

        private void SetShown(bool shown)
        {
            if (shownWhileOpen == null) return;
            foreach (var root in shownWhileOpen)
                if (root != null) root.SetActive(shown);
        }

        private void RefreshCountdown()
        {
            if (countdownText == null) return;
            int second = Mathf.CeilToInt(_secondsLeft);
            if (second == _shownSecond) return;
            _shownSecond = second;
            countdownText.text = $"Tempo restante: {second / 60}:{second % 60:00}";
        }

        private void RefreshChecklist()
        {
            if (checklistText == null || interactions == null) return;
            var sb = new System.Text.StringBuilder();
            foreach (var interaction in interactions)
            {
                bool done = _tried.Contains(interaction.id);
                sb.Append(done ? "<color=#3DA35D>✔</color> " : "<color=#9AA0A6>○</color> ");
                sb.AppendLine(interaction.label);
            }
            checklistText.text = sb.ToString();
        }
    }

    /// <summary>A practice prop that goes back to its starting state when the area opens or closes.</summary>
    public interface IPracticeResettable
    {
        void ResetForPractice();
    }
}
