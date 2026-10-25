using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// Keeps a punch-a-hole <see cref="OVROverlayCanvas"/> visible while <see cref="OVRScreenFade"/>
    /// has the view blacked out. The canvas itself is composited behind the eye buffer and only
    /// shows through the hole its imposter quad cuts; the eye camera draws the fade after that
    /// quad, so the fade fills the hole back in. While the fade is up the imposter moves to a layer
    /// that only a camera rendering after the fade draws (the popup camera's).
    ///
    /// The imposter returns to its own layer once the fade clears, so the world and the hands
    /// occlude the canvas normally: the popup camera clears depth before it draws.
    /// </summary>
    [RequireComponent(typeof(OVROverlayCanvas))]
    public class OverlayCanvasAboveFade : MonoBehaviour
    {
        [Tooltip("Layer the imposter uses while the screen is faded. It must be drawn by a camera " +
                 "that renders after the eye camera's fade (PopupUI, drawn by PopupCamera).")]
        [SerializeField] private int fadeLayer = 16;

        private OVROverlayCanvas _canvas;
        private int _ownLayer;

        private void Awake()
        {
            _canvas = GetComponent<OVROverlayCanvas>();
            _ownLayer = _canvas.layer;
        }

        private void OnDisable() => _canvas.layer = _ownLayer;

        // OVROverlayCanvas copies its layer onto the imposter in its Update.
        private void LateUpdate()
        {
            var fade = OVRScreenFade.instance;
            bool faded = fade != null && fade.isActiveAndEnabled && fade.currentAlpha > 0f;
            _canvas.layer = faded ? fadeLayer : _ownLayer;
        }
    }
}
