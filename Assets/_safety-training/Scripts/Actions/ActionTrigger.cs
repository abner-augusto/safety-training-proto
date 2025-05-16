using UnityEngine;
using UnityEngine.InputSystem;

public class ActionTrigger : MonoBehaviour
{
    [SerializeField] private ActionType actionToTrigger;

    private Keyboard kb;

    void Awake()
    {
        kb = Keyboard.current;
    }

    void Update()
    {
        if (kb == null) return; // no keyboard detected

        if (kb.spaceKey.wasPressedThisFrame && actionToTrigger == ActionType.AttachGuardrail)
        {
            Debug.Log($"Simulating action: {actionToTrigger}");
            GameManager.Instance?.SimulateAction(actionToTrigger);
        }

        if (kb.enterKey.wasPressedThisFrame && actionToTrigger == ActionType.CheckValve)
        {
            Debug.Log($"Simulating action: {actionToTrigger}");
            GameManager.Instance?.SimulateAction(actionToTrigger);
        }
    }

    public void TriggerAction()
    {
        Debug.Log($"Triggering action: {actionToTrigger}");
        GameManager.Instance?.SimulateAction(actionToTrigger);
    }
}
