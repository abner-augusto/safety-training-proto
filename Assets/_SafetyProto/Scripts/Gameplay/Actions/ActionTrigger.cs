using UnityEngine;

public class ActionTrigger : MonoBehaviour
{
    [Tooltip("Assign your EventBus ScriptableObject asset here.")]
    public EventBus eventBus;

    [Header("Action Configuration")]
    public ActionType actionTypeToTrigger = ActionType.None;
    public int interactorId = 0; // 0 for player, could be specific for multi-user or hands

    private void Start()
    {
        if (eventBus == null)
        {
            Debug.LogError($"EventBus not assigned to ActionTrigger on {gameObject.name}", this);
        }
    }

    // This method will be called by other scripts (e.g., XR Grab Interactable events, collision scripts)
    public void TriggerAction()
    {
        Debug.Log($"[TriggerAction] {actionTypeToTrigger} TRIGGERED on {gameObject.name}");
        
        if (eventBus == null)
        {
            Debug.LogWarning($"ActionTrigger on {gameObject.name} cannot fire event: EventBus not assigned.");
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
        eventBus.RaiseActionAttempt(args);
        Debug.Log($"ActionTrigger on {gameObject.name} Fired Action: {actionTypeToTrigger}");
    }

}