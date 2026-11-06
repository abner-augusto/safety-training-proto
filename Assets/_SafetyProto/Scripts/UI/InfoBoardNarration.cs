using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Interaction;
using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Narrates a world-space info board the first time the participant reads it: the board's centre
    /// must stay inside a view cone of the HMD, within range, for a short dwell. The line plays once per
    /// session through the shared <see cref="NarrationVoice"/>.
    ///
    /// The board is a reading aid, so it never talks over anything: the dwell only counts while the
    /// voice is idle and no popup is open, and its line is interruptible, so a warning popup cuts it.
    /// A line cut before its end is not counted as heard and plays again the next time the participant
    /// looks at the board.
    /// </summary>
    public class InfoBoardNarration : MonoBehaviour
    {
        [Header("Narration")]
        [Tooltip("Shared voice channel (the same one the popups and DICA tips use).")]
        [SerializeField] private NarrationVoice narrationVoice;
        [SerializeField] private AudioClip narration;

        [Header("Gaze")]
        [Tooltip("Participant's head (CenterEyeAnchor). Empty = the camera tagged MainCamera.")]
        [SerializeField] private Transform head;
        [Tooltip("Point the participant must look at. Empty = this transform (panel center).")]
        [SerializeField] private Transform lookTarget;
        [Tooltip("Maximum angle (degrees) between the gaze direction and the panel center.")]
        [SerializeField, Range(1f, 60f)] private float maxAngle = 20f;
        [Tooltip("Maximum distance (m) between the head and the panel center.")]
        [SerializeField, Min(0.1f)] private float maxDistance = 4f;
        [Tooltip("Time (s) spent looking at the panel before the narration starts.")]
        [SerializeField, Min(0.05f)] private float dwellSeconds = 1f;
        [Tooltip("Gaze losses shorter than this (s) do not interrupt the count.")]
        [SerializeField, Min(0f)] private float graceSeconds = 0.3f;

        // A line that stopped this close to its end finished on its own.
        private const float EndTolerance = 0.5f;

        private GazeDwellTimer _dwell;
        private bool _heard;
        private bool _playing;
        private float _startedAt;

        /// <summary>True once the board's line has played to its end.</summary>
        public bool Heard => _heard;

        private void Awake()
        {
            _dwell = new GazeDwellTimer(dwellSeconds, graceSeconds, decaySeconds: 0.5f);
        }

        private void Start()
        {
            if (narration == null)
                SafetyLog.Warning($"[InfoBoardNarration] Não há narração atribuída ao painel '{name}'.", this);
            if (narrationVoice == null)
                SafetyLog.Warning($"[InfoBoardNarration] NarrationVoice não atribuído ao painel '{name}'.", this);
        }

        private void OnDisable()
        {
            if (_playing && narrationVoice != null) narrationVoice.StopIfCurrent(narration);
            _playing = false;
            _dwell?.Reset();
        }

        private void Update()
        {
            if (_heard || narration == null || narrationVoice == null) return;

            if (_playing)
            {
                if (narrationVoice.IsPlaying(narration)) return;
                _playing = false;
                if (Time.unscaledTime - _startedAt >= narration.length - EndTolerance)
                {
                    _heard = true;
                    return;
                }
                SafetyLog.Info($"[InfoBoardNarration] Narração do painel '{name}' interrompida; toca de novo no próximo olhar.", this);
                _dwell.Reset();
            }

            var eye = ResolveHead();
            if (eye == null) return;

            var target = lookTarget != null ? lookTarget : transform;
            bool looking = !narrationVoice.IsBusy
                           && !(PopupService.Instance != null && PopupService.Instance.IsDisplayed)
                           && ViewCone.Contains(eye.position, eye.forward, target.position, maxAngle, maxDistance);

            if (!_dwell.Tick(looking, Time.unscaledDeltaTime)) return;

            SafetyLog.Info($"[InfoBoardNarration] Participante leu o painel '{name}' — narrando.", this);
            narrationVoice.PlayInterruptible(narration);
            _playing = true;
            _startedAt = Time.unscaledTime;
        }

        private Transform ResolveHead()
        {
            if (head != null) return head;
            var mainCamera = Camera.main;
            if (mainCamera != null) head = mainCamera.transform;
            return head;
        }
    }
}
