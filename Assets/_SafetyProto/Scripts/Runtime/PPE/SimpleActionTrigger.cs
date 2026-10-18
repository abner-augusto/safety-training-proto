using System;
using Oculus.Interaction;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Safety;
using SafetyProto.Runtime.Actions;
using UnityEngine;

namespace SafetyProto.Runtime.PPE
{
    /// <summary>
    /// Publishes an <see cref="ActionAttemptedEvent"/> the first time this object is grabbed,
    /// with no pass/fail judgment of the grab itself. Used for tasks that only need to confirm
    /// the participant interacted with an object (e.g. inspecting the harness before donning),
    /// as opposed to <see cref="ScaffoldPieceInstaller"/> or <see cref="PPESnapSlot"/>, which
    /// validate placement.
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    public class SimpleActionTrigger : MonoBehaviour, ISessionResettable
    {
        [Header("Action")]
        [SerializeField, ActionId] private string actionId = string.Empty;
        [SerializeField] private string sourceId = string.Empty;
        [SerializeField] private string context = string.Empty;

        [Tooltip("Grabbable on the object. Auto-found if empty.")]
        [SerializeField] private Grabbable grabbable;

        [Tooltip("Publish once per session instead of on every grab.")]
        [SerializeField] private bool triggerOnce = true;

        private bool _triggered;
        private readonly RefusedAttemptTracker _refusalTracker = new RefusedAttemptTracker();
        private Action<ActionRefusedEventArgs> _onActionRefused;

        public bool IsTriggered => _triggered;

        private string ResolvedSourceId => string.IsNullOrWhiteSpace(sourceId) ? name : sourceId.Trim();

        private void Awake()
        {
            if (grabbable == null) grabbable = GetComponent<Grabbable>();
        }

        private void OnEnable()
        {
            if (grabbable != null) grabbable.WhenPointerEventRaised += OnPointerEvent;
            if (EventBus.Instance != null)
            {
                _onActionRefused ??= HandleActionRefused;
                EventBus.Instance.Subscribe(_onActionRefused);
            }
        }

        private void OnDisable()
        {
            if (grabbable != null) grabbable.WhenPointerEventRaised -= OnPointerEvent;
            if (EventBus.Instance != null && _onActionRefused != null)
            {
                EventBus.Instance.Unsubscribe(_onActionRefused);
            }
        }

        private void OnPointerEvent(PointerEvent evt)
        {
            if (evt.Type != PointerEventType.Select) return;
            if (triggerOnce && _triggered) return;
            if (string.IsNullOrWhiteSpace(actionId))
            {
                SafetyLog.Warning($"[SimpleActionTrigger] Nenhum ActionId configurado em {name}.", this);
                return;
            }

            _triggered = true;
            string resolvedContext = string.IsNullOrWhiteSpace(context) ? null : context.Trim();
            ActionEvents.PublishActionAttempt(actionId.Trim(), ResolvedSourceId, resolvedContext, transform.position);
        }

        private void HandleActionRefused(ActionRefusedEventArgs args)
        {
            if (!_triggered) return;

            _refusalTracker.Observe(args.ActionId, args.SourceId, args.ReasonCode, actionId.Trim(), ResolvedSourceId, waitForPopup: false, Time.time, fallbackSeconds: 0f);

            if (_refusalTracker.TryTakeRevert(Time.time))
            {
                _triggered = false;
                SafetyLog.Info($"[SimpleActionTrigger] Tentativa '{actionId.Trim()}' recusada ({args.ReasonCode}) em '{name}'; gatilho rearmado.", this);
            }
        }

        public void ResetSession()
        {
            _triggered = false;
            _refusalTracker.Reset();
        }
    }
}
