using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using SafetyProto.Domain.Feedback;
using UnityEngine;

namespace SafetyProto.Runtime.Interaction
{
    /// <summary>
    /// Watches the hands holding an item that snaps to slots, for a grip the headset loses at a
    /// slot (<see cref="LostGripSnapPolicy"/>). When it fires, it releases the item through the
    /// SDK interactors, so the item's ordinary release path runs and can snap it to the slot it
    /// was just at.
    /// </summary>
    public sealed class LostGripWatcher
    {
        private const float ForcedReleaseWindowSeconds = 0.5f;

        private readonly LostGripSnapPolicy _policy;
        private readonly HandGrabInteractable[] _grabs;
        private readonly List<IHand> _holdingHands = new List<IHand>();
        private readonly List<HandGrabInteractor> _forced = new List<HandGrabInteractor>();
        private LostGripCause _forcedCause;
        private float _forcedUntil;
        private Vector3 _lastPosition;
        private bool _hasLastPosition;
        private bool _disarmed;
        private static Transform _trackingSpace;

        public LostGripWatcher(Rigidbody body, float graceSeconds, float confirmSeconds, float jumpSpeed)
        {
            _policy = new LostGripSnapPolicy(graceSeconds, confirmSeconds, jumpSpeed);
            _grabs = System.Array.FindAll(body.GetComponentsInChildren<HandGrabInteractable>(true),
                grab => grab.GetComponentInParent<Rigidbody>(true) == body);
        }

        /// <summary>Whether a release was forced and its SDK release has not arrived yet.</summary>
        public bool Releasing => _forcedCause != LostGripCause.None;

        /// <summary>Starts watching a new hold.</summary>
        public void Reset()
        {
            ClearForcedOverrides();
            _policy.Reset();
            _forcedCause = LostGripCause.None;
            _hasLastPosition = false;
            _disarmed = false;
        }

        /// <summary>
        /// Stops watching until the next hold: the item came off its slot in this hand, so a hand
        /// near the body is taking it off, not putting it on.
        /// </summary>
        public void Disarm() => _disarmed = true;

        /// <summary>One frame of a hold that has not snapped. Returns the cause when it forced a release.</summary>
        /// <param name="nearSlot">A free slot that takes the item is in reach now.</param>
        /// <param name="canSnap">The slot the item was last near can still take it.</param>
        public LostGripCause Watch(Vector3 position, bool nearSlot, bool canSnap)
        {
            if (_disarmed || Releasing) return LostGripCause.None;

            // Measured in the tracking space: a teleport or a turn of the rig moves the item in the
            // world without the hand moving, and must not read as a jump.
            Vector3 tracked = TrackingSpace() != null ? _trackingSpace.InverseTransformPoint(position) : position;
            float dt = Time.deltaTime;
            float speed = _hasLastPosition && dt > 0f ? Vector3.Distance(tracked, _lastPosition) / dt : 0f;
            _lastPosition = tracked;
            _hasLastPosition = true;

            CollectHoldingHands();
            LostGripCause cause = _policy.Observe(Time.time, nearSlot, HoldingHandsTracked(), speed);
            if (cause == LostGripCause.None || !canSnap) return LostGripCause.None;

            _forced.Clear();
            foreach (var grab in _grabs)
                foreach (var interactor in grab.SelectingInteractors)
                    _forced.Add(interactor);
            // Nothing to release (a grab this watcher cannot drive): leave the hold alone.
            if (_forced.Count == 0) return LostGripCause.None;

            foreach (var interactor in _forced)
                interactor.ForceRelease();
            _forcedCause = cause;
            _forcedUntil = Time.time + ForcedReleaseWindowSeconds;
            return cause;
        }

        /// <summary>
        /// At the release: whether it counts as letting go at the slot the item was last near,
        /// and why. <paramref name="atSlot"/> is whether a slot is in reach where it was let go.
        /// </summary>
        public LostGripCause TakeOnRelease(bool atSlot)
        {
            LostGripCause cause = LostGripCause.None;
            if (Releasing && Time.time <= _forcedUntil)
                cause = _forcedCause;
            else if (!_disarmed && !atSlot && _policy.CountsAtSlotOnRelease(Time.time, HoldingHandsTracked()))
                cause = LostGripCause.TrackingLost;

            ClearForcedOverrides();
            _forcedCause = LostGripCause.None;
            return cause;
        }

        // ForceRelease leaves an unselect override that only Unselect() clears. A hand the headset
        // dropped is disabled instead, which keeps the override, and its next grab would let go at
        // once. This runs inside the release event, while the interactor still reports Select, so
        // clear it without looking at the state.
        private void ClearForcedOverrides()
        {
            foreach (var interactor in _forced)
                if (interactor != null) interactor.ClearComputeShouldUnselectOverride();
            _forced.Clear();
        }

        private static Transform TrackingSpace()
        {
            if (_trackingSpace == null)
            {
                var rig = Object.FindAnyObjectByType<OVRCameraRig>();
                if (rig != null) _trackingSpace = rig.trackingSpace;
            }
            return _trackingSpace;
        }

        private void CollectHoldingHands()
        {
            _holdingHands.Clear();
            foreach (var grab in _grabs)
                foreach (var interactor in grab.SelectingInteractors)
                    if (interactor.Hand != null && !_holdingHands.Contains(interactor.Hand))
                        _holdingHands.Add(interactor.Hand);
        }

        // Low confidence counts as lost: it is what the headset reports while a hand presses an
        // item to the face or chest, before it drops the hand entirely.
        private bool HoldingHandsTracked()
        {
            foreach (var hand in _holdingHands)
                if (hand == null || !hand.IsConnected || !hand.IsTrackedDataValid || !hand.IsHighConfidence)
                    return false;
            return true;
        }
    }
}
