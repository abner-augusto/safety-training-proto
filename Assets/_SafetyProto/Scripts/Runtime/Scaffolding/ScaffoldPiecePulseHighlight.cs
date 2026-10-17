using Oculus.Interaction;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Runtime.Scaffolding
{
    /// <summary>
    /// Pulses a scaffold piece between its own colour and yellow while the participant, already
    /// on the andaime, has it in view. The first grab (or an install) switches the pulse off for
    /// the rest of the session: the piece has been found, so the cue has done its job.
    ///
    /// The renderers need a material using the <c>SafetyProto/PulseHighlight</c> shader. This
    /// component only writes <c>_PulseAmount</c> through a MaterialPropertyBlock; the oscillation
    /// itself runs in the shader.
    /// </summary>
    [RequireComponent(typeof(ScaffoldPieceInstaller))]
    public class ScaffoldPiecePulseHighlight : MonoBehaviour
    {
        private static readonly int PulseAmountId = Shader.PropertyToID("_PulseAmount");

        [Tooltip("Renderers that pulse. Their material must use SafetyProto/PulseHighlight. A " +
                 "property block overrides the material, so do not point the installer's Piece " +
                 "Feedback Renderer at one of these and expect both to show.")]
        [SerializeField] private Renderer[] targetRenderers;

        [Tooltip("HMD centre eye. Leave empty to use the MainCamera-tagged camera.")]
        [SerializeField] private Transform viewOrigin;

        [Tooltip("Pulses only after the teleport to the andaime. Auto-found if empty.")]
        [SerializeField] private PhaseController phaseController;

        [Tooltip("Half-angle in degrees of the cone around the head's forward axis that counts as " +
                 "'seeing' the piece. ~35 covers most of the Quest's field of view.")]
        [SerializeField, Range(5f, 90f)] private float viewHalfAngle = 35f;

        [Tooltip("Beyond this distance in metres the piece does not pulse.")]
        [SerializeField, Range(0.5f, 20f)] private float maxViewDistance = 5f;

        [Tooltip("Seconds to fade the pulse in or out, so a head resting on the cone edge does " +
                 "not make the piece flicker.")]
        [SerializeField, Range(0f, 1f)] private float fadeDuration = 0.25f;

        private ScaffoldPieceInstaller _installer;
        private Grabbable _grabbable;
        private Transform _resolvedOrigin;
        private MaterialPropertyBlock _mpb;
        private System.Action<SessionStartedEventArgs> _onSessionStarted;
        private bool _consumed;
        private float _amount = -1f;

        private void Awake()
        {
            _installer = GetComponent<ScaffoldPieceInstaller>();
            _grabbable = GetComponent<Grabbable>();
            _mpb = new MaterialPropertyBlock();

            if (targetRenderers == null || targetRenderers.Length == 0)
            {
                var own = GetComponent<Renderer>();
                if (own != null) targetRenderers = new[] { own };
            }

            if (phaseController == null)
                phaseController = FindFirstObjectByType<PhaseController>();
            if (phaseController == null)
                SafetyLog.Warning($"[ScaffoldPiecePulseHighlight] '{name}': PhaseController não encontrado — o pulso fica limitado apenas pela distância.", this);
        }

        private void OnEnable()
        {
            if (_grabbable != null)
                _grabbable.WhenPointerEventRaised += OnPointerEvent;
        }

        private void Start()
        {
            if (EventBus.Instance == null) return;
            _onSessionStarted = _ => _consumed = false;
            EventBus.Instance.Subscribe<SessionStartedEventArgs>(_onSessionStarted);
        }

        private void OnDisable()
        {
            if (_grabbable != null)
                _grabbable.WhenPointerEventRaised -= OnPointerEvent;
            ApplyAmount(0f);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null && _onSessionStarted != null)
                EventBus.Instance.Unsubscribe<SessionStartedEventArgs>(_onSessionStarted);
        }

        private void LateUpdate()
        {
            if (_installer.IsInstalled) _consumed = true;

            bool onScaffold = phaseController == null || phaseController.IsInAndaimePhase;
            bool active = !_consumed && onScaffold && TryGetViewOrigin(out var origin) &&
                          IsInView(origin.position, origin.forward, PieceCenter(), viewHalfAngle, maxViewDistance);

            float target = active ? 1f : 0f;
            float next = fadeDuration <= 0f
                ? target
                : Mathf.MoveTowards(Mathf.Max(_amount, 0f), target, Time.deltaTime / fadeDuration);
            ApplyAmount(next);
        }

        /// <summary>
        /// Whether <paramref name="target"/> lies inside the view cone of a head at
        /// <paramref name="origin"/> looking along <paramref name="forward"/>.
        /// </summary>
        public static bool IsInView(Vector3 origin, Vector3 forward, Vector3 target,
                                    float halfAngleDegrees, float maxDistance)
        {
            Vector3 toTarget = target - origin;
            float distance = toTarget.magnitude;
            if (distance > maxDistance) return false;
            if (distance < 1e-4f) return true;
            return Vector3.Angle(forward, toTarget) <= halfAngleDegrees;
        }

        private void OnPointerEvent(PointerEvent evt)
        {
            if (evt.Type == PointerEventType.Select)
                _consumed = true;
        }

        private Vector3 PieceCenter()
        {
            if (targetRenderers != null)
                foreach (var r in targetRenderers)
                    if (r != null) return r.bounds.center;
            return transform.position;
        }

        private bool TryGetViewOrigin(out Transform origin)
        {
            if (_resolvedOrigin == null)
            {
                var mainCamera = Camera.main;
                _resolvedOrigin = viewOrigin != null ? viewOrigin
                                : mainCamera != null ? mainCamera.transform : null;
            }
            origin = _resolvedOrigin;
            return origin != null;
        }

        private void ApplyAmount(float amount)
        {
            if (Mathf.Approximately(amount, _amount) || targetRenderers == null) return;
            _amount = amount;

            foreach (var r in targetRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat(PulseAmountId, amount);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
