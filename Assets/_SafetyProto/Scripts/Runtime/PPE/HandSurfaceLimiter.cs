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

            Vector3 correction = Vector3.MoveTowards(_offset, Vector3.zero, returnSpeed * deltaTime);
            if (_hasPrevious)
            {
                for (int i = 0; i < _points.Length; i++)
                {
                    Vector3 movement = _points[i] + correction - _previous[i];
                    float distance = movement.magnitude;
                    if (distance < 0.00001f) continue;
                    int count = Physics.SphereCastNonAlloc(_previous[i], Radius(i), movement / distance,
                        _hits, distance, surfaceLayers, QueryTriggerInteraction.Ignore);
                    float nearest = distance;
                    for (int h = 0; h < count; h++)
                        if (IsSurface(_hits[h].collider)) nearest = Mathf.Min(nearest, _hits[h].distance);
                    if (nearest < distance)
                        correction += movement / distance * (Mathf.Max(0f, nearest - 0.001f) - distance);
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
                            correction += direction * (depth + 0.001f);
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
        }

        private bool HasOverlap(Vector3 offset)
        {
            for (int i = 0; i < _points.Length; i++)
            {
                int count = Physics.OverlapSphereNonAlloc(_points[i] + offset, Radius(i), _overlaps,
                    surfaceLayers, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < count; h++)
                    if (IsSurface(_overlaps[h])) return true;
            }
            return false;
        }
    }
}
