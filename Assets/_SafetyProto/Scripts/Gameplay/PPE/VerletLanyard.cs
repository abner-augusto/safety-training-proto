using System;
using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.Gameplay.PPE
{
    /// <summary>
    /// Visual rope simulation for the safety harness lanyard using Verlet integration.
    /// Connects the harness (worn on the player's body) to an anchor point (olhal de ancoragem).
    ///
    /// Based on position-based Verlet integration (inspired by GaryMcWhorter/Verlet-Chain-Unity
    /// and alirezaft/UnityVerletRope), simplified and optimized for Quest 3 performance:
    ///   - Uses LineRenderer instead of procedural mesh (lower GPU cost).
    ///   - Runs in FixedUpdate with configurable substeps.
    ///   - No per-frame allocations.
    ///   - Constraint iterations keep the rope stiff enough to look realistic for a ~1.5m lanyard.
    ///
    /// Inspector setup:
    ///   1. Add this component to the harness PPE item (or a child object).
    ///   2. Assign <see cref="startAnchor"/> to the harness attachment point on the player body.
    ///   3. Assign <see cref="endAnchor"/> to the olhal de ancoragem (wall anchor) transform.
    ///      If left null, the rope hangs freely from the start anchor (disconnected state).
    ///   4. Tweak <see cref="ropeLength"/>, <see cref="nodeCount"/>, etc. in the Inspector.
    ///   5. A LineRenderer is auto-added if missing. Set its material/width to taste.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class VerletLanyard : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────

        [Header("Anchor Points")]
        [Tooltip("Transform on the player body where the lanyard connects (e.g. harness D-ring on chest/back).")]
        [SerializeField] private Transform startAnchor;

        [Tooltip("Transform on the wall/scaffold anchor point (olhal de ancoragem). " +
                 "Leave null for a disconnected lanyard that hangs freely.")]
        [SerializeField] private Transform endAnchor;

        [Header("Rope Properties")]
        [Tooltip("Total rest length of the lanyard in meters (NR-35 typical: 0.9 – 1.8 m).")]
        [SerializeField, Range(0.3f, 3f)] private float ropeLength = 1.5f;

        [Tooltip("Number of simulation nodes. More = smoother curve but higher CPU cost. " +
                 "10–20 is a good range for Quest.")]
        [SerializeField, Range(4, 40)] private int nodeCount = 12;

        [Tooltip("Constraint solver iterations per substep. Higher = stiffer rope.")]
        [SerializeField, Range(1, 20)] private int constraintIterations = 8;

        [Tooltip("Substeps per FixedUpdate for more stable simulation.")]
        [SerializeField, Range(1, 4)] private int substeps = 2;

        [Header("Physics")]
        [Tooltip("Gravity applied to rope nodes (world space).")]
        [SerializeField] private Vector3 gravity = new Vector3(0f, -9.81f, 0f);

        [Tooltip("Velocity damping per step (0 = no damping, 1 = frozen).")]
        [SerializeField, Range(0f, 0.1f)] private float damping = 0.02f;

        [Header("Visual")]
        [Tooltip("Width of the LineRenderer. Typical lanyard rope: 0.008 – 0.015 m.")]
        [SerializeField, Range(0.002f, 0.05f)] private float ropeWidth = 0.012f;

        [Tooltip("Color of the lanyard rope.")]
        [SerializeField] private Color ropeColor = new Color(1f, 0.55f, 0f); // safety orange

        // ── Runtime ───────────────────────────────────────────────

        private struct VerletNode
        {
            public Vector3 Position;
            public Vector3 PreviousPosition;
        }

        private VerletNode[] _nodes;
        private float _segmentLength;
        private LineRenderer _lineRenderer;
        private Vector3[] _linePositions; // reusable buffer for LineRenderer.SetPositions

        private bool _initialized;

        // ── Public API ────────────────────────────────────────────

        /// <summary>
        /// Connect or reconnect the free end of the lanyard to a new anchor transform.
        /// Pass null to disconnect (rope hangs freely).
        /// </summary>
        public void SetEndAnchor(Transform anchor)
        {
            endAnchor = anchor;

            if (anchor != null && _initialized)
            {
                // Snap last node to new anchor so there's no pop
                _nodes[_nodes.Length - 1].Position = anchor.position;
                _nodes[_nodes.Length - 1].PreviousPosition = anchor.position;
            }
        }

        /// <summary>
        /// Set the start anchor (harness attachment point) at runtime.
        /// </summary>
        public void SetStartAnchor(Transform anchor)
        {
            startAnchor = anchor;
        }

        /// <summary>
        /// Whether the lanyard currently has both ends anchored.
        /// </summary>
        public bool IsConnected => endAnchor != null;

        /// <summary>
        /// Current rest length of the rope.
        /// </summary>
        public float RopeLength
        {
            get => ropeLength;
            set
            {
                ropeLength = Mathf.Max(0.1f, value);
                if (_initialized)
                    _segmentLength = ropeLength / (_nodes.Length - 1);
            }
        }

        // ── Unity Lifecycle ───────────────────────────────────────

        private void Awake()
        {
            _lineRenderer = GetComponent<LineRenderer>();
            ConfigureLineRenderer();
        }

        private void Start()
        {
            if (startAnchor == null)
            {
                SafetyLog.Warning("VerletLanyard: startAnchor não atribuído. Usando transform.position como fallback.", this);
            }

            InitializeNodes();
        }

        private void OnEnable()
        {
            // Re-initialize when enabled by the RetractableLanyardController
            if (_lineRenderer != null)
                _lineRenderer.enabled = true;

            InitializeNodes();
        }

        private void OnDisable()
        {
            // Hide rope when disabled
            if (_lineRenderer != null)
            {
                _lineRenderer.enabled = false;
                _lineRenderer.positionCount = 0;
            }

            _initialized = false;
        }

        private void FixedUpdate()
        {
            if (!_initialized) return;

            float subDt = Time.fixedDeltaTime / substeps;

            for (int s = 0; s < substeps; s++)
            {
                ApplyVerletIntegration(subDt);
                ApplyConstraints();
            }
        }

        private void LateUpdate()
        {
            if (!_initialized) return;
            UpdateLineRenderer();
        }

        private void OnValidate()
        {
            // Live-tweak support in Editor
            if (_lineRenderer != null)
            {
                _lineRenderer.startWidth = ropeWidth;
                _lineRenderer.endWidth = ropeWidth;
            }

            if (_initialized && _nodes != null && _nodes.Length != nodeCount)
            {
                InitializeNodes();
            }
            else if (_initialized)
            {
                _segmentLength = ropeLength / (_nodes.Length - 1);
            }
        }

        // ── Initialization ────────────────────────────────────────

        private void InitializeNodes()
        {
            _nodes = new VerletNode[nodeCount];
            _linePositions = new Vector3[nodeCount];
            _segmentLength = ropeLength / (nodeCount - 1);

            Vector3 startPos = startAnchor != null ? startAnchor.position : transform.position;
            Vector3 endPos = endAnchor != null
                ? endAnchor.position
                : startPos + Vector3.down * ropeLength;

            for (int i = 0; i < nodeCount; i++)
            {
                float t = (float)i / (nodeCount - 1);
                Vector3 pos = Vector3.Lerp(startPos, endPos, t);

                // Add a slight sag so the rope doesn't start perfectly straight
                float sag = Mathf.Sin(t * Mathf.PI) * ropeLength * 0.05f;
                pos.y -= sag;

                _nodes[i] = new VerletNode
                {
                    Position = pos,
                    PreviousPosition = pos
                };
            }

            _lineRenderer.positionCount = nodeCount;
            _initialized = true;
        }

        // ── Verlet Integration ────────────────────────────────────

        private void ApplyVerletIntegration(float dt)
        {
            float dtSq = dt * dt;
            float dampFactor = 1f - damping;

            // Pin first node to start anchor
            if (startAnchor != null)
            {
                _nodes[0].Position = startAnchor.position;
                _nodes[0].PreviousPosition = startAnchor.position;
            }

            // Pin last node to end anchor (if connected)
            if (endAnchor != null)
            {
                int last = _nodes.Length - 1;
                _nodes[last].Position = endAnchor.position;
                _nodes[last].PreviousPosition = endAnchor.position;
            }

            // Integrate free nodes (skip pinned endpoints)
            int startIdx = 1;
            int endIdx = endAnchor != null ? _nodes.Length - 2 : _nodes.Length - 1;

            for (int i = startIdx; i <= endIdx; i++)
            {
                Vector3 current = _nodes[i].Position;
                Vector3 previous = _nodes[i].PreviousPosition;

                // Velocity = current - previous (implicit in Verlet)
                Vector3 velocity = (current - previous) * dampFactor;

                // New position: x(t+dt) = x(t) + v*dampFactor + a*dt²
                Vector3 newPos = current + velocity + gravity * dtSq;

                _nodes[i].PreviousPosition = current;
                _nodes[i].Position = newPos;
            }
        }

        // ── Distance Constraints ──────────────────────────────────

        private void ApplyConstraints()
        {
            for (int iter = 0; iter < constraintIterations; iter++)
            {
                // Forward pass
                for (int i = 0; i < _nodes.Length - 1; i++)
                {
                    SolveDistanceConstraint(i, i + 1);
                }

                // Re-pin anchors after each iteration to prevent drift
                PinAnchors();
            }
        }

        private void SolveDistanceConstraint(int idxA, int idxB)
        {
            Vector3 posA = _nodes[idxA].Position;
            Vector3 posB = _nodes[idxB].Position;

            Vector3 delta = posB - posA;
            float currentLength = delta.magnitude;

            if (currentLength < 1e-6f) return; // avoid division by zero

            float error = currentLength - _segmentLength;
            Vector3 correction = (delta / currentLength) * error * 0.5f;

            // Determine which nodes are pinned
            bool aPinned = (idxA == 0 && startAnchor != null)
                        || (idxA == _nodes.Length - 1 && endAnchor != null);
            bool bPinned = (idxB == 0 && startAnchor != null)
                        || (idxB == _nodes.Length - 1 && endAnchor != null);

            if (aPinned && bPinned)
            {
                // Both pinned — don't move either
                return;
            }
            else if (aPinned)
            {
                _nodes[idxB].Position -= correction * 2f;
            }
            else if (bPinned)
            {
                _nodes[idxA].Position += correction * 2f;
            }
            else
            {
                _nodes[idxA].Position += correction;
                _nodes[idxB].Position -= correction;
            }
        }

        private void PinAnchors()
        {
            if (startAnchor != null)
            {
                _nodes[0].Position = startAnchor.position;
            }

            if (endAnchor != null)
            {
                _nodes[_nodes.Length - 1].Position = endAnchor.position;
            }
        }

        // ── Visual ────────────────────────────────────────────────

        private void ConfigureLineRenderer()
        {
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.startWidth = ropeWidth;
            _lineRenderer.endWidth = ropeWidth;
            _lineRenderer.numCapVertices = 2;
            _lineRenderer.numCornerVertices = 2;
            _lineRenderer.textureMode = LineTextureMode.Tile;

            // Create a runtime material if none assigned — avoids the pink "missing material"
            if (_lineRenderer.sharedMaterial == null)
            {
                _lineRenderer.material = CreateRopeMaterial();
            }

            _lineRenderer.startColor = ropeColor;
            _lineRenderer.endColor = ropeColor;
        }

        /// <summary>
        /// Creates a simple unlit material at runtime. Tries URP Unlit first,
        /// falls back to legacy Unlit/Color, then Sprites/Default as last resort.
        /// All three render vertex colors, which is what LineRenderer uses.
        /// </summary>
        private Material CreateRopeMaterial()
        {
            // Try URP Unlit (preferred for Quest / URP projects)
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            // Fallback chain for non-URP or editor-only contexts
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            if (shader == null)
            {
                SafetyLog.Warning("VerletLanyard: Nenhum shader Unlit encontrado. " +
                                  "LineRenderer ficará rosa. Atribua um material no Inspector.", this);
                return null;
            }

            var mat = new Material(shader)
            {
                color = ropeColor,
                name = "VerletLanyard_Runtime"
            };

            // URP Unlit needs _BaseColor instead of _Color for tinting
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", ropeColor);

            return mat;
        }

        private void UpdateLineRenderer()
        {
            for (int i = 0; i < _nodes.Length; i++)
            {
                _linePositions[i] = _nodes[i].Position;
            }

            _lineRenderer.SetPositions(_linePositions);
        }

        // ── Gizmos ────────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!_initialized || _nodes == null) return;

            Gizmos.color = Color.yellow;
            for (int i = 0; i < _nodes.Length; i++)
            {
                Gizmos.DrawWireSphere(_nodes[i].Position, 0.005f);

                if (i < _nodes.Length - 1)
                {
                    Gizmos.color = Color.Lerp(Color.yellow, Color.red, (float)i / _nodes.Length);
                    Gizmos.DrawLine(_nodes[i].Position, _nodes[i + 1].Position);
                }
            }

            // Draw anchor markers
            if (startAnchor != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(startAnchor.position, 0.02f);
            }

            if (endAnchor != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(endAnchor.position, 0.02f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (startAnchor == null) return;

            // Preview rope length as a sphere
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.15f);
            Gizmos.DrawWireSphere(startAnchor.position, ropeLength);
        }
#endif
    }
}