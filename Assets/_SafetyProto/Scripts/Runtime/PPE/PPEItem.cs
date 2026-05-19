using SafetyProto.Core;
using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.Runtime.PPE
{
    [RequireComponent(typeof(Collider))]
    public class PPEItem : MonoBehaviour
    {
        public PPEType ppeType = PPEType.None;

        [Header("Snap Behavior")]
        [Tooltip("Esconde o item (SetActive false) quando encaixado num slot. Carregar no item permite que cada EPI decida seu próprio comportamento independente do slot.")]
        public bool hideWhenEquipped = false;

        [Header("Distractor")]
        [Tooltip("Marcar true para EPIs incorretos (ex: capacete sem jugular, cinto abdominal). " +
                 "Distradores disparam popup educativo ao tentar encaixar num slot.")]
        public bool isDistractor = false;

        private void Awake()
        {
            if (ppeType == PPEType.None)
            {
                SafetyLog.Warning($"PPEItem on {gameObject.name} has PPEType set to None.", this);
            }
        }

        private void OnDisable() => UnregisterFromManager();

        private void OnDestroy() => UnregisterFromManager();

        private void UnregisterFromManager()
        {
            FindFirstObjectByType<PPEManager>()?.UnregisterIfOwned(ppeType, gameObject);
        }
    }
}
