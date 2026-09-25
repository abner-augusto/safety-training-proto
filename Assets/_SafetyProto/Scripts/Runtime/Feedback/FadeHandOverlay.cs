using UnityEngine;

namespace SafetyProto.Runtime.Feedback
{
    /// <summary>Renders hands above the blackout only while the screen fade is visible.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class FadeHandOverlay : MonoBehaviour
    {
        [SerializeField] private OVRScreenFade screenFade;
        [SerializeField] private LayerMask handLayers;

        private Camera _camera;

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            UpdateMask();
        }

        private void OnPreCull()
        {
            UpdateMask();
        }

        private void UpdateMask()
        {
            bool fading = screenFade != null && screenFade.isActiveAndEnabled && screenFade.currentAlpha > 0f;
            // The overlay clears world depth, so rendering hands here outside a fade defeats occlusion.
            _camera.cullingMask = fading
                ? _camera.cullingMask | handLayers.value
                : _camera.cullingMask & ~handLayers.value;
        }

        private void OnDisable()
        {
            if (_camera != null) _camera.cullingMask &= ~handLayers.value;
        }
    }
}
