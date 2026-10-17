using Oculus.Interaction;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
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
    public class SimpleActionTrigger : MonoBehaviour
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

        private void Awake()
        {
            if (grabbable == null) grabbable = GetComponent<Grabbable>();
        }

        private void OnEnable()
        {
            if (grabbable != null) grabbable.WhenPointerEventRaised += OnPointerEvent;
        }

        private void OnDisable()
        {
            if (grabbable != null) grabbable.WhenPointerEventRaised -= OnPointerEvent;
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
            ActionEvents.PublishActionAttempt(actionId.Trim(), sourceId, context, transform.position);
        }
    }
}
