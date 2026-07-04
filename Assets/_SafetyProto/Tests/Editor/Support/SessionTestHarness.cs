#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Safety;
using SafetyProto.Domain.Scenarios;
using SafetyProto.Domain.Scoring;
using SafetyProto.Domain.Sessions;
using SafetyProto.Domain.Tasks;

namespace SafetyProto.Tests.Editor.Support
{
    /// <summary>
    /// Wires the FULL engine-independent domain stack — <see cref="TaskManagerCore"/> +
    /// <see cref="ScoreService"/> + <see cref="SafetyRuleEngineCore"/> +
    /// <see cref="ScoreRuleEngineCore"/> + <see cref="SessionLoggerCore"/> — through one real,
    /// in-process <see cref="FakeEventBus"/>, then drives it end-to-end. The wiring here is a
    /// faithful mirror of the production hosts (Unity's system MonoBehaviours and
    /// <c>Tools/CliHarness/Program.cs</c>): same components, same subscribe order, same
    /// score-change→event bridge. Only the outer shells differ.
    ///
    /// ── DRIVER vs STUBS (Reviewer E: "which component drives, which are stubbed?") ──
    ///
    /// DRIVER  — the test body, via <see cref="WearPpe"/> / <see cref="RemovePpe"/> /
    ///           <see cref="Attempt"/>. These publish the exact same events a human player
    ///           would generate through the VR interaction layer (PPE trigger colliders,
    ///           grab/socket actions). This is the same role the CLI harness's
    ///           <c>ScriptedActor</c> plays; the integration test IS the scripted actor.
    ///
    /// STUBS   — <see cref="FakeEventBus"/> stands in for Unity's deferred <c>EventBus</c>
    ///           ScriptableObject: it is a real in-process bus that enqueues then drains, so it
    ///           reproduces the production frame-boundary dispatch semantics deterministically
    ///           and synchronously (no Unity, no physics, no wall-clock). The PPE "sensor" is
    ///           also stubbed: PPE compliance is judged inside <see cref="SafetyRuleEngineCore"/>
    ///           from the <see cref="PPEStateChangedEventArgs"/> event cache, so the
    ///           <see cref="WearPpe"/>/<see cref="RemovePpe"/> events ARE the stubbed
    ///           equivalent of the PPE state callbacks — no separate
    ///           PPE-manager stub object is needed for the rule engine to see compliance state.
    ///
    /// REAL    — everything else (<see cref="TaskManagerCore"/>, <see cref="ScoreService"/>,
    ///           both rule engines, <see cref="SessionLoggerCore"/>) is the genuine production
    ///           code under test, unmodified. The <c>timer</c> and <c>scheduler</c> are passed
    ///           null (matching <c>Program.cs</c>): with no scheduler and zero inter-task delay
    ///           the orchestration runs synchronously, so the whole session resolves inside the
    ///           call that publishes the triggering event.
    ///
    /// Engine-independent by construction: this file references zero UnityEngine types, so the
    /// SAME .cs compiles into a standalone .NET test project (the headless path established in
    /// T3, mirroring <c>SafetyProto.Shared.csproj</c>).
    /// </summary>
    public sealed class SessionTestHarness : IDisposable
    {
        public FakeEventBus Bus { get; }
        public ScoreService Score { get; }
        public SafetyRuleEngineCore RuleEngine { get; }
        public ScoreRuleEngineCore ScoreRuleEngine { get; }
        public TaskManagerCore TaskManager { get; }
        public SessionLoggerCore SessionLogger { get; }

        private bool _disposed;

