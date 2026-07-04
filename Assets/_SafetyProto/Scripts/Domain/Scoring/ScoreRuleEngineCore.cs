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

        private readonly Action<TaskEventArgs> _onTaskLifecycle;

        private bool _subscribed;
        private bool _disposed;

        public ScoreRuleEngineCore(
            IEventBus bus,
            IScoreService scoreService,
            IHarnessLogger? logger = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _scoreService = scoreService ?? throw new ArgumentNullException(nameof(scoreService));
            _logger = logger;

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
                case TaskPhase.Timeout:   ApplyTaskTimeoutScoring(args); break;
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

            if (state == TaskState.CompletedFailure) return;

            if (args.Task.successPoints > 0)
            {
                _scoreService.AddPoints(args.Task.successPoints, $"Task '{args.Task.taskName}' completed", args.Task.id);
            }

            if (state == TaskState.CompletedSuccessButUnsafe)
            {
                int penalty = args.Task.ppePenalty;
                if (penalty > 0)
                {
                    _scoreService.SubtractPoints(penalty, $"Safety Violation: Missing PPE during '{args.Task.taskName}'", args.Task.id);
                }
            }
        }

        internal void ApplyTaskTimeoutScoring(TaskEventArgs args)
        {
            if (args.Task == null) return;
            if (args.Task.failurePenalty > 0)
                _scoreService.SubtractPoints(args.Task.failurePenalty, $"Task '{args.Task.taskName}' timed out", args.Task.id);
        }

        public void Dispose()
        {
            if (_disposed) return;
            Unsubscribe();
            _disposed = true;
        }
    }
}
