using SafetyProto.Data.Enums;
using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.Gameplay.PPE
{
    [RequireComponent(typeof(Collider))] // Ensure it has a collider to be detected
    public class PPEItem : MonoBehaviour
    {
        public PPEType ppeType = PPEType.None;

        private void Start()
        {
            if (ppeType == PPEType.None)
            {
                SafetyLog.Warning($"PPEItem on {gameObject.name} has PPEType set to None.", this);
            }
        }

        private void OnDisable()
        {
            UnregisterFromManager();
        }

        private void OnDestroy()
        {
            UnregisterFromManager();
        }

        private void UnregisterFromManager()
        {
            var manager = FindFirstObjectByType<PPEManager>();
            if (manager != null)
            {
                manager.UnregisterIfOwned(ppeType, gameObject);
            }
        }
    }
}
