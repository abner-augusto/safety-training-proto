using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Data.Enums;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Gameplay.Actions
{
    public class ActionTrigger : MonoBehaviour
    {
        [Header("Action Configuration")]
        public ActionType actionTypeToTrigger = ActionType.None;
        public int interactorId = 0; // 0 for player, could be specific for multi-user or hands

        public void TriggerAction()
        {
            if (!this.IsEventBusReady())
            {
                return;
            }
            
            Debug.Log($"[TriggerAction] {actionTypeToTrigger} TRIGGERED on {gameObject.name}");

            if (actionTypeToTrigger == ActionType.None)
            {
                Debug.LogWarning($"ActionTrigger on {gameObject.name} has ActionType set to None. No event fired.", this);
                return;
            }

            var args = new ActionAttemptEventArgs(
                actionTypeToTrigger,
                interactorId,
                transform.position
            );

            ActionEvents.RaiseActionAttempt(args);
            Debug.Log($"ActionTrigger on {gameObject.name} Fired Action: {actionTypeToTrigger}");
        }
    }
}