        public SessionTestHarness(IReadOnlyList<ITaskGroup> taskGroups, string scenarioName = "TestScenario", string participantId = "P-TEST")
        {
            if (taskGroups == null) throw new ArgumentNullException(nameof(taskGroups));

            Bus = new FakeEventBus();
            Score = new ScoreService();

            RuleEngine = new SafetyRuleEngineCore(bus: Bus, timer: null, logger: null, verboseLogging: false);
            RuleEngine.Subscribe();

            ScoreRuleEngine = new ScoreRuleEngineCore(bus: Bus, scoreService: Score, logger: null);
            ScoreRuleEngine.Subscribe();

            // Bridge: ScoreService raises a C# event; the production hosts republish it onto the
            // bus as a ScoreChangedEventArgs so downstream (logger/UI/dashboard) can observe it.
            // Mirrors Program.cs and ScoreManagerAdapter.HandleScoreChanged.
            Score.ScoreChanged += OnScoreChanged;

            TaskManager = new TaskManagerCore(
                bus: Bus,
                scoreService: Score,
                taskGroups: taskGroups,
                timer: null,
                scheduler: null,        // null scheduler + 0 delay ⇒ synchronous orchestration
                logger: null,
                delayBetweenTasks: 0f);
            TaskManager.Subscribe();

            // Wired for full-stack fidelity. A no-op serializer + temp dir keep it headless-safe
            // (no System.Text.Json / JsonUtility dependency, no meaningful file IO to assert on);
            // its presence proves the whole stack coexists on one bus without error.
            SessionLogger = new SessionLoggerCore(
                eventBus: Bus,
                outputDirectory: Path.Combine(Path.GetTempPath(), "SafetyProtoTests"),
                serialize: _ => string.Empty,
                logger: null);
            SessionLogger.Subscribe();

            EventContext.StartSession(
                sessionId: Guid.NewGuid().ToString(),
                playerId: participantId,
                scenarioId: scenarioName);
        }

        private void OnScoreChanged(int newScore, int delta, string reason, string taskId)
        {
            Bus.Publish(new ScoreChangedEventArgs(newScore, delta) { TaskId = taskId, Reason = reason });
        }

        /// <summary>Publishes SessionStarted, then kicks off the first task group.</summary>
        public void StartSession()
        {
            Bus.Publish(new SessionStartedEventArgs());
            TaskManager.StartSession();
        }

        /// <summary>DRIVER: player equips a PPE item (stubbed sensor input).</summary>
        public void WearPpe(PPEType ppe) => Bus.Publish(new PPEStateChangedEventArgs(ppe, true));

        /// <summary>DRIVER: player removes a PPE item.</summary>
        public void RemovePpe(PPEType ppe) => Bus.Publish(new PPEStateChangedEventArgs(ppe, false));

        /// <summary>DRIVER: player performs an action (stubbed interaction input).</summary>
        public void Attempt(string actionId) => Bus.Publish(new ActionAttemptedEvent(actionId));

        /// <summary>
        /// DRIVER: replays a scenario's scripted playthrough synchronously (delays ignored —
        /// the FakeEventBus resolves each step's full cascade before the next is published).
        /// This is the synchronous twin of the CLI harness's <c>ScriptedActor.PlayAsync</c>,
        /// so a JSON scenario's own <c>script</c> block drives the domain stack identically to
        /// the CLI run — that is exactly the CLI↔domain parity being codified.
        /// </summary>
        public void ReplayScript(IReadOnlyList<ScriptStepDef> steps)
        {
            if (steps == null) return;
            foreach (var step in steps)
            {
                switch (step.Kind)
                {
                    case "ppe":
                        if (Enum.TryParse<PPEType>(step.PpeType, ignoreCase: true, out var ppe))
                            Bus.Publish(new PPEStateChangedEventArgs(ppe, step.IsWearing));
                        break;
                    case "action":
                        Bus.Publish(new ActionAttemptedEvent(step.ActionId ?? string.Empty));
                        break;
                }
            }
        }

        public IReadOnlyList<RuntimeSafetyTask> SessionTasks => TaskManager.GetSessionTasks();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Score.ScoreChanged -= OnScoreChanged;
            SessionLogger.Dispose();
            TaskManager.Dispose();
            ScoreRuleEngine.Dispose();
            RuleEngine.Dispose();
            EventContext.Clear();
        }
    }
}
