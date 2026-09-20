using System;
using System.Linq;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Safety;
using SafetyProto.Domain.Scoring;
using SafetyProto.Runtime.Feedback;
using SafetyProto.Runtime.Interaction;
using SafetyProto.Runtime.Task;
using UnityEngine;

namespace SafetyProto.Runtime.Safety
{
    /// <summary>
    /// Turns a completed gaze dwell on a hazard into a reportable action.
    ///
    /// Flow: dwell completes -> the report button materialises next to the defect and stays there
    /// for the rest of the session -> pressing it asks for confirmation -> confirming publishes the
    /// authored ActionId.
    ///
    /// The button is revealed regardless of whether the participant is wearing the PPE the task
    /// requires. TaskManager already validates requiredPPE on the attempt, and the scenario already
    /// authors a ppeAdvice explaining why the inspection has to happen with the lanyard connected —
    /// letting the attempt fail with that advice is the teaching moment. Hiding the button instead
    /// would just read as "there is nothing here".
    /// </summary>
    public class SafetyIssueReporter : MonoBehaviour, ISessionResettable
    {
        [Header("Action")]
        [Tooltip("ActionId published on confirmation. Must exist in Resources/Actions/actions.json.")]
        [SerializeField] private string _actionId = "flag_safety_net";

        [Tooltip("Free-form context recorded with the action attempt, for session log and dashboard.")]
        [SerializeField] private string _actionContext = "gaze_dwell";

        [Header("References")]
        [Tooltip("Dwell that reveals the button when it completes.")]
        [SerializeField] private GazeDwellTarget _dwellTarget;

        [Tooltip("Button the participant presses to open the confirmation. Subscribed in code — do " +
                 "not also wire its OnClick to Report(), or the popup opens twice.")]
        [SerializeField] private DualModeButton _reportButton;

        [Tooltip("Root object switched on when the dwell completes and off once the report is filed. " +
                 "Usually the button's parent, so its visuals and colliders go together.")]
        [SerializeField] private GameObject _reportButtonRoot;

        [Tooltip("Component implementing IPopupFeedback (the popup controller).")]
        [SerializeField] private MonoBehaviour _popupFeedbackProvider;

        [Tooltip("Optional. Falls back to TaskManager.Instance. Used to look up the scenario " +
                 "task matching this reporter's ActionId, for its reportOptions and popup copy.")]
        [SerializeField] private TaskManager _taskManager;

        [Header("Dwell completion feedback")]
        [Tooltip("Optional. Plays once when the dwell completes and the button appears.")]
        [SerializeField] private AudioSource _audioSource;

        [SerializeField] private AudioClip _dwellCompletedSound;

        [Tooltip("Optional. Pulses the controllers when the dwell completes.")]
        [SerializeField] private HapticManager _hapticManager;

        [SerializeField, Range(0f, 1f)] private float _dwellHapticAmplitude = 0.25f;
        [SerializeField, Range(0f, 0.5f)] private float _dwellHapticDuration = 0.06f;

        [Header("Popup copy — participant facing, Portuguese")]
        [Tooltip("Fallback only. The scenario task's reportPopupTitle/Body/ConfirmLabel/" +
                 "CancelLabel win when a task matching ActionId is found; these serve tests " +
                 "and any scene where no TaskManager is wired.")]
        [SerializeField] private string _popupTitle = "Comunicar Irregularidade";

        [TextArea(2, 4)]
        [SerializeField] private string _popupBody =
            "Você identificou uma irregularidade na tela fachadeira. Deseja comunicá-la?";

        [SerializeField] private string _confirmLabel = "Comunicar";
        [SerializeField] private string _cancelLabel = "Cancelar";

        /// <summary>
        /// Injectable popup dependency. Falls back to the serialized provider when unset, which is
        /// how the scene wires it; tests set it directly.
        /// </summary>
        public IPopupFeedback PopupFeedback { get; set; }

        /// <summary>True once the participant has confirmed the report.</summary>
        public bool HasReported { get; private set; }

        /// <summary>How many times the confirmation was opened and backed out of.</summary>
        public int CancelledReportCount { get; private set; }

        private bool _confirmationOpen;

        private readonly RefusedAttemptTracker _refusalTracker = new RefusedAttemptTracker();
        private System.Action<ActionRefusedEventArgs> _onActionRefused;

        private void Awake() => HideButton();

        private void OnEnable()
        {
            if (_dwellTarget != null) _dwellTarget.Completed += HandleDwellCompleted;
            if (_reportButton != null) _reportButton.Clicked += Report;
            if (EventBus.Instance != null)
            {
                _onActionRefused ??= HandleActionRefused;
                EventBus.Instance.Subscribe(_onActionRefused);
            }
        }

        private void OnDisable()
        {
            if (_dwellTarget != null) _dwellTarget.Completed -= HandleDwellCompleted;
            if (_reportButton != null) _reportButton.Clicked -= Report;
            if (EventBus.Instance != null && _onActionRefused != null)
                EventBus.Instance.Unsubscribe(_onActionRefused);
        }

        private void HandleDwellCompleted()
        {
            if (HasReported) return;

            ShowButton();

            // The button can appear at the edge of vision, and a blink can swallow the last frames
            // of the fill. Announce the transition on two channels the participant cannot miss.
            if (_audioSource != null && _dwellCompletedSound != null)
                _audioSource.PlayOneShot(_dwellCompletedSound);

            if (_hapticManager != null)
                _hapticManager.Pulse(_dwellHapticAmplitude, _dwellHapticDuration);

            SafetyLog.Info($"[SafetyIssueReporter] Dwell completo em '{name}'; botão de reporte disponível.", this);
        }

