using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Hides a content object while the popup panel is visible and restores it when the popup
    /// closes. Driven by <see cref="PopupPanel"/>'s own Shown/Hidden events rather than the
    /// session pause/resume events — opening this menu raises SessionPaused too, so reacting to
    /// that would wrongly hide the menu the instant it opens.
    ///
    /// Place this on an object that stays active (e.g. the menu root) and point <see cref="content"/>
    /// at the child to hide, so this component keeps listening while the content is hidden.
    /// </summary>
    public class HideWhilePopupVisible : MonoBehaviour
    {
        [Tooltip("Object hidden while a popup is visible. Defaults to the first child.")]
        [SerializeField] private GameObject content;

        [Tooltip("Popup panel to watch. Auto-found in the scene if left empty.")]
        [SerializeField] private PopupPanel popupPanel;

        private bool _subscribed;

        private void Awake()
        {
            if (content == null && transform.childCount > 0)
                content = transform.GetChild(0).gameObject;
        }

        private void OnEnable()
        {
            if (popupPanel == null)
                popupPanel = FindFirstObjectByType<PopupPanel>(FindObjectsInactive.Include);

            if (popupPanel == null || _subscribed)
                return;

            popupPanel.Shown += OnPopupShown;
            popupPanel.Hidden += OnPopupHidden;
            _subscribed = true;

            // Sync to the current popup state in case it's already open when we re-enable.
            if (content != null)
                content.SetActive(!popupPanel.IsVisible);
        }

        private void OnDisable()
        {
            if (!_subscribed || popupPanel == null)
                return;

            popupPanel.Shown -= OnPopupShown;
            popupPanel.Hidden -= OnPopupHidden;
            _subscribed = false;
        }

        private void OnPopupShown()
        {
            if (content != null) content.SetActive(false);
        }

        private void OnPopupHidden()
        {
            if (content != null) content.SetActive(true);
        }
    }
}
