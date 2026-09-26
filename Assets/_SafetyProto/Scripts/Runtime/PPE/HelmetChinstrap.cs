using System;
using Oculus.Interaction;
using SafetyProto.Core.Interfaces;
using SafetyProto.Runtime.Feedback;
using UnityEngine;

namespace SafetyProto.Runtime.PPE
{
    /// <summary>
    /// Drives a skinned chinstrap with short Verlet chains while loose and hides it while worn.
    /// Visibility does not publish a separate training action or compliance state.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class HelmetChinstrap : MonoBehaviour, ISessionResettable
    {
        [Serializable]
        public sealed class Strap
        {
            public Transform[] bones;
            public Vector3[] fittedPositions;
            public Quaternion[] fittedRotations;

            [NonSerialized] internal Vector3[] positions;
            [NonSerialized] internal Vector3[] previous;
            [NonSerialized] internal float[] lengths;
            [NonSerialized] internal Vector3[] surfaceNormals;
        }

        [SerializeField] private PPESnapItem snapItem;
        [SerializeField] private ReturnObjectHome returnHome;
        [SerializeField] private Strap[] straps;
        [SerializeField] private LayerMask collisionMask = Physics.DefaultRaycastLayers;
        [SerializeField, Min(0.001f)] private float collisionRadius = 0.012f;
        [SerializeField, Range(1, 4)] private int substeps = 2;
        [SerializeField, Range(2, 12)] private int constraintIterations = 6;
        [SerializeField, Range(0f, 0.2f)] private float damping = 0.045f;
        [SerializeField, Range(0f, 0.3f)] private float bendResistance = 0.035f;
        [SerializeField, Min(0.1f)] private float teleportDistance = 0.3f;
        [SerializeField] private Vector3 shellCenter = new Vector3(0f, 0.018f, 0f);
        [SerializeField] private Vector3 shellRadii = new Vector3(0.087f, 0.047f, 0.102f);

        private ChinstrapCollision _collision;
        private GameObject _probeObject;
        private Vector3 _lastPosition;
        private Quaternion _lastRotation;
        private bool _ready;
        private bool _wasFitted;
        private bool _wasReturning;
        private SkinnedMeshRenderer[] _strapRenderers;
        private bool[] _rendererVisibility;
        private Grabbable _grabbable;
        private bool _wasHeld;
        private float _previousStepDuration;

        private bool IsHeld => _grabbable != null && _grabbable.SelectingPointsCount > 0;

        /// <summary>True when the helmet is worn and the strap is hidden with simulation suspended.</summary>
        public bool IsFitted => snapItem != null && snapItem.IsSnapped;

        private void Awake()
        {
            _grabbable = GetComponent<Grabbable>();
            if (snapItem == null) snapItem = GetComponent<PPESnapItem>();
            if (returnHome == null) returnHome = GetComponent<ReturnObjectHome>();
            if (straps == null || straps.Length == 0) return;
            foreach (Strap strap in straps)
            {
                if (strap.bones == null || strap.bones.Length < 3 ||
                    strap.fittedPositions == null || strap.fittedRotations == null ||
                    strap.fittedPositions.Length != strap.bones.Length ||
                    strap.fittedRotations.Length != strap.bones.Length) return;
                foreach (Transform bone in strap.bones) if (bone == null) return;
                int count = strap.bones.Length;
                strap.positions = new Vector3[count];
                strap.previous = new Vector3[count];
                strap.lengths = new float[count - 1];
                strap.surfaceNormals = new Vector3[count];
                for (int i = 0; i < count - 1; i++)
                    strap.lengths[i] = Vector3.Distance(transform.TransformPoint(strap.fittedPositions[i]),
                        transform.TransformPoint(strap.fittedPositions[i + 1]));
            }

            _strapRenderers = Array.FindAll(GetComponentsInChildren<SkinnedMeshRenderer>(true),
                renderer => Array.Exists(renderer.bones, bone =>
                    Array.Exists(straps, strap => Array.IndexOf(strap.bones, bone) >= 0)));
            _rendererVisibility = new bool[_strapRenderers.Length];
            for (int i = 0; i < _strapRenderers.Length; i++)
                _rendererVisibility[i] = _strapRenderers[i].enabled;

            // ComputePenetration requires an enabled collider. Park this trigger away from gameplay.
            _probeObject = new GameObject("ChinstrapContactProbe")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = 2
            };
            _probeObject.transform.position = new Vector3(0f, -10000f, 0f);
            var probe = _probeObject.AddComponent<CapsuleCollider>();
            probe.isTrigger = true;
            _collision = new ChinstrapCollision(transform, probe, collisionMask, collisionRadius);
            _ready = true;
            ResetSession();
        }

