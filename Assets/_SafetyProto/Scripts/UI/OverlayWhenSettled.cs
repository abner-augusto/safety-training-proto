using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Interaction;
using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Puts a head-following canvas on its <see cref="OVROverlayCanvas"/> layer only while it is
    /// at rest and fully faded in, and keeps it in the eye buffer otherwise (see
    /// <see cref="OverlaySettlePolicy"/>). The layer is what makes the text sharp, so the canvas
    /// gets it back as soon as it stops.
    ///
    /// Leave <c>overlayEnabled</c> off on the canvas itself: this component turns it on.
    /// </summary>
    // Runs after MenuFollowHmd.LateUpdate, so the motion check sees this frame's placement.
    [DefaultExecutionOrder(100)]
    public class OverlayWhenSettled : MonoBehaviour
    {
        [Tooltip("Overlay canvas to switch. Auto-found in children if left empty.")]
        [SerializeField] private OVROverlayCanvas overlayCanvas;

        [Tooltip("Follow driver whose motion keeps the canvas off the layer. Auto-found on this object if left empty.")]
        [SerializeField] private MenuFollowHmd follow;

        [Tooltip("Fade driver; the canvas stays off the layer until it is fully opaque. Auto-found on this object if left empty.")]
        [SerializeField] private CanvasGroup canvasGroup;

        [Tooltip("Seconds the canvas must hold still before it moves onto the layer.")]
        [Min(0f)]
        [SerializeField] private float settleSeconds = 0.3f;

        private const float StillPositionMetres = 0.0005f;
        private const float StillRotationDegrees = 0.1f;

        private Vector3 _lastPosition;
        private Quaternion _lastRotation;
        private float _stillFor;

        private void Awake()
        {
            if (overlayCanvas == null) overlayCanvas = GetComponentInChildren<OVROverlayCanvas>(true);
            if (follow == null) follow = GetComponent<MenuFollowHmd>();
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        }

        private void OnEnable()
        {
            _stillFor = 0f;
            _lastPosition = transform.position;
            _lastRotation = transform.rotation;
            SetLayer(false);
        }

        private void OnDisable()
        {
            // The next Show starts with a fade, so it must start in the eye buffer.
            SetLayer(false);
        }

        private void LateUpdate()
        {
            bool moved = (transform.position - _lastPosition).sqrMagnitude > StillPositionMetres * StillPositionMetres
                         || Quaternion.Angle(transform.rotation, _lastRotation) > StillRotationDegrees;
            _lastPosition = transform.position;
            _lastRotation = transform.rotation;

            _stillFor = moved ? 0f : _stillFor + Time.unscaledDeltaTime;

            SetLayer(OverlaySettlePolicy.ShouldUseLayer(
                follow != null && follow.IsFollowing,
                canvasGroup == null ? 1f : canvasGroup.alpha,
                _stillFor, settleSeconds));
        }

        private void SetLayer(bool on)
        {
            if (overlayCanvas == null || overlayCanvas.overlayEnabled == on) return;
            overlayCanvas.overlayEnabled = on;
            SafetyLog.Info($"[OverlayWhenSettled] {name}: compositor layer {(on ? "on" : "off")}.", this);
        }
    }
}
