using SafetyProto.Core;
using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// Keeps a participant on a narrow virtual floor (the scaffold plank) when the physical room
    /// is larger than it. When the body steps off, the shared screen fade dims the view part way,
    /// a short popup asks them to come back, and a glowing column marks the nearest spot on the
    /// floor's center line. Everything clears once they are back inside.
    ///
    /// The dimming is an explicit level on <see cref="OVRScreenFade"/>, which keeps the larger of
    /// it and the teleport/fall fades. The column must sit on a layer only the popup camera
    /// draws (PopupUI): that camera renders after the fade, so the column stays bright over it.
    ///
    /// This is XR ergonomics, never scoring: the guard only publishes
    /// <see cref="PlayspaceNoticeEventArgs"/> for the session log and the evaluator dashboard.
    ///
    /// The zone is this transform: centered on the floor, x along its width, z along its depth,
    /// no scale. Active only after the phase transition into the area and outside a recenter.
    /// </summary>
    public class WalkableZoneGuard : MonoBehaviour
    {
        [Header("Zone")]
        [Tooltip("Area name written to the session log and the dashboard.")]
        [SerializeField] private string areaName = "andaime";
        [Tooltip("Walkable rectangle in meters (x = width, y = depth), already inset from the real edges.")]
        [SerializeField] private Vector2 size = new Vector2(2.4f, 0.7f);
        [Tooltip("Seconds outside before the guard reacts, so leaning over the edge does not trigger it.")]
        [SerializeField] private float exitDelay = 0.3f;
        [Tooltip("How far back inside the edge counts as returned (meters).")]
        [SerializeField] private float reentryDepth = 0.15f;

        [Header("Scene")]
        [SerializeField] private PhaseController phaseController;
        [SerializeField] private RecenterService recenterService;
        [Tooltip("OVRCameraRig root; the head (CenterEyeAnchor) is resolved under it.")]
        [SerializeField] private Transform playerRig;

        [Header("Feedback")]
        [Tooltip("Screen fade level while outside (0 = none, 1 = black).")]
        [SerializeField, Range(0f, 1f)] private float dimAlpha = 0.75f;
        [Tooltip("Column (PlayspaceBeacon shader) placed on the nearest safe point. Layer: PopupUI.")]
        [SerializeField] private Renderer beacon;
        [SerializeField] private float fadeSeconds = 0.35f;
        [Tooltip("Popup provider (PopupService) implementing IPopupFeedback. Optional.")]
        [SerializeField] private MonoBehaviour popupFeedbackProvider;
        [SerializeField] private string popupTitle = "Atenção: fora do andaime";
        [SerializeField, TextArea(2, 3)] private string popupBody = "Você saiu da área do andaime. Caminhe até a coluna de luz.";
        [SerializeField] private float popupSeconds = 5f;

        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        private WalkableZoneTracker _tracker;
        private Transform _head;
        private SafetyProto.Core.Interfaces.IPopupFeedback _popup;
        private MaterialPropertyBlock _block;
        private float _visibility;

        private void Awake()
        {
            _tracker = new WalkableZoneTracker(size, exitDelay, reentryDepth);
            _block = new MaterialPropertyBlock();
            _popup = popupFeedbackProvider as SafetyProto.Core.Interfaces.IPopupFeedback;
            if (popupFeedbackProvider != null && _popup == null)
                SafetyLog.Warning("[WalkableZoneGuard] popupFeedbackProvider não implementa IPopupFeedback.", this);
            ApplyVisibility(0f);
        }

        private void Update()
        {
            if (_head == null && playerRig != null)
                _head = PlayerRecenter.ResolveHead(playerRig);

            bool active = _head != null && phaseController != null && phaseController.IsInAndaimePhase
                          && (recenterService == null || !recenterService.IsBusy);

            if (!active)
            {
                // A teleport or session restart carries the participant elsewhere: no return to report.
                _tracker.Reset();
            }
            else
            {
                Vector3 body = RoomCenterCalibration.BodyAxis(_head.position, _head.rotation);
                Vector3 local = transform.InverseTransformPoint(body);
                var bodyInZone = new Vector2(local.x, local.z);

                switch (_tracker.Step(bodyInZone, Time.deltaTime))
                {
                    case WalkableZoneTracker.Transition.Left:
                        Publish(PlayspaceNoticeKind.LeftWalkableArea, 0f);
                        _popup?.TryShowIfIdle(popupTitle, popupBody, popupSeconds);
                        SafetyLog.Info($"[WalkableZoneGuard] Participante saiu da área caminhável ({areaName}).", this);
                        break;
                    case WalkableZoneTracker.Transition.Returned:
                        Publish(PlayspaceNoticeKind.ReturnedToWalkableArea, _tracker.OutsideSeconds);
                        SafetyLog.Info($"[WalkableZoneGuard] Participante voltou à área caminhável ({areaName}) após {_tracker.OutsideSeconds:0.0} s.", this);
                        break;
                }

                if (_tracker.IsOutside && beacon != null)
                {
                    Vector2 safe = _tracker.NearestSafePoint(bodyInZone);
                    beacon.transform.position = transform.TransformPoint(new Vector3(safe.x, 0f, safe.y))
                                                + Vector3.up * beacon.transform.lossyScale.y;
                }
            }

            float target = _tracker.IsOutside ? 1f : 0f;
            float step = fadeSeconds > 0f ? Time.deltaTime / fadeSeconds : 1f;
            float next = Mathf.MoveTowards(_visibility, target, step);
            if (!Mathf.Approximately(next, _visibility)) ApplyVisibility(next);
        }

        private void OnDisable()
        {
            _tracker?.Reset();
            ApplyVisibility(0f);
        }

        private void ApplyVisibility(float visibility)
        {
            _visibility = visibility;
            if (OVRScreenFade.instance != null)
                OVRScreenFade.instance.SetExplicitFade(visibility * dimAlpha);
            SetAlpha(beacon, visibility);
        }

        private void SetAlpha(Renderer target, float alpha)
        {
            if (target == null) return;
            target.enabled = alpha > 0f;
            target.GetPropertyBlock(_block);
            _block.SetFloat(AlphaId, alpha);
            target.SetPropertyBlock(_block);
        }

        private void Publish(PlayspaceNoticeKind kind, float durationSeconds)
        {
            EventBus.Instance?.Publish(new PlayspaceNoticeEventArgs
            {
                Kind = kind,
                Area = areaName,
                DurationSeconds = durationSeconds,
            });
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.25f, 0.75f, 1f, 1f);
            Gizmos.DrawWireCube(Vector3.up * 0.01f, new Vector3(size.x, 0f, size.y));
            Gizmos.color = new Color(0.25f, 0.75f, 1f, 0.4f);
            Gizmos.DrawWireCube(Vector3.up * 0.01f, new Vector3(size.x - 2f * reentryDepth, 0f, size.y - 2f * reentryDepth));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
