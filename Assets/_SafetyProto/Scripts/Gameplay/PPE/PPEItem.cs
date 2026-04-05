using SafetyProto.Data.Enums;
using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.Gameplay.PPE
{
    [RequireComponent(typeof(Collider))] // Ensure it has a collider to be detected
    public class PPEItem : MonoBehaviour
    {
        public PPEType ppeType = PPEType.None;

        [Header("Distractor")]
        [Tooltip("Marcar true para EPIs incorretos (ex: capacete sem jugular, cinto abdominal). " +
                 "Distradores disparam popup educativo ao tentar encaixar num slot.")]
        public bool isDistractor = false;

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
