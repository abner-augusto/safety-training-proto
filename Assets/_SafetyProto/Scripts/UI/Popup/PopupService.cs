using SafetyProto.Core.Logging;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.UI
{
    public class PopupService : MonoBehaviour
    {
        public static PopupService Instance { get; private set; }

        [SerializeField] private PopupPanel popupPanel;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            if (popupPanel == null)
                SafetyLog.Warning("[PopupService] popupPanel não atribuído no Inspector.", this);
        }

        public void Show(PopupData data)
        {
            if (popupPanel == null) return;
            popupPanel.Show(data);
        }

        public void Hide()
        {
            if (popupPanel == null) return;
            popupPanel.Hide();
        }

        public void ShowNormal(string title, string body)
        {
            Show(new PopupData { type = PopupType.Normal, title = title, body = body });
        }

        public void ShowWarning(string title, string body)
        {
            Show(new PopupData { type = PopupType.Warning, title = title, body = body });
        }

        public void ShowInteractive(string title, string body,
                                    string buttonLabel, UnityAction onAction)
        {
            var data = new PopupData
            {
                type = PopupType.Interactive,
                title = title,
                body = body,
                actionButtonLabel = buttonLabel,
                onActionPressed = new UnityEvent()
            };
            data.onActionPressed.AddListener(onAction);
            Show(data);
        }
    }
}
