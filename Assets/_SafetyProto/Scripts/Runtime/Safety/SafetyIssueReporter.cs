using System;
using System.Linq;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Safety;
using SafetyProto.Domain.Scoring;
using SafetyProto.Runtime.Actions;
using SafetyProto.Runtime.Feedback;
using SafetyProto.Runtime.Interaction;
using SafetyProto.Runtime.Task;
using UnityEngine;
using UnityEngine.UI;

namespace SafetyProto.Runtime.Safety
{
    /// <summary>
    /// Turns a completed gaze dwell on a hazard into a reportable action.
    ///
    /// Flow: dwell completes -> the report button materialises next to the defect and stays there
    /// for the rest of the session -> pressing it publishes the authored ActionId. When the scenario
    /// task matching that ActionId authors a report popup (<c>reportOptions</c>), the press opens it
    /// first and the chosen alternative travels with the report.
    ///
    /// The popup's title, question and buttons live only in the scenario, edited in the authoring
    /// app; the scene carries none of that copy, so there is one place to change it.
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
        [Tooltip("ActionId published by the report. Must exist in Resources/Actions/actions.json; " +
                 "the scenario task with this ActionId supplies the report popup.")]
        [SerializeField, ActionId] private string _actionId = "flag_safety_net";

        [Tooltip("Free-form context recorded with the action attempt, for session log and dashboard.")]
        [SerializeField] private string _actionContext = "gaze_dwell";

        [Header("References")]
        [Tooltip("Dwell that reveals the button when it completes.")]
        [SerializeField] private GazeDwellTarget _dwellTarget;

        [Tooltip("Button the participant presses to report. Subscribed in code — do " +
                 "not also wire its OnClick to Report(), or the popup opens twice.")]
        [SerializeField] private Button _reportButton;

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

        /// <summary>
        /// Injectable popup dependency. Falls back to the serialized provider when unset, which is
        /// how the scene wires it; tests set it directly.
        /// </summary>
        public IPopupFeedback PopupFeedback { get; set; }

        /// <summary>
        /// Injectable scenario task supplying the popup. Falls back to the session task whose
        /// expected action is this reporter's ActionId; tests set it directly because EditMode has
        /// no TaskManager.
        /// </summary>
        public ISafetyTask ReportTask { get; set; }

        /// <summary>True once the participant has filed the report.</summary>
        public bool HasReported { get; private set; }

        private bool _choiceOpen;

        private readonly RefusedAttemptTracker _refusalTracker = new RefusedAttemptTracker();
        private readonly DeferredMisclassification _misclassification = new DeferredMisclassification();
        private System.Action<ActionRefusedEventArgs> _onActionRefused;
        private System.Action<TaskEventArgs> _onTaskEvent;

        private void Awake() => HideButton();

        private void OnEnable()
        {
            if (_dwellTarget != null) _dwellTarget.Completed += HandleDwellCompleted;
            if (_reportButton != null) _reportButton.onClick.AddListener(Report);
            if (EventBus.Instance != null)
            {
                _onActionRefused ??= HandleActionRefused;
                _onTaskEvent ??= HandleTaskEvent;
                EventBus.Instance.Subscribe(_onActionRefused);
                EventBus.Instance.Subscribe(_onTaskEvent);
            }
        }

        private void OnDisable()
        {
            if (_dwellTarget != null) _dwellTarget.Completed -= HandleDwellCompleted;
            if (_reportButton != null) _reportButton.onClick.RemoveListener(Report);
            if (EventBus.Instance != null)
            {
                if (_onActionRefused != null) EventBus.Instance.Unsubscribe(_onActionRefused);
                if (_onTaskEvent != null) EventBus.Instance.Unsubscribe(_onTaskEvent);
            }
        }

        private void HandleDwellCompleted()
        {
            if (HasReported) return;

            ShowButton();

            if (_audioSource != null && _dwellCompletedSound != null)
                _audioSource.PlayOneShot(_dwellCompletedSound);

            if (_hapticManager != null)
                _hapticManager.Pulse(_dwellHapticAmplitude, _dwellHapticDuration);

            SafetyLog.Info($"[SafetyIssueReporter] Dwell completo em '{name}'; botão de reporte disponível.", this);
        }

        /// <summary>Files the report, or first opens the scenario task's report popup when it
        /// authors one. Raised by the report button.</summary>
        public void Report()
        {
            if (HasReported || _choiceOpen) return;

            var task = ResolveTask();
            var options = task?.reportOptions;
            if (options == null || options.Count == 0)
            {
                PublishReport(null);
                return;
            }

            var popup = PopupFeedback ?? _popupFeedbackProvider as IPopupFeedback;
            if (popup == null)
            {
                SafetyLog.Warning("[SafetyIssueReporter] IPopupFeedback indisponível; publicando o reporte sem classificação.", this);
                PublishReport(null);
                return;
            }

            _choiceOpen = true;

            // The button is world-space and stays pressable while the dialog is up; left visible it
            // also draws over the dialog.
            HideButton();

            popup.ShowChoice(task.reportPopupTitle, task.reportPopupBody, options, onChosen: option =>
            {
                _choiceOpen = false;
                PublishReport(option);
            });
        }

        private ISafetyTask ResolveTask() => ReportTask ?? ResolveRuntimeTask()?.TaskData;

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

                if (!chosenOption.Correct && ResolveTask() is { } task)
                {
                    // A task finished through another path will not complete again, so nothing
                    // would ever release a held charge: settle it now.
                    if (runtimeTask != null && IsFinished(runtimeTask.State)) ChargeMisclassification(task);
                    else _misclassification.Hold(task);
                }
            }

            HideButton();
        }

        /// <summary>Delegates to the pure <see cref="HazardClassificationPolicy"/> so the
        /// scoring/violation logic is unit-testable without a MonoBehaviour.</summary>
        private static bool IsFinished(TaskState state) =>
            state == TaskState.CompletedSuccess || state == TaskState.CompletedSuccessButUnsafe;

        /// <summary>Charges a held wrong choice once the reported task completes — the report
        /// was accepted.</summary>
        private void HandleTaskEvent(TaskEventArgs args)
        {
            if (args.Phase != TaskPhase.Completed) return;
            if (_misclassification.TakeOnCompleted(args.Task) is { } task) ChargeMisclassification(task);
        }

        private void ChargeMisclassification(ISafetyTask task)
        {
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
            _misclassification.Drop();
            var runtimeTask = ResolveRuntimeTask();
            if (runtimeTask != null) runtimeTask.ReportedOptionId = string.Empty;
            ShowButton();

            SafetyLog.Info(
                $"[SafetyIssueReporter] Reporte recusado ({args.ReasonCode}) em '{name}'; botão reexibido.",
                this);
        }

        public void ResetSession()
        {
            HasReported = false;
            _choiceOpen = false;
            _misclassification.Drop();
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
