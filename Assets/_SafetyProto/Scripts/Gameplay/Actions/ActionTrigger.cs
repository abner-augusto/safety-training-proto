using UnityEngine;
public class ActionTrigger : MonoBehaviour
{
    [Header("Action Configuration")]
    public ActionType actionTypeToTrigger = ActionType.None;
    public int interactorId = 0; // 0 for player, could be specific for multi-user or hands

    // This method will be called by other scripts (e.g., XR Grab Interactable events, collision scripts)
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

        EventBus.Instance.RaiseActionAttempt(args);
        Debug.Log($"ActionTrigger on {gameObject.name} Fired Action: {actionTypeToTrigger}");
    }
}