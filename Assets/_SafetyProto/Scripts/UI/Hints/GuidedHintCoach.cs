using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Feedback;
using SafetyProto.Runtime.Feedback;
using SafetyProto.Runtime.PPE;
using SafetyProto.Runtime.Task;
using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Guided-mode help for a participant stuck on a task (see <see cref="HintLadder"/>): after
    /// <see cref="pulseAfterSeconds"/> without progress the next object and its slot pulse, after
    /// <see cref="narrateAfterSeconds"/> a voice line says the step, after
    /// <see cref="alertAfterSeconds"/> the evaluator's dashboard raises the stuck alert. Progress is
    /// a task advancing, a PPE item put on, or a grab of one of the current task's objects; any of
    /// them cancels the help. Evaluation mode never gets hints, and a participant who gets it right
    /// alone never sees one. Every level reached is published as <see cref="HintShownEventArgs"/>.
    /// </summary>
    public sealed class GuidedHintCoach : MonoBehaviour
    {
        [SerializeField] private NarrationVoice narrationVoice;
        [Tooltip("Locução de cada passo, pela chave 'hint:<taskId>' (NarrationKeys.ForTaskHint).")]
        [SerializeField] private NarrationClipTable hintClips = new();
        [SerializeField, Min(1f)] private float pulseAfterSeconds = 15f;
        [SerializeField, Min(1f)] private float narrateAfterSeconds = 30f;
        [SerializeField, Min(1f)] private float alertAfterSeconds = 60f;

        private HintLadder _ladder;
        private PPEManager _ppe;
        private string _taskId;
        private readonly List<AttentionTargetInfo> _targets = new List<AttentionTargetInfo>();
        private readonly List<GameObject> _pulsing = new List<GameObject>();

        private System.Action<TaskEventArgs> _onTask;
        private System.Action<PPEStateChangedEventArgs> _onPpe;
        private System.Action<ObjectGrabbedEventArgs> _onGrab;

        private void Start()
        {
            _ladder = new HintLadder(pulseAfterSeconds, narrateAfterSeconds, alertAfterSeconds);
            if (narrationVoice == null) narrationVoice = FindAnyObjectByType<NarrationVoice>();
            _ppe = FindAnyObjectByType<PPEManager>();

            var bus = EventBus.Instance;
            if (bus == null) return;
            _onTask = _ => Progress();
            _onPpe = args => { if (args.IsWearing) Progress(); };
            _onGrab = args => { if (IsCurrentTarget(args.Target)) Progress(); };
            bus.Subscribe(_onTask);
            bus.Subscribe(_onPpe);
            bus.Subscribe(_onGrab);
        }

        private void OnDestroy()
        {
            var bus = EventBus.Instance;
            if (bus == null || _onTask == null) return;
            bus.Unsubscribe(_onTask);
            bus.Unsubscribe(_onPpe);
            bus.Unsubscribe(_onGrab);
        }

        private void Update()
        {
            var task = TaskManager.Instance != null ? TaskManager.Instance.CurrentRuntimeTask?.TaskData : null;
            string taskId = task?.id;
            if (taskId != _taskId)
            {
                _taskId = taskId;
                Progress();
            }

            if (task == null || SessionModeState.Current != SessionMode.Guided || SessionPause.IsPaused) return;

            int level = _ladder.Advance(Time.deltaTime);
            if (level == 0) return;

            // The log counts hints as an analysis covariate: only help that reached the participant
            // (or the evaluator, at level 3) is a hint.
            bool delivered = level switch
            {
                1 => PulseTargets(task),
                2 => Narrate(task),
                _ => true,
            };
            if (delivered)
                InteractionEvents.RaiseHintShown(level, task.id, task.taskName, _ladder.SecondsWithoutProgress);
        }

        private void Progress()
        {
            _ladder?.Progress();
            StopPulses();
        }

        private bool PulseTargets(ISafetyTask task)
        {
            var service = AttentionService.Instance;
            if (service == null) return false;
            CollectTargets(task, service);
            foreach (var target in _targets)
            {
                var host = service.HostOf(target.Id);
                if (host == null) continue;
                service.Hold(host);
                _pulsing.Add(host);
            }
            return _pulsing.Count > 0;
        }

        private void StopPulses()
        {
            var service = AttentionService.Instance;
            foreach (var host in _pulsing)
                if (host != null) service?.Release(host);
            _pulsing.Clear();
        }

        private bool Narrate(ISafetyTask task)
        {
            var clip = hintClips.Find(NarrationKeys.ForTaskHint(task.id));
            if (clip == null || narrationVoice == null) return false;
            narrationVoice.PlayAfterCurrent(clip);
            return true;
        }

        /// <summary>
        /// The current task's objects: for an action task, every target keyed by its action (the
        /// piece and its socket, the lanyard and the anchor, the object to report); for an equip
        /// task, each PPE item still to put on and the body slot that takes it.
        /// </summary>
        private void CollectTargets(ISafetyTask task, AttentionService service)
        {
            _targets.Clear();
            string action = task.ResolveExpectedActionId();
            if (!string.IsNullOrEmpty(action))
            {
                foreach (var kind in new[] { AttentionTargetKind.ScaffoldPiece, AttentionTargetKind.ScaffoldSocket,
                                             AttentionTargetKind.ActionObject, AttentionTargetKind.Lanyard, AttentionTargetKind.Anchor })
                    _targets.AddRange(service.FindByKey(kind, action));
                return;
            }

            foreach (var type in task.requiredPPE)
            {
                if (_ppe != null && _ppe.IsWearing(type)) continue;
                _targets.AddRange(service.FindByKey(AttentionTargetKind.Ppe, type.ToString()));
                foreach (var target in service.Targets)
                    if (target.Kind == AttentionTargetKind.BodySlot && AttentionTargets.SlotTakes(target, type))
                        _targets.Add(target);
            }
        }

        private bool IsCurrentTarget(string objectName)
        {
            var service = AttentionService.Instance;
            var task = TaskManager.Instance != null ? TaskManager.Instance.CurrentRuntimeTask?.TaskData : null;
            if (service == null || task == null || string.IsNullOrEmpty(objectName)) return false;
            CollectTargets(task, service);
            foreach (var target in _targets)
                if (target.Kind != AttentionTargetKind.BodySlot && target.Kind != AttentionTargetKind.ScaffoldSocket
                    && target.Id.EndsWith(":" + objectName, System.StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
