using Oculus.Interaction;
using SafetyProto.Core.Logging;
using SafetyProto.Gameplay.PPE;
using UnityEngine;

namespace SafetyProto.Gameplay.PPE
{
    [RequireComponent(typeof(SnapInteractor))]
    public class PPESlotController : MonoBehaviour
    {
        private SnapInteractor _snapInteractor;
        private PPEManager _ppeManager;

        private void Awake()
        {
            _snapInteractor = GetComponent<SnapInteractor>();
        }

        private void Start()
        {
            _ppeManager = FindFirstObjectByType<PPEManager>();

            if (_ppeManager == null)
            {
                SafetyLog.Error($"PPESlotController on {gameObject.name} could not find PPEManager in scene.", this);
                enabled = false;
                return;
            }

            _snapInteractor.WhenInteractableSelected.Action += OnSnapped;
            _snapInteractor.WhenInteractableUnselected.Action += OnUnsnapped;
        }

        private void OnDestroy()
        {
            if (_snapInteractor != null)
            {
                _snapInteractor.WhenInteractableSelected.Action -= OnSnapped;
                _snapInteractor.WhenInteractableUnselected.Action -= OnUnsnapped;
            }
        }

        private void OnSnapped(SnapInteractable interactable)
        {
            var item = interactable.GetComponent<PPEItem>();
            if (item == null)
            {
                SafetyLog.Warning($"PPESlotController: snapped object '{interactable.gameObject.name}' has no PPEItem component.", this);
                return;
            }

            _ppeManager.ReportPPEStateChange(item.ppeType, true, interactable.gameObject);
        }

        private void OnUnsnapped(SnapInteractable interactable)
        {
            var item = interactable.GetComponent<PPEItem>();
            if (item == null) return;

            _ppeManager.ReportPPEStateChange(item.ppeType, false, interactable.gameObject);
        }
    }
}