        private void OnEnable()
        {
            if (_ready) ResetSession();
        }

        private void OnDestroy()
        {
            if (_probeObject == null) return;
            if (Application.isPlaying) Destroy(_probeObject);
            else DestroyImmediate(_probeObject);
        }

        /// <summary>Discards stored velocity and seeds a pose outside the helmet shell.</summary>
        public void ResetSession()
        {
            if (!_ready) return;
            SeedPose(IsFitted);
            _wasFitted = IsFitted;
            _wasReturning = returnHome != null && returnHome.IsReturning;
            _wasHeld = IsHeld;
            _previousStepDuration = 0f;
            RememberPose();
            SetVisible(!IsFitted);
            UpdateBones();
        }

        private void SeedPose(bool fitted)
        {
            foreach (Strap strap in straps)
            {
                Vector3 anchor = strap.fittedPositions[0];
                float side = Mathf.Sign(anchor.x);
                for (int i = 0; i < strap.positions.Length; i++)
                {
                    float t = (float)i / (strap.positions.Length - 1);
                    Vector3 local = fitted ? strap.fittedPositions[i] : anchor +
                        new Vector3(side * (0.13f * t + 0.018f * Mathf.Sin(t * Mathf.PI)),
                            -0.01f * t, -0.15f * t);
                    strap.positions[i] = transform.TransformPoint(local);
                    strap.previous[i] = strap.positions[i];
                    strap.surfaceNormals[i] = Vector3.zero;
                }
            }
        }

        private void FixedUpdate()
        {
            if (IsHeld) return;
            AdvanceSimulation(Time.fixedDeltaTime);
        }

        private void AdvanceSimulation(float duration)
        {
            if (!_ready) return;
            bool held = IsHeld;
            bool fitted = IsFitted;
            bool returning = returnHome != null && returnHome.IsReturning;
            bool jumped = Vector3.Distance(_lastPosition, transform.position) > teleportDistance ||
                          Quaternion.Angle(_lastRotation, transform.rotation) > 100f;
            bool reset = fitted != _wasFitted || jumped || returning != _wasReturning;
            if (reset)
            {
                SeedPose(fitted);
                _previousStepDuration = 0f;
            }
            else if (returning) CarryWithHelmet();
            if (held != _wasHeld)
            {
                // A clock change must not reinterpret the last displacement as a new velocity.
                foreach (Strap strap in straps)
                    Array.Copy(strap.positions, strap.previous, strap.positions.Length);
                _previousStepDuration = 0f;
            }
            _wasHeld = held;
            _wasFitted = fitted;
            _wasReturning = returning;
            RememberPose();
            if (fitted || duration <= 0f) return;

            float referenceStep = Time.fixedDeltaTime / substeps;
            duration = Mathf.Min(duration, 0.05f);
            int steps = Mathf.Max(substeps, Mathf.CeilToInt(duration / referenceStep));
            float dt = duration / steps;
            float attenuation = Mathf.Pow(1f - damping, dt / referenceStep);
            for (int step = 0; step < steps; step++)
            {
                float velocityScale = _previousStepDuration > 0f ? dt / _previousStepDuration : 1f;
                foreach (Strap strap in straps) Simulate(strap, dt, velocityScale * attenuation);
                _previousStepDuration = dt;
            }
        }

        private void CarryWithHelmet()
        {
            Quaternion delta = transform.rotation * Quaternion.Inverse(_lastRotation);
            foreach (Strap strap in straps)
                for (int i = 0; i < strap.positions.Length; i++)
                {
                    strap.positions[i] = transform.position + delta * (strap.positions[i] - _lastPosition);
                    strap.previous[i] = strap.positions[i];
                }
        }

        private void RememberPose()
        {
            _lastPosition = transform.position;
            _lastRotation = transform.rotation;
        }

