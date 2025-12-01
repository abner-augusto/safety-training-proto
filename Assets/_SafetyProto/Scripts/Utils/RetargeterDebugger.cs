using UnityEngine;
using Meta.XR.Movement.Retargeting;
using static OVRSkeleton; // Make sure to include necessary namespaces

public class RetargeterDebugger : MonoBehaviour
{
    [Tooltip("Reference to the CharacterRetargeter component you are debugging. If not assigned, it will be automatically found on this GameObject.")]
    public CharacterRetargeter targetRetargeter;

    [Tooltip("The IOVRSkeletonDataProvider component (e.g., OVRBody) to force-assign.")]
    public MonoBehaviour dataProviderComponent; // Use MonoBehaviour to get a reference to the component

    private void Awake()
    {
        // Automatically find the CharacterRetargeter if not assigned
        if (targetRetargeter == null)
        {
            targetRetargeter = GetComponent<CharacterRetargeter>();
        }

        if (targetRetargeter == null)
        {
            Debug.LogWarning("RetargeterDebugger could not find a CharacterRetargeter component on this GameObject. Please assign it manually.", this);
        }
    }


    /// <summary>
    /// Forces the data provider assignment and re-initializes the retargeter.
    /// </summary>
    [ContextMenu("Force Initialization & Reset")]
    public void ForceInitAndReset()
    {
        if (targetRetargeter == null)
        {
            Debug.LogError("Target Retargeter is not assigned.");
            return;
        }

        // 1. Attempt to cast the MonoBehaviour reference to the required interface
        IOVRSkeletonDataProvider newDataProvider = dataProviderComponent as IOVRSkeletonDataProvider;

        if (newDataProvider == null)
        {
            Debug.LogError("The assigned Data Provider Component does not implement IOVRSkeletonDataProvider.");
            return;
        }

        // 2. Use reflection or a direct reference to reset the private field
        // Since you cannot directly access the private '_dataProvider' field in the official script,
        // the safest method is to call a public method that forces re-initialization,
        // like calling the core logic from Awake() or Start().

        // We can mimic the Awake logic for fetching the component, but if it fails,
        // you would need to modify the SDK script or use reflection, which is risky.

        // Safer approach: Reset the component and rely on the next Update() cycle.
        
        Debug.Log("Attempting to re-run CharacterRetargeter's Setup...");
        
        // This relies on the internal structure of the SDK script:
        // By setting _dataProvider = gameObject.GetComponent<IOVRSkeletonDataProvider>();
        // inside the public virtual void Awake(), a direct modification would be needed.
        
        // As a workaround, we will try to mimic the initialization flow:

        // 3. Force-call the Start/Setup logic if the Retargeter exposes it.
        // The script already has a public `Setup(string config)` method.
        // We can call it to re-initialize the internal SkeletonRetargeter.
        targetRetargeter.Setup(targetRetargeter.Config);
        
        // If the Awake() logic is what's failing, you must either:
        // A) Modify the SDK script (not recommended).
        // B) Use reflection (complex and brittle).
        // C) Manually set the public 'DataProvider' property if it had a setter.
        
        // Assuming the setup is the main thing, call Setup:
        targetRetargeter.Setup(targetRetargeter.Config);
        
        Debug.Log("CharacterRetargeter Setup has been re-called. Check console for any new Assert errors.");
    }
}