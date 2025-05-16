using UnityEngine;
using System.Collections.Generic; // Needed for Dictionary

// Checks player's worn protective equipment (Placeholder using bool toggles)
public class PPEManager : MonoBehaviour
{
    // --- Singleton Pattern ---
    public static PPEManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }
    // --- End Singleton Pattern ---

    [Header("PPE Status (Prototype Toggles)")]
    [SerializeField] private bool helmetWorn = true;
    [SerializeField] private bool glovesWorn = true;
    [SerializeField] private bool gogglesWorn = true;
    [SerializeField] private bool safetyVestWorn = false; // Example: Start with some missing

    // In a real app, this would be driven by collider checks, sensor data, etc.
    public bool isWearing(ProtectionType pt)
    {
        switch (pt)
        {
            case ProtectionType.Helmet: return helmetWorn;
            case ProtectionType.Gloves: return glovesWorn;
            case ProtectionType.Goggles: return gogglesWorn;
            case ProtectionType.SafetyVest: return safetyVestWorn;
            case ProtectionType.None: return true; // No PPE required, always true
            default:
                Debug.LogWarning($"PPEManager: Checking for unknown ProtectionType: {pt}. Returning false.");
                return false;
        }
    }

    // --- Prototype Methods to simulate changing PPE ---
    public void SetWearing(ProtectionType pt, bool worn)
    {
        Debug.Log($"PPEManager: Setting {pt} to {(worn ? "Worn" : "Not Worn")}");
        switch (pt)
        {
            case ProtectionType.Helmet: helmetWorn = worn; break;
            case ProtectionType.Gloves: glovesWorn = worn; break;
            case ProtectionType.Goggles: gogglesWorn = worn; break;
            case ProtectionType.SafetyVest: safetyVestWorn = worn; break;
            case ProtectionType.None: break; // Cannot set None
            default:
                Debug.LogWarning($"PPEManager: Cannot set status for unknown ProtectionType: {pt}.");
                break;
        }
        // In a real system, this might trigger an event
        // public event Action<ProtectionType, bool> OnPPEStatusChanged;
        // OnPPEStatusChanged?.Invoke(pt, worn);
    }

    // Method to check if a list of required PPE is fully worn
    public bool AreAllWearing(List<ProtectionType> requiredPPE)
    {
        if (requiredPPE == null || requiredPPE.Count == 0)
        {
            return true; // No PPE required
        }

        foreach (var requiredType in requiredPPE)
        {
            if (requiredType != ProtectionType.None && !isWearing(requiredType))
            {
                Debug.Log($"PPEManager: Missing required PPE: {requiredType}");
                return false;
            }
        }
        Debug.Log("PPEManager: All required PPE worn.");
        return true;
    }
}