        private void Simulate(Strap strap, float dt, float velocityScale)
        {
            Vector3[] p = strap.positions;
            p[0] = transform.TransformPoint(strap.fittedPositions[0]);
            strap.previous[0] = p[0];
            for (int i = 1; i < p.Length; i++)
            {
                strap.surfaceNormals[i] = Vector3.zero;
                Vector3 current = p[i];
                Vector3 velocity = (current - strap.previous[i]) * velocityScale;
                p[i] = _collision.Sweep(current, current + velocity + Physics.gravity * (dt * dt));
                strap.previous[i] = current;
            }

            for (int iteration = 0; iteration < constraintIterations; iteration++)
            {
                for (int i = 0; i < p.Length - 2; i++)
                    Constrain(p, i, i + 2, strap.lengths[i] + strap.lengths[i + 1], bendResistance);
                for (int i = 0; i < p.Length - 1; i++)
                    Constrain(p, i, i + 1, strap.lengths[i], 1f);
                for (int i = 1; i < p.Length; i++)
                {
                    Vector3 before = p[i];
                    ResolveShell(ref p[i]);
                    Vector3 midpoint = (p[i - 1] + p[i]) * 0.5f;
                    Vector3 outside = midpoint;
                    ResolveShell(ref outside);
                    Vector3 shellCorrection = outside - midpoint;
                    if (i > 1) p[i - 1] += shellCorrection;
                    p[i] += shellCorrection * (i == 1 ? 2f : 1f);
                    if (_collision.Resolve(ref p[i - 1], ref p[i], i == 1))
                    {
                        // Static friction prevents perpetual tabletop jitter in a visual-only chain.
                        strap.previous[i - 1] = p[i - 1];
                        strap.previous[i] = p[i];
                        strap.surfaceNormals[i - 1] = _collision.ContactNormal;
                        strap.surfaceNormals[i] = _collision.ContactNormal;
                    }
                    else if (before != p[i]) strap.previous[i] = p[i];
                }
            }
        }

        private static void Constrain(Vector3[] p, int a, int b, float length, float strength)
        {
            Vector3 delta = p[b] - p[a];
            float distance = delta.magnitude;
            if (distance < 0.000001f) return;
            Vector3 correction = delta * ((distance - length) / distance * strength);
            if (a == 0) p[b] -= correction;
            else
            {
                p[a] += correction * 0.5f;
                p[b] -= correction * 0.5f;
            }
        }

        private void ResolveShell(ref Vector3 point)
        {
            Vector3 local = transform.InverseTransformPoint(point) - shellCenter;
            Vector3 radii = shellRadii + Vector3.one * collisionRadius;
            Vector3 scaled = new Vector3(local.x / radii.x, local.y / radii.y, local.z / radii.z);
            if (scaled.sqrMagnitude >= 1f) return;
            if (scaled.sqrMagnitude < 0.000001f) scaled = Vector3.down;
            point = transform.TransformPoint(shellCenter + Vector3.Scale(scaled.normalized, radii));
        }

        private void LateUpdate()
        {
            if (!_ready) return;
            // Read the final snap state after PPESnapItem's LateUpdate, including frames without a physics step.
            bool fitted = IsFitted;
            SetVisible(!fitted);
            if (fitted)
            {
                _wasFitted = true;
                return;
            }
            if (_wasFitted)
            {
                SeedPose(false);
                _previousStepDuration = 0f;
                _wasFitted = false;
                RememberPose();
            }
            // Tracked grabs move the Transform outside FixedUpdate; solve before drawing that pose.
            if (IsHeld) AdvanceSimulation(Time.deltaTime);
            UpdateBones();
        }

        private void SetVisible(bool visible)
        {
            for (int i = 0; i < _strapRenderers.Length; i++)
                if (_strapRenderers[i] != null)
                    _strapRenderers[i].enabled = visible && _rendererVisibility[i];
        }

        private void UpdateBones()
        {
            foreach (Strap strap in straps)
            {
                for (int i = 0; i < strap.bones.Length; i++)
                {
                    // The mounting point must not inherit contact roll or lag behind the tracked helmet.
                    if (i == 0)
                    {
                        strap.bones[i].SetPositionAndRotation(transform.TransformPoint(strap.fittedPositions[i]),
                            transform.rotation * strap.fittedRotations[i]);
                        continue;
                    }
                    int a = Mathf.Max(0, i - 1);
                    int b = Mathf.Min(strap.bones.Length - 1, i + 1);
                    Vector3 restDirection = transform.TransformDirection(strap.fittedPositions[b] - strap.fittedPositions[a]);
                    Vector3 direction = strap.positions[b] - strap.positions[a];
                    Quaternion rotation = transform.rotation * strap.fittedRotations[i];
                    if (direction.sqrMagnitude > 0.0000001f)
                    {
                        Quaternion bend = Quaternion.FromToRotation(restDirection, direction);
                        rotation = bend * rotation;
                        if (strap.surfaceNormals[i].sqrMagnitude > 0.0001f)
                        {
                            // Roll the webbing onto the contact surface instead of balancing on its edge.
                            Vector3 normal = bend * Vector3.Cross(restDirection, transform.forward).normalized;
                            float roll = Vector3.SignedAngle(Vector3.ProjectOnPlane(normal, direction),
                                Vector3.ProjectOnPlane(strap.surfaceNormals[i], direction), direction);
                            if (roll > 90f) roll -= 180f;
                            if (roll < -90f) roll += 180f;
                            rotation = Quaternion.AngleAxis(roll, direction) * rotation;
                        }
                    }
                    strap.bones[i].SetPositionAndRotation(strap.positions[i], rotation);
                }
            }
        }
    }
}
