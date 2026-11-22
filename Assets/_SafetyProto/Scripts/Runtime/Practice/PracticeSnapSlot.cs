using Oculus.Interaction.Input;
using SafetyProto.Runtime.PPE;
using UnityEngine;

namespace SafetyProto.Runtime.Practice
{
    /// <summary>Where on the body a practice slot sits.</summary>
    public enum PracticeSlotSide
    {
        Body,
        LeftHand,
        RightHand
    }

    /// <summary>
    /// A body slot of the practice area: takes the practice cube the way a PPE slot takes a PPE
    /// item, with none of a PPE slot's reporting (worn PPE, task actions, order checks). It sits
    /// inside the training's PPE slot and shows itself through that slot's sphere, which turns
    /// green while the cube is in reach and blue once it is worn, as it does for the real PPE.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PracticeSnapSlot : MonoBehaviour
    {
        [Tooltip("Interaction id reported when the cube snaps here.")]
        [SerializeField] private string interactionId;
        [Tooltip("A slot on a hand never takes the cube that same hand is holding: it sits on the wrist, " +
                 "so it would catch every cube that hand lets go of.")]
        [SerializeField] private PracticeSlotSide side = PracticeSlotSide.Body;
        [Tooltip("The cube stays on this slot until the practice area resets: it cannot be pulled off.")]
        [SerializeField] private bool locksItem;
        [Tooltip("Where the cube sits once worn; empty = the slot itself. The head slot catches the cube in " +
                 "front of the forehead, where the headset cameras still see the hand, as the helmet's slot " +
                 "does, and wears it on top of the head.")]
        [SerializeField] private Transform wornPose;
        [Tooltip("The training's PPE slot this one sits in; its sphere shows the cube's hover and worn states.")]
        [SerializeField] private PPESnapSlot cueSlot;

        private int _hovering;

        public PracticeSnapItem Item { get; private set; }
        public bool IsOccupied => Item != null;
        public bool LocksItem => locksItem;
        public Transform WornPose => wornPose != null ? wornPose : transform;

        /// <summary>Whether this slot is worn on <paramref name="hand"/>.</summary>
        public bool IsOn(Handedness hand) =>
            side == (hand == Handedness.Left ? PracticeSlotSide.LeftHand : PracticeSlotSide.RightHand);

        private void Awake() => GetComponent<Collider>().isTrigger = true;

        private void OnEnable() => Refresh();

        private void OnDisable()
        {
            _hovering = 0;
            if (cueSlot != null) cueSlot.SetExternalCue(false, false);
        }

        public void OnItemEntered()
        {
            _hovering++;
            Refresh();
        }

        public void OnItemExited()
        {
            _hovering = Mathf.Max(0, _hovering - 1);
            Refresh();
        }

        public void Accept(PracticeSnapItem item)
        {
            Item = item;
            _hovering = 0;
            Refresh();
            if (PracticeArea.Active != null) PracticeArea.Active.MarkDone(interactionId);
        }

        public void Release(PracticeSnapItem item)
        {
            if (Item != item) return;
            Item = null;
            Refresh();
        }

        private void Refresh()
        {
            if (cueSlot != null) cueSlot.SetExternalCue(_hovering > 0 && !IsOccupied, IsOccupied);
        }
    }
}
