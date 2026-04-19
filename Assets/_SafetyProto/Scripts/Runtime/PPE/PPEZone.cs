using SafetyProto.Core.Events;
using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Data.Enums;
using UnityEngine;

namespace SafetyProto.Runtime.PPE
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
                SafetyLog.Error($"The 'zoneCollider' on {gameObject.name} is not assigned in the inspector!", this);
                SafetyEvents.RaiseSafetyError(new SafetyErrorEventArgs { Source = nameof(PPEZone), Message = "Zone collider missing", Details = $"GameObject '{gameObject.name}' has no zoneCollider assigned." });
                enabled = false;
                return;
            }
            
            if (!zoneCollider.isTrigger)
            {
                SafetyLog.Warning($"PPEZone on {gameObject.name} should have its Collider set to 'Is Trigger'. Automatically setting.", this);
                zoneCollider.isTrigger = true;
            }

            if (expectedPPEType == PPEType.None)
            {
                SafetyLog.Warning($"PPEZone on {gameObject.name} has ExpectedPPEType set to None.", this);
            }

            _ppeManager = Object.FindFirstObjectByType<PPEManager>();
            if (_ppeManager == null)
            {
                SafetyLog.Error($"PPEZone on {gameObject.name} could not find PPEManager in scene!", this);
                SafetyEvents.RaiseSafetyError(new SafetyErrorEventArgs { Source = nameof(PPEZone), Message = "PPEManager missing", Details = $"PPEZone '{gameObject.name}' could not locate a PPEManager in the scene." });
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
