using UnityEngine;

[RequireComponent(typeof(Collider))] // This collider defines the zone
public class PPEZone : MonoBehaviour
{
    public PPEType expectedPPEType = PPEType.None; // What this zone is for (e.g., Helmet for HeadZone)
    // No EventBus needed here, it reports to PPEManager

    private PPEManager _ppeManager;

    void Start()
    {
        Collider col = GetComponent<Collider>();
        if (!col.isTrigger)
        {
            Debug.LogWarning($"PPEZone on {gameObject.name} should have its Collider set to 'Is Trigger'. Automatically setting.", this);
            col.isTrigger = true;
        }

        if (expectedPPEType == PPEType.None)
        {
            Debug.LogWarning($"PPEZone on {gameObject.name} has ExpectedPPEType set to None.", this);
        }

        _ppeManager = Object.FindFirstObjectByType<PPEManager>();
        if (_ppeManager == null)
        {
            Debug.LogError($"PPEZone on {gameObject.name} could not find PPEManager in scene!", this);
            enabled = false;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (_ppeManager == null) return;

        PPEItem ppeItem = other.GetComponent<PPEItem>();
        if (ppeItem != null && ppeItem.ppeType == expectedPPEType)
        {
            _ppeManager.ReportPPEStateChange(ppeItem.ppeType, true, other.gameObject);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (_ppeManager == null) return;

        PPEItem ppeItem = other.GetComponent<PPEItem>();
        if (ppeItem != null && ppeItem.ppeType == expectedPPEType)
        {
            // Ensure the exiting item is the one currently registered for this zone/type
            _ppeManager.ReportPPEStateChange(ppeItem.ppeType, false, other.gameObject);
        }
    }
}