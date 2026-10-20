using System.Collections.Generic;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Runtime.Safety;
using SafetyProto.Tests.Editor.Support;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Covers the report decision: a task without a report popup files the authored action at
    /// once; a task with one opens it with the scenario's copy and files after a choice, exactly
    /// once either way.
    ///
    /// The reporter's reaction to <c>ActionRefusedEventArgs</c> is deliberately absent here. It
    /// subscribes in <c>OnEnable</c>, which Unity never runs on a MonoBehaviour added outside
    /// Play Mode, so an EditMode fixture cannot deliver a refusal to it at all — a test that
    /// tried would either fail or, worse, assert the unchanged state and pass without exercising
    /// anything. The matching and revert policy it delegates to is covered engine-independently
    /// by <c>RefusedAttemptTrackerTests</c>; the wiring itself would need a PlayMode fixture,
    /// which this project does not have.
    /// </summary>
    public class SafetyIssueReporterTests
    {
        private GameObject _host;
        private SafetyIssueReporter _reporter;
        private string _publishedActionId;
        private string _publishedContext;
        private int _publishCount;

        /// <summary>Records the choice popup and keeps it open until the test answers it.</summary>
        private class HoldingPopup : IPopupFeedback
        {
            public string Title;
            public string Body;
            public IReadOnlyList<IReportOption> Options;
            public UnityAction<IReportOption> OnChosen;
            public int ChoiceCount;

            public void ShowChoice(string title, string body, IReadOnlyList<IReportOption> options,
                                   UnityAction<IReportOption> onChosen)
            {
                Title = title;
                Body = body;
                Options = options;
                OnChosen = onChosen;
                ChoiceCount++;
            }

            public void ShowConfirmation(string title, string body, string confirmLabel,
                                         string cancelLabel, UnityAction onConfirm, UnityAction onCancel = null) { }
            public void ShowSuccess(string title, string body) { }
            public void ShowWarning(string title, string body) { }
            public bool TryShowIfIdle(string title, string body, float autoCloseSeconds) => true;
            public void ShowInteractive(string title, string body, string buttonLabel, UnityAction onAction) { }
            public void Hide() { }
            public bool IsDisplayed => false;
        }

        [SetUp]
        public void SetUp()
        {
            _publishedActionId = null;
            _publishedContext = null;
            _publishCount = 0;

            EventContext.StartSession("test-session", "player", "scene");
            EventBus.Instance.Subscribe<ActionAttemptedEvent>(Capture);

            _host = new GameObject("reporter");
            _reporter = _host.AddComponent<SafetyIssueReporter>();
        }

        [TearDown]
        public void TearDown()
        {
            // A test that reports without draining leaves its report queued, and the next test's
            // ProcessEvents would count it.
            EventBus.Instance.ProcessEvents(10);
            EventBus.Instance.Unsubscribe<ActionAttemptedEvent>(Capture);
            EventContext.Clear();
            if (_host != null) Object.DestroyImmediate(_host);
        }

        private void Capture(ActionAttemptedEvent evt)
        {
            _publishedActionId = evt.ActionId;
            _publishedContext = evt.Context;
            _publishCount++;
        }

        private static FakeTaskBuilder.FakeSafetyTask MeshTask()
        {
            var task = new FakeTaskBuilder().Task("Comunicar Irregularidade na Tela Fachadeira", "flag_safety_net");
            task.reportPopupTitle = "Comunicar Irregularidade";
            task.reportPopupBody = "Como ela deve ser classificada?";
            task.ReportOptions.Add(new FakeTaskBuilder.FakeReportOption { Id = "torn_mesh", Label = "Tela rasgada", Correct = true });
            task.ReportOptions.Add(new FakeTaskBuilder.FakeReportOption { Id = "loose_fixing", Label = "Fixação inadequada" });
            task.ReportOptions.Add(new FakeTaskBuilder.FakeReportOption { Id = "no_irregularity", Label = "Sem irregularidade" });
            return task;
        }

        [Test]
        public void WithoutAReportPopup_TheActionIsFiledAtOnce()
        {
            var popup = new HoldingPopup();
            _reporter.PopupFeedback = popup;

            _reporter.Report();
            EventBus.Instance.ProcessEvents(10);

            Assert.AreEqual("flag_safety_net", _publishedActionId);
            Assert.IsTrue(_reporter.HasReported);
            Assert.AreEqual(0, popup.ChoiceCount, "A task without alternatives opens no popup.");
        }

        [Test]
        public void WithAReportPopup_TheScenarioCopyAndAlternativesAreShown()
        {
            var popup = new HoldingPopup();
            _reporter.PopupFeedback = popup;
            _reporter.ReportTask = MeshTask();

            _reporter.Report();
            EventBus.Instance.ProcessEvents(10);

            Assert.AreEqual(0, _publishCount, "Nothing is filed before a choice.");
            Assert.AreEqual("Comunicar Irregularidade", popup.Title);
            Assert.AreEqual("Como ela deve ser classificada?", popup.Body);
            Assert.AreEqual(3, popup.Options.Count);
        }

        [Test]
        public void ChoosingAnAlternative_FilesTheActionWithTheChoice()
        {
            var popup = new HoldingPopup();
            _reporter.PopupFeedback = popup;
            _reporter.ReportTask = MeshTask();
            _reporter.Report();

            popup.OnChosen(popup.Options[0]);
            EventBus.Instance.ProcessEvents(10);

            Assert.AreEqual("flag_safety_net", _publishedActionId);
            StringAssert.EndsWith(":option=torn_mesh", _publishedContext);
            Assert.IsTrue(_reporter.HasReported);
        }

        [Test]
        public void PressingAgainWhileThePopupIsOpen_OpensItOnce()
        {
            var popup = new HoldingPopup();
            _reporter.PopupFeedback = popup;
            _reporter.ReportTask = MeshTask();

            _reporter.Report();
            _reporter.Report();

            Assert.AreEqual(1, popup.ChoiceCount);
        }

        [Test]
        public void ReportingTwicePublishesOnce()
        {
            _reporter.PopupFeedback = new HoldingPopup();

            _reporter.Report();
            _reporter.Report();
            EventBus.Instance.ProcessEvents(10);

            Assert.AreEqual(1, _publishCount);
        }

        [Test]
        public void ResetSessionClearsReportedState()
        {
            _reporter.PopupFeedback = new HoldingPopup();
            _reporter.Report();
            EventBus.Instance.ProcessEvents(10);
            Assert.IsTrue(_reporter.HasReported, "Setup check: the report should have been filed.");

            _reporter.ResetSession();

            Assert.IsFalse(_reporter.HasReported);
        }

        private GameObject WireVisibleButtonRoot()
        {
            var root = new GameObject("button root");
            root.transform.SetParent(_host.transform);
            var so = new UnityEditor.SerializedObject(_reporter);
            so.FindProperty("_reportButtonRoot").objectReferenceValue = root;
            so.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(true);
            return root;
        }

        [Test]
        public void ReportButtonHidesWhileThePopupIsOpen()
        {
            var root = WireVisibleButtonRoot();
            _reporter.PopupFeedback = new HoldingPopup();
            _reporter.ReportTask = MeshTask();

            _reporter.Report();

            Assert.IsFalse(root.activeSelf, "The button must not stay over the popup it opened.");
        }

        [Test]
        public void ReportButtonStaysHiddenAfterChoosing()
        {
            var root = WireVisibleButtonRoot();
            var popup = new HoldingPopup();
            _reporter.PopupFeedback = popup;
            _reporter.ReportTask = MeshTask();
            _reporter.Report();

            popup.OnChosen(popup.Options[0]);

            Assert.IsFalse(root.activeSelf);
        }

        [Test]
        public void ReportButtonHidesAfterAFilingWithoutPopup()
        {
            var root = WireVisibleButtonRoot();
            _reporter.PopupFeedback = new HoldingPopup();

            _reporter.Report();

            Assert.IsFalse(root.activeSelf);
        }
    }
}
