using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// Centers the player on a chosen start point when the session starts. With a
    /// <see cref="recenterService"/> it first records the participant's position as the room
    /// center (unless the evaluator already calibrated one), then places that center over
    /// <see cref="startPoint"/>; without one the head lands there via <see cref="PlayerRecenter"/>.
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

        [Tooltip("Owns the room-center calibration. When empty, the live head is centered instead.")]
        [SerializeField] private RecenterService recenterService;

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
            EventBus.Instance.Subscribe<SessionStartedEventArgs>(OnSessionStarted);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
                EventBus.Instance.Unsubscribe<SessionStartedEventArgs>(OnSessionStarted);
        }

        private void OnSessionStarted(SessionStartedEventArgs _)
        {
            if (_done && !recenterOnEverySessionStart) return;
            if (playerRig == null || startPoint == null) return;

            if (recenterService != null)
            {
                recenterService.CaptureRoomCenter(RoomCenterSource.SessionStart);
                recenterService.PlaceOn(startPoint);
            }
            else
            {
                if (playerHead == null)
                    playerHead = PlayerRecenter.ResolveHead(playerRig);
                PlayerRecenter.Recenter(playerRig, playerHead, startPoint);
            }
            _done = true;

            SafetyLog.Info("[PlayerSpawnCenter] Jogador centralizado no ponto de partida.", this);
        }
    }
}
