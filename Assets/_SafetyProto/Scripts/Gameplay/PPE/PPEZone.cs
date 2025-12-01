using SafetyProto.Data.Enums;
using UnityEngine;

namespace SafetyProto.Gameplay.PPE
{
    public class PPEZone : MonoBehaviour
    {
        public PPEType expectedPPEType = PPEType.None;
        [SerializeField] private Collider zoneCollider;

        private PPEManager _ppeManager;

        private void Start()
        {
            if (zoneCollider == null)
            {
                Debug.LogError($"The 'zoneCollider' on {gameObject.name} is not assigned in the inspector!", this);
                enabled = false;
                return;
            }
            
            if (!zoneCollider.isTrigger)
            {
                Debug.LogWarning($"PPEZone on {gameObject.name} should have its Collider set to 'Is Trigger'. Automatically setting.", this);
                zoneCollider.isTrigger = true;
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

        private void OnTriggerEnter(Collider other)
        {
            if (_ppeManager == null) return;

            PPEItem ppeItem = other.GetComponent<PPEItem>();
            if (ppeItem != null && ppeItem.ppeType == expectedPPEType)
            {
                _ppeManager.ReportPPEStateChange(ppeItem.ppeType, true, other.gameObject);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (_ppeManager == null) return;

            PPEItem ppeItem = other.GetComponent<PPEItem>();
            if (ppeItem != null && ppeItem.ppeType == expectedPPEType)
            {
                _ppeManager.ReportPPEStateChange(ppeItem.ppeType, false, other.gameObject);
            }
        }
    }
}
