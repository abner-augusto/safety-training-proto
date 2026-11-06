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
        [Tooltip("Hides the item (SetActive false) when snapped into a slot. Keeping it on the item lets each PPE decide its own behavior, independent of the slot.")]
        public bool hideWhenEquipped = false;

        [Header("Distractor")]
        [Tooltip("Set true for incorrect PPE (e.g. helmet without chinstrap, waist belt). " +
                 "Distractors raise an educational popup when snapped into a slot.")]
        public bool isDistractor = false;

        [Tooltip("Distractor id in the scenario (the equip task's 'distractors' field). Selects " +
                 "which explanation the popup shows and which task the deduction is charged to. " +
                 "Only applies with isDistractor.")]
        public string distractorId = string.Empty;

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
            FindAnyObjectByType<PPEManager>()?.UnregisterIfOwned(ppeType, gameObject);
        }
    }
}
