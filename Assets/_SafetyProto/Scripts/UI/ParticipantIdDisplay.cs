using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Runtime.Session;
using SafetyProto.Utils;
using TMPro;
using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Shows the current participant identifier (e.g. "Participante: P-7F3A") on an on-device UI
    /// such as the connection/restart menu. Because this label lives only in the headset (not the
    /// LAN dashboard), it may optionally include the private first name to help the operator.
    /// Refreshes on enable and whenever a session starts (when the id is assigned).
    /// </summary>
    [DisallowMultipleComponent]
    public class ParticipantIdDisplay : MonoBehaviour
    {
        [Tooltip("Label to write into. Defaults to a TextMeshProUGUI on this object.")]
        [SerializeField] private TextMeshProUGUI targetLabel;
        [SerializeField] private string prefix = "Participante: ";
        [Tooltip("Append the private first name (on-device only — never sent to the dashboard).")]
        [SerializeField] private bool includeName = true;
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
                EventBus.Instance.onSessionStarted.AddListener(OnSessionStarted);
                _subscribed = true;
            }
        }

        private void OnDisable()
        {
            if (_subscribed && EventBus.Instance != null)
                EventBus.Instance.onSessionStarted.RemoveListener(OnSessionStarted);
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

            string text = $"{prefix}{id}";
            if (includeName && !string.IsNullOrEmpty(ParticipantIdentity.CurrentName))
                text += $" — {ParticipantIdentity.CurrentName}";

            targetLabel.text = text;
        }
    }
}
