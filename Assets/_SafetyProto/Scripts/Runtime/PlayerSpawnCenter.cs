using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// B8 — centers the player on a chosen start point when the session starts, cancelling the
    /// room-scale offset via <see cref="PlayerRecenter"/> so the head (not the rig origin) lands
    /// over <see cref="startPoint"/>.
    ///
    /// Wire in the Inspector: <see cref="playerRig"/> (OVRCameraRig root), optional
    /// <see cref="playerHead"/> (auto-resolves CenterEyeAnchor if empty), and <see cref="startPoint"/>.
    /// </summary>
    public class PlayerSpawnCenter : MonoBehaviour
    {
        [Header("Player")]
        [Tooltip("OVRCameraRig root (room-scale origin).")]
        [SerializeField] private Transform playerRig;
        [Tooltip("Head transform (CenterEyeAnchor). Auto-resolved from playerRig if empty.")]
        [SerializeField] private Transform playerHead;

        [Header("Spawn")]
        [Tooltip("Where the player's head should end up at session start.")]
        [SerializeField] private Transform startPoint;

        [Tooltip("Recenter again every time the session restarts (SessionStarted re-fires).")]
        [SerializeField] private bool recenterOnEverySessionStart = true;

        private bool _done;

        private void Start()
        {
            if (playerHead == null && playerRig != null)
                playerHead = PlayerRecenter.ResolveHead(playerRig);

            if (playerRig == null)
                SafetyLog.Warning("[PlayerSpawnCenter] playerRig não atribuído no Inspector.", this);
            if (startPoint == null)
                SafetyLog.Warning("[PlayerSpawnCenter] startPoint não atribuído no Inspector.", this);

            if (!this.IsEventBusReady()) return;
            EventBus.Instance.onSessionStarted.AddListener(OnSessionStarted);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
                EventBus.Instance.onSessionStarted.RemoveListener(OnSessionStarted);
        }

        private void OnSessionStarted(SessionStartedEventArgs _)
        {
            if (_done && !recenterOnEverySessionStart) return;
            if (playerRig == null || startPoint == null) return;

            if (playerHead == null)
                playerHead = PlayerRecenter.ResolveHead(playerRig);

            PlayerRecenter.Recenter(playerRig, playerHead, startPoint);
            _done = true;

            SafetyLog.Info("[PlayerSpawnCenter] Jogador centralizado no ponto de partida.", this);
        }
    }
}
