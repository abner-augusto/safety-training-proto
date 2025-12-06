using Oculus.Interaction;
using SafetyProto.Gameplay.Actions;
using UnityEngine;

namespace SafetyProto.Integration.MetaXR
{
    [DisallowMultipleComponent]
    public class SnapToActionAdapter : SnapInteractable
    {
        [Header("Actions to fire")]
        [Tooltip("Action fired when an interactor snaps onto this object.")]
        [SerializeField] private ActionTrigger onSnap;

        [Tooltip("Optional action fired when the interactor unsnaps.")]
        [SerializeField] private ActionTrigger onUnsnap;

        [Header("Auto wiring")]
        [Tooltip("If true, will try to find ActionTrigger components on this GameObject when missing.")]
        [SerializeField] private bool autoFindTriggers = true;

        protected override void Awake()
        {
            base.Awake();

            if (!autoFindTriggers) return;

            // If only one ActionTrigger exists, use it as onSnap by default
            if (onSnap == null && onUnsnap == null)
            {
                var trigger = GetComponent<ActionTrigger>();
                if (trigger != null)
                {
                    onSnap = trigger;
                }
            }
        }

        protected override void SelectingInteractorAdded(SnapInteractor interactor)
        {
            base.SelectingInteractorAdded(interactor);

            if (onSnap != null)
            {
                onSnap.TriggerAction();
            }
        }

        protected override void SelectingInteractorRemoved(SnapInteractor interactor)
        {
            base.SelectingInteractorRemoved(interactor);

            if (onUnsnap != null)
            {
                onUnsnap.TriggerAction();
            }
        }
    }
}
