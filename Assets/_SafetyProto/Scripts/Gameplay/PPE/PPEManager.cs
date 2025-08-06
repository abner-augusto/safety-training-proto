using UnityEngine;
using System.Collections.Generic;
using SafetyProto.Core.Interfaces;

public class PPEManager : MonoBehaviour, ISessionResettable
{
    // Tracks which PPEType is currently considered "worn" and by which GameObject
    private readonly Dictionary<PPEType, GameObject> _wornPPE = new Dictionary<PPEType, GameObject>();

    void Start()
    {
        // Use the helper to validate the EventBus at startup.
        // If it fails, this component will be disabled.
        this.IsEventBusReady();
    }

    // Called by PPEZone scripts
    public void ReportPPEStateChange(PPEType ppeType, bool isNowInsideZone, GameObject ppeObject)
    {
        bool previouslyWorn = _wornPPE.ContainsKey(ppeType);
        GameObject currentWornObject = previouslyWorn ? _wornPPE[ppeType] : null;

        if (isNowInsideZone)
        {
            if (!previouslyWorn || currentWornObject != ppeObject) // New item or different item for this type
            {
                _wornPPE[ppeType] = ppeObject; // Register this specific object as worn
                EventBus.Instance.RaisePpeStateChanged(new PPEStateChangedEventArgs(ppeType, true));
                Debug.Log($"PPEManager: {ppeType} is now WORN (Item: {ppeObject.name}).");
            }
        }
        else // isNowOutsideZone
        {
            // Only unregister if the specific object that was registered is exiting
            if (previouslyWorn && currentWornObject == ppeObject)
            {
                _wornPPE.Remove(ppeType);
                EventBus.Instance.RaisePpeStateChanged(new PPEStateChangedEventArgs(ppeType, false));
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

    public void ResetSession()
    {
        _wornPPE.Clear();
    }
}