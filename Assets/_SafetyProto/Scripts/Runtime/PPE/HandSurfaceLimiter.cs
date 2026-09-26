using System;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace SafetyProto.Runtime.PPE
{
    /// <summary>Offsets the rendered hand against stationary colliders without changing tracked interaction poses.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HandVisual))]
    public sealed class HandSurfaceLimiter : MonoBehaviour
    {
        [SerializeField] private LayerMask surfaceLayers = ~0;
        [SerializeField] private Transform ignoredRoot;
        [SerializeField] private MonoBehaviour[] priorityInteractors = Array.Empty<MonoBehaviour>();
        [SerializeField, Min(0.001f)] private float fingerRadius = 0.012f;
        [SerializeField, Min(0.001f)] private float palmRadius = 0.035f;
        [SerializeField, Min(0.01f)] private float maximumOffset = 0.18f;
        [SerializeField, Min(0.01f)] private float returnSpeed = 0.6f;

        private static readonly HandJointId[] Tips =
        {
            HandJointId.HandThumbTip, HandJointId.HandIndexTip, HandJointId.HandMiddleTip,
            HandJointId.HandRingTip, HandJointId.HandPinkyTip
        };

        private readonly Vector3[] _points = new Vector3[6];
        private readonly Vector3[] _previous = new Vector3[6];
        private readonly RaycastHit[] _hits = new RaycastHit[64];
        private readonly Collider[] _overlaps = new Collider[64];
        private HandVisual _visual;
        private SphereCollider _probe;
        private Vector3 _offset;
        private Vector3 _lastRoot;
        private bool _hasPrevious;
        private bool _released;
        private float _lastUpdateTime;
        private bool _hasSurfaceContact;
        private Vector3 _contactPoint;

        /// <summary>Raised on each valid contact update, including sustained contact while sliding.</summary>
        public event Action<Vector3> WhenSurfaceContact;

        private void Awake()
        {
            _visual = GetComponent<HandVisual>();
            var probeObject = new GameObject("Hand contact probe") { hideFlags = HideFlags.HideAndDontSave };
            _probe = probeObject.AddComponent<SphereCollider>();
            _probe.isTrigger = true;
            _probe.enabled = false;
        }

        private void OnEnable()
        {
            _visual.WhenHandVisualUpdated += ConstrainVisual;
        }

        private void OnDisable()
        {
            _visual.WhenHandVisualUpdated -= ConstrainVisual;
            if (_visual.Root != null && _visual.Hand != null && _visual.Hand.GetRootPose(out Pose pose))
                _visual.Root.position = pose.position;
            ResetContact();
        }

        private void OnDestroy()
        {
            if (_probe != null) Destroy(_probe.gameObject);
        }

        private bool HasPriorityInteraction()
        {
            foreach (MonoBehaviour component in priorityInteractors)
            {
                if (component == null || !component.isActiveAndEnabled) continue;
                if (component is IInteractorView interactor && interactor.State == InteractorState.Select)
                    return true;
                if (component is PokeInteractor poke && poke.IsPassedSurface) return true;
            }
            return false;
        }

        private bool IsSurface(Collider candidate)
        {
            return candidate != null && candidate.attachedRigidbody == null &&
                (ignoredRoot == null || !candidate.transform.IsChildOf(ignoredRoot));
        }

        private void ResetContact()
        {
            _offset = Vector3.zero;
            _hasPrevious = false;
            _released = false;
            _hasSurfaceContact = false;
            _lastUpdateTime = Time.unscaledTime;
        }

        private float Radius(int index) => (index == 5 ? palmRadius : fingerRadius) * _visual.Hand.Scale;

        private void ConstrainVisual()
        {
            if (_visual.Root == null || !_visual.IsVisible || !_visual.Hand.IsTrackedDataValid ||
                HasPriorityInteraction())
            {
                ResetContact();
                return;
            }

            for (int i = 0; i < Tips.Length; i++)
            {
                if (!_visual.Hand.GetJointPose(Tips[i], out Pose pose))
                {
                    ResetContact();
                    return;
                }
                _points[i] = pose.position;
            }
            Vector3 root = _visual.Root.position;
            _points[5] = Vector3.Lerp(root, (_points[1] + _points[4]) * 0.5f, 0.45f);
            if (_hasPrevious && Vector3.Distance(root, _lastRoot) > maximumOffset * 2f)
                ResetContact();

            float deltaTime = Mathf.Clamp(Time.unscaledTime - _lastUpdateTime, 0f, 0.05f);
            _lastUpdateTime = Time.unscaledTime;
            _lastRoot = root;
            if (_released)
            {
                if (HasOverlap(Vector3.zero)) return;
                _released = false;
            }

            bool wasTouching = _hasSurfaceContact;
            _hasSurfaceContact = false;
            Vector3 correction = Vector3.MoveTowards(_offset, Vector3.zero, returnSpeed * deltaTime);
            if (_hasPrevious)
            {
                // A shared offset can push another probe into contact, so revisit all six probes.
                for (int pass = 0; pass < 3; pass++)
                {
                    Vector3 before = correction;
                    for (int i = 0; i < _points.Length; i++)
                    {
                        Vector3 target = _points[i] + correction;
                        correction += SlideProbe(_previous[i], target, Radius(i)) - target;
                    }
                    if ((correction - before).sqrMagnitude < 0.0000000001f) break;
                }
            }

            // Resolve all probes together so a finger correction also moves the palm out of corners.
            // Unity needs an enabled collider for penetration queries; disable it before any physics step.
            _probe.enabled = true;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < _points.Length; i++)
                {
                    _probe.radius = Radius(i);
                    int count = Physics.OverlapSphereNonAlloc(_points[i] + correction, _probe.radius,
                        _overlaps, surfaceLayers, QueryTriggerInteraction.Ignore);
                    for (int h = 0; h < count; h++)
                    {
                        Collider surface = _overlaps[h];
                        if (!IsSurface(surface)) continue;
                        if (Physics.ComputePenetration(_probe, _points[i] + correction, Quaternion.identity,
                            surface, surface.transform.position, surface.transform.rotation,
                            out Vector3 direction, out float depth))
                        {
                            RecordContact(_points[i] + correction - direction * _probe.radius);
                            correction += direction * (depth + 0.001f);
                        }
                    }
                }
            }
            _probe.enabled = false;

            // A large real/virtual mismatch releases the hand until it leaves the obstacle.
            if (correction.magnitude > maximumOffset)
            {
                ResetContact();
                _released = true;
                return;
            }
            _offset = correction;
            _visual.Root.position = root + correction;
            for (int i = 0; i < _points.Length; i++) _previous[i] = _points[i] + correction;
            _hasPrevious = true;
            // The solver's clearance must not turn a resting hand into repeated contact entries.
            if (!_hasSurfaceContact && wasTouching) _hasSurfaceContact = HasOverlap(correction, 0.003f);
            if (_hasSurfaceContact) WhenSurfaceContact?.Invoke(_contactPoint);
        }

        private void RecordContact(Vector3 point)
        {
            if (!_hasSurfaceContact) _contactPoint = point;
            _hasSurfaceContact = true;
        }

        private Vector3 SlideProbe(Vector3 position, Vector3 target, float radius)
        {
            Vector3 remaining = target - position;
            for (int iteration = 0; iteration < 4; iteration++)
            {
                float distance = remaining.magnitude;
                if (distance < 0.00001f) break;
                Vector3 direction = remaining / distance;
                int count = Physics.SphereCastNonAlloc(position, radius, direction,
                    _hits, distance, surfaceLayers, QueryTriggerInteraction.Ignore);
                int nearest = -1;
                for (int h = 0; h < count; h++)
                {
                    if (!IsSurface(_hits[h].collider) || Vector3.Dot(direction, _hits[h].normal) >= 0f)
                        continue;
                    if (nearest < 0 || _hits[h].distance < _hits[nearest].distance) nearest = h;
                }
                if (nearest < 0) return position + remaining;

                RaycastHit hit = _hits[nearest];
                RecordContact(hit.point);
                Vector3 advance = direction * Mathf.Max(0f, hit.distance - 0.001f);
                position += advance;
                remaining -= advance;
                remaining -= hit.normal * Mathf.Min(0f, Vector3.Dot(remaining, hit.normal));
            }

            // Keep the last swept position if the contact budget is exhausted.
            return position;
        }

        private bool HasOverlap(Vector3 offset, float padding = 0f)
        {
            for (int i = 0; i < _points.Length; i++)
            {
                int count = Physics.OverlapSphereNonAlloc(_points[i] + offset, Radius(i) + padding, _overlaps,
                    surfaceLayers, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < count; h++)
                    if (IsSurface(_overlaps[h])) return true;
            }
            return false;
        }
    }
}
