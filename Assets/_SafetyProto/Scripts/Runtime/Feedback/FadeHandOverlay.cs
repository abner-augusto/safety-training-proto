using SafetyProto.Core;
using SafetyProto.Core.Events;
using UnityEngine;

namespace SafetyProto.Runtime.Feedback
{
    /// <summary>Renders the hands in this camera's depth-cleared overlay pass while something
    /// drawn in that pass would otherwise cover them: the screen fade (hands stay visible over
    /// a blackout) and a visible popup panel (hands reach its buttons in front of it). Outside
    /// those cases the hands are removed from the camera, because the cleared depth would
    /// defeat world occlusion.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class FadeHandOverlay : MonoBehaviour
    {
        [SerializeField] private OVRScreenFade screenFade;
        [SerializeField] private LayerMask handLayers;

        private Camera _camera;
        private bool _subscribed;
        private bool _popupVisible;
        private System.Action<PopupShownEventArgs> _onPopupShown;
        private System.Action<PopupClosedEventArgs> _onPopupClosed;

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            Subscribe();
            UpdateMask();
        }

        private void OnPreCull()
        {
            UpdateMask();
        }

        private void OnDisable()
        {
            Unsubscribe();
            _popupVisible = false;

            if (_camera != null) _camera.cullingMask &= ~handLayers.value;
        }

        private void Subscribe()
        {
            if (_subscribed) return;

            var bus = EventBus.Instance;
            if (bus == null) return;

            _onPopupShown ??= OnPopupShown;
            _onPopupClosed ??= OnPopupClosed;
            bus.Subscribe(_onPopupShown);
            bus.Subscribe(_onPopupClosed);
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;

            var bus = EventBus.Instance;
            if (bus != null)
            {
                bus.Unsubscribe(_onPopupShown);
                bus.Unsubscribe(_onPopupClosed);
            }

            _subscribed = false;
        }

        private void OnPopupShown(PopupShownEventArgs _) => _popupVisible = true;

        private void OnPopupClosed(PopupClosedEventArgs _) => _popupVisible = false;

        private void UpdateMask()
        {
            bool fading = screenFade != null && screenFade.isActiveAndEnabled && screenFade.currentAlpha > 0f;
            _camera.cullingMask = fading || _popupVisible
                ? _camera.cullingMask | handLayers.value
                : _camera.cullingMask & ~handLayers.value;
        }
    }
}
