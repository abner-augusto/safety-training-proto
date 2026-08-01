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

            // When RuntimeTask is null the emitter does not own the runtime instance and conveys
            // PPE compliance via WasPpeCompliant instead (see TaskEventArgs docs; SafetyRuleEngineCore
            // publishes completions this way). Honor that flag so a non-compliant completion is scored
            // as unsafe and the ppePenalty below actually applies — otherwise the penalty branch is
            // unreachable through the wired stack. WasPpeCompliant defaults to true, so compliant
            // completions and RuntimeTask-bearing events are unaffected.
            var state = args.RuntimeTask?.State
                ?? (args.WasPpeCompliant ? TaskState.CompletedSuccess : TaskState.CompletedSuccessButUnsafe);

            if (state == TaskState.NotPerformed) return;

            // Severity-driven earning: a safe completion earns the tier's full points;
            // an unsafe completion earns points × unsafeFactor (critical = 0). No
            // separate penalty subtraction — the reduced earning IS the penalty, so an
            // unsafe completion can never net more than the tier's factor allows.
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
