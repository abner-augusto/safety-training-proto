using SafetyProto.Runtime.Interaction;
using UnityEngine;
using UnityEngine.UI;

namespace SafetyProto.UI
{
    /// <summary>
    /// Renders dwell progress as a radial fill anchored on the defect itself.
    ///
    /// Anchoring the ring on the tear rather than head-locking it means the ring marks <em>what</em>
    /// was found, not merely that something is loading — and since the participant is already
    /// looking at the tear, it lands near the centre of vision anyway, without the fatigue of HUD
    /// elements welded to the head.
    ///
    /// A plain world-space Canvas, deliberately not an OVROverlayCanvas: the overlay's depth slab
    /// clips geometry within roughly a centimetre of the canvas plane, which is exactly the range a
    /// ring sitting against the mesh occupies.
    /// </summary>
    public class DwellRingView : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Dwell whose progress this ring displays.")]
        [SerializeField] private GazeDwellTarget _dwellTarget;

        [Tooltip("Image with Image Type = Filled, Fill Method = Radial 360.")]
        [SerializeField] private Image _fillImage;

        [Tooltip("Canvas toggled with the ring. Leave empty to use the Canvas on this object.")]
        [SerializeField] private Canvas _canvas;

        [Tooltip("Transform rotated to face the camera. Leave empty to billboard this object.")]
        [SerializeField] private Transform _billboardRoot;

        private Transform _cameraTransform;

        private void Reset() => _canvas = GetComponent<Canvas>();

        private void Awake()
        {
            if (_canvas == null) _canvas = GetComponent<Canvas>();
            if (_billboardRoot == null) _billboardRoot = transform;
            SetVisible(false);
        }

        private void OnEnable()
        {
            if (_dwellTarget != null) _dwellTarget.ProgressChanged += HandleProgressChanged;
        }

        private void OnDisable()
        {
            if (_dwellTarget != null) _dwellTarget.ProgressChanged -= HandleProgressChanged;
        }

        private void HandleProgressChanged(float progress)
        {
            if (_fillImage != null) _fillImage.fillAmount = progress;
            SetVisible(progress > 0f);
        }

        private void LateUpdate()
        {
            if (_canvas == null || !_canvas.enabled) return;

            if (_cameraTransform == null)
            {
                var mainCamera = Camera.main;
                if (mainCamera == null) return;
                _cameraTransform = mainCamera.transform;
            }

            Vector3 away = _billboardRoot.position - _cameraTransform.position;
            if (away.sqrMagnitude > 1e-6f)
                _billboardRoot.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        // Canvas.enabled rather than SetActive: toggling the GameObject forces a full layout and
        // graphic rebuild every time the ring appears, which is wasted work on a GPU-bound target.
        private void SetVisible(bool visible)
        {
            if (_canvas != null) _canvas.enabled = visible;
        }
    }
}
