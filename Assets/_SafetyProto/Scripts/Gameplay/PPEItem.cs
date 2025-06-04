using UnityEngine;

[RequireComponent(typeof(Collider))] // Ensure it has a collider to be detected
public class PPEItem : MonoBehaviour
{
    public PPEType ppeType = PPEType.None;

    private void Start()
    {
        if (ppeType == PPEType.None)
        {
            Debug.LogWarning($"PPEItem on {gameObject.name} has PPEType set to None.", this);
        }
        // Optionally ensure the collider is a trigger if your detection logic requires it,
        // or ensure it has a Rigidbody if it needs to physically interact and then be detected.
        // For simple trigger zone detection, the PPEItem's collider can be non-trigger.
    }
}