        /// <summary>Opens the confirmation (or, for a task with authored <c>reportOptions</c>,
        /// the classification choice). Raised by the report button.</summary>
        public void Report()
        {
            if (HasReported || _confirmationOpen) return;

            var popup = PopupFeedback ?? _popupFeedbackProvider as IPopupFeedback;
            if (popup == null)
            {
                SafetyLog.Warning("[SafetyIssueReporter] IPopupFeedback indisponível; publicando o reporte sem confirmação.", this);
                PublishReport(null);
                return;
            }

            var runtimeTask = ResolveRuntimeTask();
            var task = runtimeTask?.TaskData;
            var options = task?.reportOptions;

            _confirmationOpen = true;

            if (options != null && options.Count > 0)
            {
                string title = NonEmpty(task.reportPopupTitle, _popupTitle);
                string body = NonEmpty(task.reportPopupBody, _popupBody);

                popup.ShowChoice(title, body, options, onChosen: option =>
                {
                    _confirmationOpen = false;
                    PublishReport(option);
                });
            }
            else
            {
                string title = NonEmpty(task?.reportPopupTitle, _popupTitle);
                string body = NonEmpty(task?.reportPopupBody, _popupBody);
                string confirm = NonEmpty(task?.reportConfirmLabel, _confirmLabel);
                string cancel = NonEmpty(task?.reportCancelLabel, _cancelLabel);

                popup.ShowConfirmation(title, body, confirm, cancel,
                    onConfirm: () => { _confirmationOpen = false; PublishReport(null); },
                    onCancel: () =>
                    {
                        _confirmationOpen = false;
                        CancelledReportCount++;
                        SafetyLog.Info($"[SafetyIssueReporter] Reporte cancelado ({CancelledReportCount}x).", this);
                    });
            }
        }

        private static string NonEmpty(string authored, string fallback) =>
            string.IsNullOrWhiteSpace(authored) ? fallback : authored;

        /// <summary>The scenario task this reporter files against, found by matching
        /// <see cref="_actionId"/> against every session task's expected action — not just
        /// the pending ones, since a report can be confirmed after the task already
        /// completed via another path. Null with no TaskManager wired (e.g. a unit test).</summary>
        private RuntimeSafetyTask ResolveRuntimeTask()
        {
            var manager = _taskManager != null ? _taskManager : TaskManager.Instance;
            if (manager == null) return null;

            return manager.GetSessionTasks()
                .FirstOrDefault(t => string.Equals(t.ExpectedActionId, _actionId, StringComparison.OrdinalIgnoreCase));
        }

        private void PublishReport(IReportOption chosenOption)
        {
            if (HasReported) return;
            HasReported = true;

            string context = chosenOption != null
                ? $"{_actionContext}:option={chosenOption.Id}"
                : _actionContext;

            ActionEvents.PublishActionAttempt(
                _actionId,
                sourceId: name,
                context: context,
                position: transform.position);

            if (chosenOption != null)
            {
                var runtimeTask = ResolveRuntimeTask();
                if (runtimeTask != null) runtimeTask.ReportedOptionId = chosenOption.Id;

                if (!chosenOption.Correct) ChargeMisclassification(runtimeTask);
            }

            HideButton();
        }

        /// <summary>Delegates to the pure <see cref="HazardClassificationPolicy"/> so the
        /// scoring/violation logic is unit-testable without a MonoBehaviour.</summary>
        private void ChargeMisclassification(RuntimeSafetyTask runtimeTask)
        {
            var task = runtimeTask?.TaskData;
            var manager = _taskManager != null ? _taskManager : TaskManager.Instance;
            var group = manager?.GetCurrentGroup();
            var scoring = manager != null ? manager.Scoring : ScoringConfig.Default;

            HazardClassificationPolicy.ChargeMisclassification(
                EventBus.Instance, ScoreService.Instance, scoring, task, group, name);
        }

        /// <summary>
        /// Undoes the optimistic report when the rule engine refuses the attempt this reporter
        /// published (e.g. the group's safety precondition — the lanyard isn't connected yet) —
        /// the participant sees the warning popup but never actually filed the report, so the
        /// button must come back for another try. Matched by this reporter's own action id and
        /// source id (its GameObject name, the same value <see cref="PublishReport"/> stamps),
        /// so a refusal aimed at a different reporter is ignored. <c>waitForPopup: false</c>
        /// because reshowing a button has no world state to protect from being yanked out from
        /// under the warning.
        ///
        /// Any reason code undoes the report, not just the pending-prerequisite one this replaced:
        /// whatever the engine declined the attempt for, the report did not land, so the button
        /// has to come back. Filtering by code here would strand the button on every decline path
        /// the filter did not anticipate.
        /// </summary>
        private void HandleActionRefused(ActionRefusedEventArgs args)
        {
            if (!HasReported) return;

            _refusalTracker.Observe(args.ActionId, args.SourceId, args.ReasonCode,
                _actionId, name, waitForPopup: false, Time.time, fallbackSeconds: 0f);

            if (!_refusalTracker.TryTakeRevert(Time.time)) return;

            HasReported = false;
            ShowButton();

            SafetyLog.Info(
                $"[SafetyIssueReporter] Reporte recusado ({args.ReasonCode}) em '{name}'; botão reexibido.",
                this);
        }

        public void ResetSession()
        {
            HasReported = false;
            CancelledReportCount = 0;
            _confirmationOpen = false;
            _refusalTracker.Reset();
            if (_dwellTarget != null) _dwellTarget.ResetDwell();
            HideButton();
        }

        private void ShowButton()
        {
            if (_reportButtonRoot != null) _reportButtonRoot.SetActive(true);
        }

        private void HideButton()
        {
            if (_reportButtonRoot != null) _reportButtonRoot.SetActive(false);
        }
    }
}
