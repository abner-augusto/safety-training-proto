using System;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Runtime.Interaction
{
    /// <summary>Progress in the 0..1 range, for inspector-wired listeners.</summary>
    [Serializable]
    public class DwellProgressEvent : UnityEvent<float> { }

    /// <summary>
    /// Turns head-gaze frames into a timed dwell. Attach to the object that carries the
    /// GazeTarget-layer collider; <see cref="HeadGazeSource"/> finds it through
    /// <see cref="IGazeTarget"/>.
    ///
    /// The timer is ticked in LateUpdate, never in Update. HeadGazeSource raycasts in Update, and
    /// Unity guarantees every Update runs before every LateUpdate — so by the time this ticks, the
    /// "was I gazed this frame" flag is always current. Ticking in Update would depend on undefined
    /// component execution order and drop or double-count frames.
    /// </summary>
    public class GazeDwellTarget : MonoBehaviour, IGazeTarget
    {
        [Header("Timing — calibrate on device")]
        [Tooltip("Seconds of continuous head gaze required to complete the dwell. 2.0 is the authored " +
                 "starting point; on a Quest 3 this usually feels long, so 1.2-1.5 is worth trying.")]
        [SerializeField, Range(0.3f, 5f)] private float _dwellDuration = 2f;

        [Tooltip("Seconds of lost gaze the dwell forgives before it starts draining. Absorbs head " +
                 "tracking jitter and flicker at the collider edge.")]
        [SerializeField, Range(0f, 1f)] private float _graceSeconds = 0.2f;

        [Tooltip("Seconds a full ring takes to drain once the grace window expires. Shorter than the " +
                 "dwell duration on purpose, so looking away reads as a reset.")]
        [SerializeField, Range(0.05f, 2f)] private float _decaySeconds = 0.3f;

        [Header("Events")]
        [SerializeField] private DwellProgressEvent _onProgressChanged = new DwellProgressEvent();
        [SerializeField] private UnityEvent _onCompleted = new UnityEvent();

        public DwellProgressEvent OnProgressChanged => _onProgressChanged;
        public UnityEvent OnCompleted => _onCompleted;

        /// <summary>Code-side equivalent of <see cref="OnProgressChanged"/>.</summary>
        public event Action<float> ProgressChanged;

        /// <summary>Code-side equivalent of <see cref="OnCompleted"/>.</summary>
        public event Action Completed;

        private GazeDwellTimer _timer;
        private bool _gazedThisFrame;
        private float _lastReportedProgress = -1f;

        public float Progress => _timer?.Progress ?? 0f;

        public bool IsCompleted => _timer != null && _timer.State == GazeDwellState.Completed;

        private void Awake() => Rebuild();

        void IGazeTarget.OnGazed(float deltaTime) => _gazedThisFrame = true;

        private void LateUpdate()
        {
            if (_timer == null) return;

            bool justCompleted = _timer.Tick(_gazedThisFrame, Time.deltaTime);
            _gazedThisFrame = false;

            float progress = _timer.Progress;
            if (!Mathf.Approximately(progress, _lastReportedProgress))
            {
                _lastReportedProgress = progress;
                _onProgressChanged.Invoke(progress);
                ProgressChanged?.Invoke(progress);
            }

            if (justCompleted)
            {
                _onCompleted.Invoke();
                Completed?.Invoke();
            }
        }

        /// <summary>Returns the dwell to Idle. Used by session reset.</summary>
        public void ResetDwell()
        {
            Rebuild();
            _gazedThisFrame = false;
            _lastReportedProgress = -1f;
        }

        private void Rebuild() => _timer = new GazeDwellTimer(_dwellDuration, _graceSeconds, _decaySeconds);
    }
}
