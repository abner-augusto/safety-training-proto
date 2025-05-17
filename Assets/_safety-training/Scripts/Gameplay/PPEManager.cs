using UnityEngine;
using System.Collections.Generic;

public class PPEManager : MonoBehaviour
{
    [Tooltip("Assign your EventBus ScriptableObject asset here.")]
    public EventBus eventBus;

    // Tracks which PPEType is currently considered "worn" and by which GameObject
    private Dictionary<PPEType, GameObject> _wornPPE = new Dictionary<PPEType, GameObject>();

    void Start()
    {
        if (eventBus == null)
        {
            Debug.LogError("EventBus not assigned to PPEManager!", this);
            enabled = false;
        }
    }

    // Called by PPEZone scripts
    public void ReportPPEStateChange(PPEType ppeType, bool isNowInsideZone, GameObject ppeObject)
    {
        if (eventBus == null) return;

        bool previouslyWorn = _wornPPE.ContainsKey(ppeType);
        GameObject currentWornObject = previouslyWorn ? _wornPPE[ppeType] : null;

        if (isNowInsideZone)
        {
            if (!previouslyWorn || currentWornObject != ppeObject) // New item or different item for this type
            {
                _wornPPE[ppeType] = ppeObject; // Register this specific object as worn
                eventBus.RaisePPEStateChanged(new PPEStateChangedEventArgs(ppeType, true));
                Debug.Log($"PPEManager: {ppeType} is now WORN (Item: {ppeObject.name}).");
            }
        }
        else // isNowOutsideZone
        {
            // Only unregister if the specific object that was registered is exiting
            if (previouslyWorn && currentWornObject == ppeObject)
            {
                _wornPPE.Remove(ppeType);
                eventBus.RaisePPEStateChanged(new PPEStateChangedEventArgs(ppeType, false));
                Debug.Log($"PPEManager: {ppeType} is now NOT WORN (Item: {ppeObject.name} exited).");
            }
        }
    }

    public bool IsWearing(PPEType ppeType)
    {
        return _wornPPE.ContainsKey(ppeType);
    }

    public bool AreAllRequiredPPEWorn(List<PPEType> requiredPPEList)
    {
        if (requiredPPEList == null || requiredPPEList.Count == 0)
        {
            return true; // No PPE required
        }

        foreach (PPEType ppe in requiredPPEList)
        {
            if (!IsWearing(ppe))
            {
                return false; // Found a required PPE that is not worn
            }
        }
        return true; // All required PPE are worn
    }
}