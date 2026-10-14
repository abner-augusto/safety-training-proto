#nullable enable
using System;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;

namespace SafetyProto.Domain.Scoring
{
    public sealed class ScoreRuleEngineCore : IDisposable
    {
        private readonly IEventBus _bus;
        private readonly IScoreService _scoreService;
        private readonly IHarnessLogger? _logger;
        private readonly ScoringConfig _config;

        private readonly Action<TaskEventArgs> _onTaskLifecycle;

        private bool _subscribed;
        private bool _disposed;

        public ScoreRuleEngineCore(
            IEventBus bus,
            IScoreService scoreService,
            IHarnessLogger? logger = null,
            ScoringConfig? config = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _scoreService = scoreService ?? throw new ArgumentNullException(nameof(scoreService));
            _logger = logger;
            _config = config ?? ScoringConfig.Default;

            _onTaskLifecycle = HandleTaskLifecycle;
        }

        public void Subscribe()
        {
            if (_subscribed) return;
            _bus.Subscribe(_onTaskLifecycle);
            _subscribed = true;
        }

        public void Unsubscribe()
        {
            if (!_subscribed) return;
            _bus.Unsubscribe(_onTaskLifecycle);
            _subscribed = false;
        }

        private void HandleTaskLifecycle(TaskEventArgs args)
        {
            switch (args.Phase)
            {
                case TaskPhase.Completed: ApplyTaskCompletedScoring(args); break;
            }
        }

        internal void ApplyTaskCompletedScoring(TaskEventArgs args)
        {
            if (args.Task == null) return;

            var state = args.RuntimeTask?.State
                ?? (args.WasPpeCompliant ? TaskState.CompletedSuccess : TaskState.CompletedSuccessButUnsafe);

            if (state == TaskState.NotPerformed) return;

            int earned = state == TaskState.CompletedSuccessButUnsafe
                ? _config.UnsafeEarnFor(args.Task.riskLevel)
                : _config.PointsFor(args.Task.riskLevel);

            string reason = state == TaskState.CompletedSuccessButUnsafe
                ? $"Task '{args.Task.taskName}' completed without required PPE"
                : $"Task '{args.Task.taskName}' completed";

            if (earned > 0)
            {
                _scoreService.AddPoints(earned, reason, args.Task.id);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Unsubscribe();
            _disposed = true;
        }
    }
}
