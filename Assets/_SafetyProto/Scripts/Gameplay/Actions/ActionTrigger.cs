using UnityEngine;
public class ActionTrigger : MonoBehaviour
{
    [Header("Action Configuration")]
    public ActionType actionTypeToTrigger = ActionType.None;
    public int interactorId = 0; // 0 for player, could be specific for multi-user or hands

    // This method will be called by other scripts (e.g., XR Grab Interactable events, collision scripts)
    public void TriggerAction()
    {
        Debug.Log($"[TriggerAction] {actionTypeToTrigger} TRIGGERED on {gameObject.name}");
        if (EventBus.Instance == null)
        {
            Debug.LogWarning($"ActionTrigger on {gameObject.name} cannot fire event: EventBus instance is missing.");
            return;
        }
        if (actionTypeToTrigger == ActionType.None)
        {
            Debug.LogWarning($"ActionTrigger on {gameObject.name} has ActionType set to None. No event fired.", this);
            return;
        }

        ActionAttemptEventArgs args = new ActionAttemptEventArgs(
            actionTypeToTrigger,
            interactorId,
            transform.position // Or the position of the interaction point
        );
        EventBus.Instance.RaiseActionAttempt(args);
        Debug.Log($"ActionTrigger on {gameObject.name} Fired Action: {actionTypeToTrigger}");
    }
}