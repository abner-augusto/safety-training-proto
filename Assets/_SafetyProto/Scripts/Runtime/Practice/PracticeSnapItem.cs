using Oculus.Interaction;
using SafetyProto.Domain.Feedback;
using SafetyProto.Runtime.Feedback;
using SafetyProto.Runtime.Interaction;
using UnityEngine;

namespace SafetyProto.Runtime.Practice
{
    /// <summary>
    /// The practice cube: equips on a <see cref="PracticeSnapSlot"/> like a PPE item on a body
    /// slot. Let go in reach and it snaps and follows the slot; pull it past the unsnap distance
    /// and it comes off. A grip the headset loses at the slot counts as letting go there, as for
    /// the real PPE, so the practice teaches the same forgiveness.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PracticeSnapItem : MonoBehaviour, ISecuredPlacement
    {
        [SerializeField] private float snapSearchRadius = 0.12f;
        [SerializeField] private float unsnapDistance = 0.15f;
        [SerializeField] private float followSpeed = 20f;

        [Header("Lost Grip")]
        [SerializeField, Range(0f, 1f)] private float lostGripGraceSeconds = 0.4f;
        [SerializeField, Range(0f, 0.5f)] private float lostGripConfirmSeconds = 0.1f;
        [SerializeField, Range(2f, 20f)] private float lostGripJumpSpeed = 5f;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip snapSound;
        [SerializeField] private AudioClip unsnapSound;
        [SerializeField, Range(0f, 1f)] private float audioVolume = 0.85f;

        private readonly Collider[] _overlap = new Collider[16];
        private Rigidbody _rigidbody;
        private Grabbable _grabbable;
        private ReturnObjectHome _home;
        private LostGripWatcher _lostGrip;
        private PracticeSnapSlot _slot;
        private PracticeSnapSlot _hovering;
        private PracticeSnapSlot _recent;
        private bool _grabbed;
        private bool _physicsOwed;

        bool ISecuredPlacement.IsSecured => _slot != null;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _grabbable = GetComponent<Grabbable>();
            _home = GetComponent<ReturnObjectHome>();
            _lostGrip = new LostGripWatcher(_rigidbody, lostGripGraceSeconds, lostGripConfirmSeconds, lostGripJumpSpeed);
        }

        private void OnEnable()
        {
            if (_grabbable != null) _grabbable.WhenPointerEventRaised += OnPointerEvent;
        }

        private void OnDisable()
        {
            if (_grabbable != null) _grabbable.WhenPointerEventRaised -= OnPointerEvent;
            _grabbed = false;
            _hovering = null;
        }

        /// <summary>Takes the cube off its slot without a sound; the practice area sends it home after.</summary>
        public void ResetForPractice()
        {
            if (_slot != null) Unsnap(silent: true);
            _lostGrip.Reset();
        }

        private void OnPointerEvent(PointerEvent evt)
        {
            if (evt.Type != PointerEventType.Select && evt.Type != PointerEventType.Unselect
                && evt.Type != PointerEventType.Cancel) return;

            bool was = _grabbed;
            _grabbed = _grabbable.SelectingPointsCount > 0;
            if (!was && _grabbed)
            {
                _lostGrip.Reset();
                _recent = null;
            }
            else if (was && !_grabbed)
            {
                OnReleased();
            }
        }

        private void OnReleased()
        {
            if (_slot != null) return;

            PracticeSnapSlot slot = FindSlot();
            LostGripCause lostGrip = _lostGrip.TakeOnRelease(slot != null);
            if (slot == null && lostGrip != LostGripCause.None) slot = UsableRecent();
            _lostGrip.Reset();
            _recent = null;

            if (slot != null) SnapTo(slot);
        }

        private void LateUpdate()
        {
            if (_slot == null)
            {
                if (_physicsOwed) RestorePhysics();
                if (!_grabbed) return;
                PracticeSnapSlot near = FindSlot();
                if (near != null) _recent = near;
                _lostGrip.Watch(transform.position, near != null, UsableRecent() != null);
                return;
            }

            if (_grabbed && Vector3.Distance(transform.position, _slot.transform.position) >= unsnapDistance)
            {
                Unsnap(silent: false);
                return;
            }

            float t = followSpeed <= 0f ? 1f : followSpeed * Time.deltaTime;
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, _slot.transform.position, t),
                Quaternion.Slerp(transform.rotation, _slot.transform.rotation, t));
        }

        private PracticeSnapSlot UsableRecent() => _recent != null && !_recent.IsOccupied ? _recent : null;

        private PracticeSnapSlot FindSlot()
        {
            if (_hovering != null && !_hovering.IsOccupied) return _hovering;

            int count = Physics.OverlapSphereNonAlloc(transform.position, snapSearchRadius, _overlap, ~0, QueryTriggerInteraction.Collide);
            PracticeSnapSlot best = null;
            float bestSq = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var slot = _overlap[i] != null ? _overlap[i].GetComponent<PracticeSnapSlot>() : null;
                if (slot == null || slot.IsOccupied) continue;
                float d = (slot.transform.position - transform.position).sqrMagnitude;
                if (d < bestSq) { best = slot; bestSq = d; }
            }
            return best;
        }

        private void SnapTo(PracticeSnapSlot slot)
        {
            _slot = slot;
            _hovering = null;
            _physicsOwed = false;
            slot.Accept(this);
            if (_home != null) _home.CancelReturn();
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
            transform.SetPositionAndRotation(slot.transform.position, slot.transform.rotation);
            Play(snapSound, audioVolume);
        }

        private void Unsnap(bool silent)
        {
            var slot = _slot;
            _slot = null;
            slot.Release(this);
            RestorePhysics();
            _lostGrip.Disarm();
            if (!silent) Play(unsnapSound, audioVolume * 0.7f);
            if (!_grabbed && _home != null) _home.RequestReturn();
        }

        private void RestorePhysics()
        {
            // Writing isKinematic under the SDK's lock would hand a held cube to gravity, and the
            // lock restores the snapped (kinematic) state on release; hand physics back after it.
            if (_rigidbody.IsLocked())
            {
                _physicsOwed = true;
                return;
            }
            _physicsOwed = false;
            _rigidbody.isKinematic = false;
            _rigidbody.useGravity = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            var slot = other.GetComponent<PracticeSnapSlot>();
            if (slot == null || slot.IsOccupied) return;
            _hovering = slot;
            slot.OnItemEntered();
        }

        private void OnTriggerExit(Collider other)
        {
            var slot = other.GetComponent<PracticeSnapSlot>();
            if (slot == null) return;
            if (_hovering == slot) _hovering = null;
            slot.OnItemExited();
        }

        private void Play(AudioClip clip, float volume)
        {
            if (clip == null) return;
            audioSource = AudioSourceDefaults.EnsureSpatial(this, audioSource, 0.3f, 5f);
            audioSource.PlayOneShot(clip, volume);
        }
    }
}
