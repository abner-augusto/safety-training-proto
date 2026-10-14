using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Metrics;
using SafetyProto.Domain.Sessions;
using UnityEngine;

namespace SafetyProto.Utils
{
    /// <summary>
    /// Records, for every frame of a participant session, the queued EventBus flush (duration,
    /// events dispatched, queue length, budget cut) and the engine's frame timings, then writes
    /// them to <c>persistentDataPath/perf/perf_&lt;stem&gt;.csv</c>, where the stem matches the
    /// session log's. Rows go to disk on session end, reset, application pause, focus loss and
    /// quit — never on a timer, so disk work never lands inside the measured frames.
    /// </summary>
    [DisallowMultipleComponent]
    public class EventBusPerfRecorder : MonoBehaviour, IEventBusFlushProbe, ISessionResettable
    {
        [Tooltip("Grava o custo do EventBus e o tempo de quadro de cada sessão em perf/*.csv. Desligue para não coletar.")]
        [SerializeField] private bool recordingEnabled = true;

        [Tooltip("Quadros reservados por sessão (200 000 ≈ 46 min a 72 Hz, ~9,6 MB). Além disso, a gravação para.")]
        [SerializeField, Min(1000)] private int capacityFrames = 200_000;

        /// <summary>The recorder in the scene, so the session logger can embed its summary.</summary>
        public static EventBusPerfRecorder Current { get; private set; }

        private readonly object _ioLock = new object();
        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private FramePerfBuffer _buffer;
        private FramePerfCsvHeader _header;
        private string _path;
        private string _sessionId;
        private bool _recording;
        private bool _fullReported;
        private int _writtenRows;
        private bool _headerWritten;

        private bool _frameFlushed;
        private long _frameFlushTicks;
        private int _frameDispatched;
        private int _frameQueueBefore;
        private bool _frameCut;

        private void Awake()
        {
            if (!recordingEnabled || !this.IsEventBusReady())
            {
                enabled = false;
                return;
            }

            _buffer = new FramePerfBuffer(capacityFrames);
            Current = this;
            EventBus.FlushProbe = this;
            EventBus.Instance.onSessionStarted.AddListener(OnSessionStarted);
            EventBus.Instance.onSessionCompleted.AddListener(StopBeforeSessionLoggerSummary);
            EventBus.Instance.onSessionEnded.AddListener(OnSessionEnded);
        }

        private void OnDestroy()
        {
            if (_buffer == null) return;

            if (ReferenceEquals(EventBus.FlushProbe, this)) EventBus.FlushProbe = null;
            if (Current == this) Current = null;
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onSessionStarted.RemoveListener(OnSessionStarted);
                EventBus.Instance.onSessionCompleted.RemoveListener(StopBeforeSessionLoggerSummary);
                EventBus.Instance.onSessionEnded.RemoveListener(OnSessionEnded);
            }

            StopAndWrite(background: false);
        }

        public void OnFlush(int queueBefore, int dispatched, long elapsedTicks, bool cutByBudget)
        {
            if (!_frameFlushed) _frameQueueBefore = queueBefore;
            _frameFlushed = true;
            _frameFlushTicks += elapsedTicks;
            _frameDispatched += dispatched;
            _frameCut |= cutByBudget;
        }

        private void LateUpdate()
        {
            if (_recording) CommitFrame();

            _frameFlushed = false;
            _frameFlushTicks = 0;
            _frameDispatched = 0;
            _frameQueueBefore = 0;
            _frameCut = false;
        }

        private void CommitFrame()
        {
            FrameTimingManager.CaptureFrameTimings();
            bool timed = FrameTimingManager.GetLatestTimings(1, _timings) > 0;
            ref readonly var t = ref _timings[0];

            var row = new FramePerfRow
            {
                UtcMs = EventContext.NowUnixMs(),
                Frame = Time.frameCount,
                FlushTicks = _frameFlushTicks > int.MaxValue ? int.MaxValue : (int)_frameFlushTicks,
                Dispatched = _frameDispatched,
                QueueBefore = _frameQueueBefore,
                CutByBudget = _frameCut,
                MainThreadMs = timed ? (float)t.cpuMainThreadFrameTime : FramePerfRow.Unavailable,
                PresentWaitMs = timed ? (float)t.cpuMainThreadPresentWaitTime : FramePerfRow.Unavailable,
                RenderThreadMs = timed ? (float)t.cpuRenderThreadFrameTime : FramePerfRow.Unavailable,
                GpuMs = timed ? (float)t.gpuFrameTime : FramePerfRow.Unavailable
            };

            if (_buffer.TryAppend(row) || _fullReported) return;
            _fullReported = true;
            SafetyLog.Warning($"[EventBusPerfRecorder] Buffer de {_buffer.Capacity} quadros cheio — o restante da sessão não será medido.", this);
        }

        private void OnSessionStarted(SessionStartedEventArgs args)
        {
            StopAndWrite(background: false);

            if ((args.PlayerId ?? string.Empty).StartsWith("SIM-", StringComparison.OrdinalIgnoreCase))
                return;

            lock (_ioLock)
            {
                _buffer.Clear();
                _writtenRows = 0;
                _headerWritten = false;
                _fullReported = false;
                _sessionId = args.SessionId ?? string.Empty;
                _header = new FramePerfCsvHeader
                {
                    SessionId = _sessionId,
                    ParticipantId = args.PlayerId ?? string.Empty,
                    AppVersion = Application.version,
                    Commit = BuildCommit.Current,
                    StopwatchFrequency = Stopwatch.Frequency
                };
                _path = Path.Combine(Application.persistentDataPath, "perf",
                    $"perf_{SessionFileStem.Of(args.TimestampMs, args.SessionId)}.csv");
            }
            _recording = true;
        }

        private void StopBeforeSessionLoggerSummary(SessionCompletedEventArgs _) => StopAndWrite(background: true);

        private void OnSessionEnded(SessionEndedEventArgs _) => StopAndWrite(background: true);

        public void ResetSession() => StopAndWrite(background: false);

        private void OnApplicationPause(bool paused)
        {
            if (paused) WritePending();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) WritePending();
        }

        private void OnApplicationQuit() => StopAndWrite(background: false);

        /// <summary>Summary of the recorded session, or null when <paramref name="sessionId"/>
        /// is not the session this recorder measured.</summary>
        public PerfSummary BuildSummary(string sessionId)
        {
            if (_buffer == null || _sessionId == null || _sessionId != sessionId) return null;
            return PerfStats.Summarize(_buffer, Stopwatch.Frequency);
        }

        private void StopAndWrite(bool background)
        {
            bool wasRecording = _recording;
            _recording = false;
            if (!wasRecording && (_buffer == null || _writtenRows >= _buffer.Count)) return;

            if (background) _ = Task.Run(WritePending);
            else WritePending();
        }

        /// <summary>Appends the rows not yet on disk. Safe to call from any thread.</summary>
        private void WritePending()
        {
            lock (_ioLock)
            {
                if (_buffer == null || _path == null) return;
                int count = _buffer.Count;
                if (count <= _writtenRows && _headerWritten) return;

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    using (var writer = new StreamWriter(_path, append: _headerWritten))
                    {
                        if (!_headerWritten) FramePerfCsv.WriteHeader(writer, _header);
                        FramePerfCsv.WriteRows(writer, _buffer, _writtenRows, count, _header.StopwatchFrequency);
                    }
                    _headerWritten = true;
                    _writtenRows = count;
                }
                catch (Exception ex)
                {
                    SafetyLog.Error($"[EventBusPerfRecorder] Falha ao gravar {_path}: {ex.Message}");
                }
            }
        }
    }
}
