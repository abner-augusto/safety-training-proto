using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Runtime.Session;
using SafetyProto.Utils;
using TMPro;
using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Shows the current participant identifier (e.g. "Participante: P-07") on an on-device UI
    /// such as the connection/restart menu. Refreshes on enable and whenever a session starts
    /// (when the id is assigned).
    /// </summary>
    [DisallowMultipleComponent]
    public class ParticipantIdDisplay : MonoBehaviour
    {
        [Tooltip("Label to write into. Defaults to a TextMeshProUGUI on this object.")]
        [SerializeField] private TextMeshProUGUI targetLabel;
        [SerializeField] private string prefix = "Participante: ";
        [SerializeField] private string emptyText = "Participante: —";

        private bool _subscribed;

        private void Awake()
        {
            if (targetLabel == null)
                targetLabel = GetComponent<TextMeshProUGUI>();
        }

        private void OnEnable()
        {
            Refresh();

            if (this.IsEventBusReady())
            {
                EventBus.Instance.Subscribe<SessionStartedEventArgs>(OnSessionStarted);
                _subscribed = true;
            }
        }

        private void OnDisable()
        {
            if (_subscribed && EventBus.Instance != null)
                EventBus.Instance.Unsubscribe<SessionStartedEventArgs>(OnSessionStarted);
            _subscribed = false;
        }

        private void OnSessionStarted(SessionStartedEventArgs _) => Refresh();

        public void Refresh()
        {
            if (targetLabel == null) return;

            string id = ParticipantIdentity.CurrentId;
            if (string.IsNullOrEmpty(id))
            {
                targetLabel.text = emptyText;
                return;
            }

            targetLabel.text = $"{prefix}{id}";
        }
    }
}
