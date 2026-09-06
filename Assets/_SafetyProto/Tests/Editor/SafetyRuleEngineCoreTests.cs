using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Safety;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    public class SafetyRuleEngineCoreTests
    {
        private FakeEventBus _bus = null!;
        private FakeTaskBuilder _tasks = null!;
        private List<TaskEventArgs> _taskCompletions = null!;
        private List<SafetyViolationEventArgs> _violations = null!;
        private SafetyRuleEngineCore _engine = null!;

        [SetUp]
        public void Setup()
        {
            _bus = new FakeEventBus();
            _tasks = new FakeTaskBuilder();
            _taskCompletions = new List<TaskEventArgs>();
            _violations = new List<SafetyViolationEventArgs>();

            _bus.Subscribe<TaskEventArgs>(args =>
            {
                if (args.Phase == TaskPhase.Completed)
                {
                    _taskCompletions.Add(args);
                }
            });
            _bus.Subscribe<SafetyViolationEventArgs>(args => _violations.Add(args));

            _engine = new SafetyRuleEngineCore(_bus);
            _engine.Subscribe();
        }

        [TearDown]
        public void TearDown()
        {
            _engine.Dispose();
        }

        [TearDown]
        public void ResetSessionMode() => SessionModeState.Reset();

        // ── Evaluation-mode free-order override ──────────────────────────────────

        [Test]
        public void EvaluationMode_SequentialGroup_CompletesEquipTasksInAnyOrder()
        {
            SessionModeState.Current = SessionMode.Evaluation;

            var boots = _tasks.Task("Botas", "", PPEType.Boots);
            var gloves = _tasks.Task("Luvas", "", PPEType.GloveLeft, PPEType.GloveRight);
            var group = _tasks.Group("EPIs", TaskExecutionModeShared.Sequential, boots, gloves);

            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started));

            // Reverse order: gloves first, then boots — both must complete.
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.GloveLeft, true));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.GloveRight, true));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Boots, true));

            var completions = _taskCompletions.Select(e => e.Task.taskName).ToList();
            CollectionAssert.AreEquivalent(new[] { "Luvas", "Botas" }, completions);
        }

        [Test]
        public void GuidedMode_SequentialGroup_StillWaitsForActiveTask()
        {
            // Guard: the EffectiveMode change must not alter Guided semantics.
            // (Same arrange as above but mode stays Guided and only the group's
            // first task may complete before a TaskStarted advances the cursor.)
            var boots = _tasks.Task("Botas", "", PPEType.Boots);
            var gloves = _tasks.Task("Luvas", "", PPEType.GloveLeft, PPEType.GloveRight);
            var group = _tasks.Group("EPIs", TaskExecutionModeShared.Sequential, boots, gloves);

            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started));
            _bus.Publish(new TaskEventArgs(boots, null, TaskPhase.Started));

            _bus.Publish(new PPEStateChangedEventArgs(PPEType.GloveLeft, true));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.GloveRight, true));

            Assert.IsEmpty(_taskCompletions, "gloves must not complete while boots is the active sequential task");
        }

        [Test]
        public void SequentialGroup_MatchingAction_PublishesTaskCompleted()
        {
            var task = _tasks.Task("ppe_helmet", "equip_helmet", PPEType.Helmet);
            var group = _tasks.Group("Sequential group", TaskExecutionModeShared.Sequential, task);

            _bus.Publish(new TaskGroupEventArgs(group));
            _bus.Publish(new TaskEventArgs(task));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Helmet, isWearing: true));
            _bus.Publish(new ActionAttemptedEvent("equip_helmet"));

            Assert.That(_taskCompletions.Count, Is.GreaterThanOrEqualTo(1));
            var completion = _taskCompletions[_taskCompletions.Count - 1];
            Assert.AreEqual(task.taskName, completion.Task.taskName);
            Assert.IsTrue(completion.WasPpeCompliant);
            Assert.IsEmpty(_violations);
        }

        [Test]
        public void SequentialGroup_WrongAction_PublishesWrongActionViolation()
        {
            var task = _tasks.Task("ppe_helmet", "equip_helmet", PPEType.Helmet);
            var group = _tasks.Group("g", TaskExecutionModeShared.Sequential, task);

            _bus.Publish(new TaskGroupEventArgs(group));
            _bus.Publish(new TaskEventArgs(task));
            _bus.Publish(new ActionAttemptedEvent("equip_boots"));

            Assert.AreEqual(1, _violations.Count);
            Assert.AreEqual("WRONG_ACTION", _violations[0].ViolationCode);
            Assert.IsEmpty(_taskCompletions);
        }

        [Test]
        public void ActionWithoutActiveGroup_PublishesNoActiveGroupViolation()
        {
            _bus.Publish(new ActionAttemptedEvent("equip_helmet"));

            Assert.AreEqual(1, _violations.Count);
            Assert.AreEqual("NO_ACTIVE_GROUP", _violations[0].ViolationCode);
        }

        [Test]
        public void ActionWithMissingPPE_PublishesPpeMissingViolation_AndUnsafeCompletion()
        {
            var task = _tasks.Task("ppe_helmet", "equip_helmet", PPEType.Helmet);
            var group = _tasks.Group("g", TaskExecutionModeShared.Sequential, task);

            _bus.Publish(new TaskGroupEventArgs(group));
            _bus.Publish(new TaskEventArgs(task));
            _bus.Publish(new ActionAttemptedEvent("equip_helmet"));

            Assert.AreEqual(1, _violations.Count);
            Assert.AreEqual("PPE_MISSING", _violations[0].ViolationCode);
            Assert.AreEqual(1, _taskCompletions.Count);
            Assert.IsFalse(_taskCompletions[0].WasPpeCompliant);
        }

        [Test]
        public void FreeOrderGroup_TasksCanCompleteInAnyOrder()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var t3 = _tasks.Task("t3", "action_c");
            var group = _tasks.Group("free", TaskExecutionModeShared.FreeOrder, t1, t2, t3);

            _bus.Publish(new TaskGroupEventArgs(group));

            _bus.Publish(new ActionAttemptedEvent("action_c"));
            _bus.Publish(new ActionAttemptedEvent("action_a"));
            _bus.Publish(new ActionAttemptedEvent("action_b"));

            Assert.AreEqual(3, _taskCompletions.Count);
            Assert.IsEmpty(_violations);
        }

        [Test]
        public void FreeOrderGroup_RepeatedActionAfterCompletion_IsIgnored()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var group = _tasks.Group("free", TaskExecutionModeShared.FreeOrder, t1);

            _bus.Publish(new TaskGroupEventArgs(group));

            _bus.Publish(new ActionAttemptedEvent("action_a"));
            _bus.Publish(new ActionAttemptedEvent("action_a"));

            Assert.AreEqual(1, _taskCompletions.Count);
            Assert.IsEmpty(_violations);
        }

        // ── Race condition tests ──────────────────────────────────────────────────

        [Test]
        public void RaceCondition_PpeZoneExitAfterActionInSameBatch_NoFalseViolation()
        {
            // Models the physics-frame race: player picks up new PPE while the
            // previously-held item exits its zone. The action and the zone-exit
            // arrive in the event queue in the same batch before EventBusRunner
            // processes them. Compliance must be evaluated against the state at
            // action time (boots still on in _ppeStates) — not against the
            // physics-ahead PPEManager state (boots already off).
            var task = _tasks.Task("equip_gloves", "equip_gloves", PPEType.Boots);
            var group = _tasks.Group("PPE selection", TaskExecutionModeShared.Sequential, task);

            _bus.Publish(new TaskGroupEventArgs(group));
            _bus.Publish(new TaskEventArgs(task));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Boots, isWearing: true));

            _bus.BatchPublish(() =>
            {
                _bus.Publish(new ActionAttemptedEvent("equip_gloves"));
                _bus.Publish(new PPEStateChangedEventArgs(PPEType.Boots, isWearing: false));
            });

            Assert.IsEmpty(_violations);
            Assert.AreEqual(1, _taskCompletions.Count);
            Assert.IsTrue(_taskCompletions[0].WasPpeCompliant);
        }

        [Test]
        public void RaceCondition_PpeZoneExitBeforeActionInSameBatch_ViolationFires()
        {
            // Sanity-check: if the zone-exit arrives *before* the action in the same
            // batch, the engine correctly sees missing PPE at action time and raises
            // a violation — the fix must not suppress legitimate failures.
            var task = _tasks.Task("equip_gloves", "equip_gloves", PPEType.Boots);
            var group = _tasks.Group("PPE selection", TaskExecutionModeShared.Sequential, task);

            _bus.Publish(new TaskGroupEventArgs(group));
            _bus.Publish(new TaskEventArgs(task));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Boots, isWearing: true));

            _bus.BatchPublish(() =>
            {
                _bus.Publish(new PPEStateChangedEventArgs(PPEType.Boots, isWearing: false));
                _bus.Publish(new ActionAttemptedEvent("equip_gloves"));
            });

            Assert.AreEqual(1, _violations.Count);
            Assert.AreEqual("PPE_MISSING", _violations[0].ViolationCode);
            Assert.AreEqual(1, _taskCompletions.Count);
            Assert.IsFalse(_taskCompletions[0].WasPpeCompliant);
        }

        // ── Group prerequisite (NR-35: anchor before working) ────────────────────

        private const string AnchorAdvice =
            "Conecte o talabarte ao ponto de ancoragem antes de trabalhar.";

        /// <summary>Free-order platform group whose first task is the anchoring precondition.
        /// No requiredPPE anywhere, so PPE compliance never clouds the assertions.</summary>
        private (FakeTaskBuilder.FakeTaskGroup group,
                 FakeTaskBuilder.FakeSafetyTask lanyard,
                 FakeTaskBuilder.FakeSafetyTask guardrail,
                 FakeTaskBuilder.FakeSafetyTask toeboard) BuildPlatformGroup(
            string prerequisiteId = "Talabarte")
        {
            var lanyard = _tasks.Task("Talabarte", "connect_harness");
            var guardrail = _tasks.Task("Guarda-corpo", "install_guardrail");
            var toeboard = _tasks.Task("Rodapé", "install_toeboard");

            var group = _tasks.Group("Plataforma", TaskExecutionModeShared.FreeOrder,
                lanyard, guardrail, toeboard);
            group.prerequisiteTaskId = prerequisiteId;
            group.prerequisiteAdvice = AnchorAdvice;

            return (group, lanyard, guardrail, toeboard);
        }

        [Test]
        public void GuidedMode_PrerequisitePending_RefusesSiblingAndRaisesViolation()
        {
            var (group, _, _, _) = BuildPlatformGroup();
            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started));

            _bus.Publish(new ActionAttemptedEvent("install_guardrail"));

            CollectionAssert.IsEmpty(_taskCompletions, "Sibling must not complete before the precondition.");
            Assert.AreEqual(1, _violations.Count);
            Assert.AreEqual("PREREQUISITE_PENDING", _violations[0].ViolationCode);
            Assert.AreEqual(AnchorAdvice, _violations[0].Message, "Authored advice drives the popup body.");
            Assert.AreEqual("Guarda-corpo", _violations[0].TaskName, "The violation names the REFUSED task.");
        }

        [Test]
        public void GuidedMode_RefusedSibling_StaysPendingAndCompletesAfterPrerequisite()
        {
            var (group, _, _, _) = BuildPlatformGroup();
            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started));

            // Refused, then the participant anchors and retries the same task.
            _bus.Publish(new ActionAttemptedEvent("install_guardrail"));
            _bus.Publish(new ActionAttemptedEvent("connect_harness"));
            _bus.Publish(new ActionAttemptedEvent("install_guardrail"));

            var completions = _taskCompletions.Select(e => e.Task.taskName).ToList();
            CollectionAssert.AreEqual(new[] { "Talabarte", "Guarda-corpo" }, completions);
        }

        [Test]
        public void GuidedMode_PrerequisiteMet_SiblingsStillCompleteInAnyOrder()
        {
            // The precondition must not turn the group into a sequence: once anchored, the
            // remaining tasks are free-order again, here done back-to-front.
            var (group, _, _, _) = BuildPlatformGroup();
            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started));

            _bus.Publish(new ActionAttemptedEvent("connect_harness"));
            _bus.Publish(new ActionAttemptedEvent("install_toeboard"));
            _bus.Publish(new ActionAttemptedEvent("install_guardrail"));

            var completions = _taskCompletions.Select(e => e.Task.taskName).ToList();
            CollectionAssert.AreEqual(new[] { "Talabarte", "Rodapé", "Guarda-corpo" }, completions);
            CollectionAssert.IsEmpty(_violations);
        }

        [Test]
        public void EvaluationMode_PrerequisitePending_DoesNotBlockSibling()
        {
            // Evaluation has to let the participant work unanchored — the inspection gate's
            // consequences are what measure the omission.
            SessionModeState.Current = SessionMode.Evaluation;

            var (group, _, _, _) = BuildPlatformGroup();
            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started));

            _bus.Publish(new ActionAttemptedEvent("install_guardrail"));

            Assert.AreEqual(1, _taskCompletions.Count);
            Assert.AreEqual("Guarda-corpo", _taskCompletions[0].Task.taskName);
            CollectionAssert.IsEmpty(_violations);
        }

        [Test]
        public void GuidedMode_UnknownPrerequisiteId_DoesNotDeadlockGroup()
        {
            var (group, _, _, _) = BuildPlatformGroup(prerequisiteId: "nao_existe");
            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started));

            _bus.Publish(new ActionAttemptedEvent("install_guardrail"));

            Assert.AreEqual(1, _taskCompletions.Count);
            Assert.AreEqual("Guarda-corpo", _taskCompletions[0].Task.taskName);
        }

        [Test]
        public void GuidedMode_PrerequisitePending_PublishesActionRefusedForTheEmitter()
        {
            // The refused attempt already changed the world (the piece snapped into its socket),
            // so the emitter is told it was declined and can put itself back.
            var refusals = new List<ActionRefusedEventArgs>();
            _bus.Subscribe<ActionRefusedEventArgs>(args => refusals.Add(args));

            var (group, _, _, _) = BuildPlatformGroup();
            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started));

            _bus.Publish(new ActionAttemptedEvent("install_guardrail", sourceId: "GuardRailPiece"));

            Assert.AreEqual(1, refusals.Count);
            Assert.AreEqual("install_guardrail", refusals[0].ActionId);
            Assert.AreEqual("GuardRailPiece", refusals[0].SourceId);
            Assert.AreEqual("PREREQUISITE_PENDING", refusals[0].ReasonCode);
        }

        [Test]
        public void GuidedMode_AcceptedAction_PublishesNoActionRefused()
        {
            var refusals = new List<ActionRefusedEventArgs>();
            _bus.Subscribe<ActionRefusedEventArgs>(args => refusals.Add(args));

            var (group, _, _, _) = BuildPlatformGroup();
            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started));

            _bus.Publish(new ActionAttemptedEvent("connect_harness", sourceId: "Lanyard"));
            _bus.Publish(new ActionAttemptedEvent("install_guardrail", sourceId: "GuardRailPiece"));

            Assert.AreEqual(2, _taskCompletions.Count);
            CollectionAssert.IsEmpty(refusals);
        }

        [Test]
        public void GuidedMode_GroupWithoutPrerequisite_IsUnaffected()
        {
            var (group, _, _, _) = BuildPlatformGroup(prerequisiteId: "");
            group.prerequisiteAdvice = string.Empty;
            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started));

            _bus.Publish(new ActionAttemptedEvent("install_guardrail"));

            Assert.AreEqual(1, _taskCompletions.Count);
            CollectionAssert.IsEmpty(_violations);
        }
    }
}
