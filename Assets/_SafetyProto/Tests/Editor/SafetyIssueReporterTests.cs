using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Runtime.Safety;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Covers the report decision: confirming publishes the authored action exactly once, and
    /// cancelling publishes nothing but is still counted as hesitation.
    /// </summary>
    public class SafetyIssueReporterTests
    {
        private GameObject _host;
        private SafetyIssueReporter _reporter;
        private string _publishedActionId;
        private int _publishCount;

        /// <summary>Answers every confirmation the same way, without any UI.</summary>
        private class ScriptedPopup : IPopupFeedback
        {
            private readonly bool _confirm;
            public ScriptedPopup(bool confirm) => _confirm = confirm;

            public void ShowConfirmation(string title, string body, string confirmLabel,
                                         string cancelLabel, UnityAction onConfirm, UnityAction onCancel = null)
            {
                if (_confirm) onConfirm?.Invoke();
                else onCancel?.Invoke();
            }

            public void ShowSuccess(string title, string body) { }
            public void ShowWarning(string title, string body) { }
            public void ShowTransient(string title, string body, float autoCloseSeconds) { }
            public void ShowInteractive(string title, string body, string buttonLabel, UnityAction onAction) { }
            public void Hide() { }
        }

        [SetUp]
        public void SetUp()
        {
            _publishedActionId = null;
            _publishCount = 0;

            EventContext.StartSession("test-session", "player", "scene");
            EventBus.Instance.onActionAttempt.AddListener(Capture);

            _host = new GameObject("reporter");
            _reporter = _host.AddComponent<SafetyIssueReporter>();
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Instance.onActionAttempt.RemoveListener(Capture);
            EventContext.Clear();
            if (_host != null) Object.DestroyImmediate(_host);
        }

        private void Capture(ActionAttemptedEvent evt)
        {
            _publishedActionId = evt.ActionId;
            _publishCount++;
        }

        [Test]
        public void ConfirmingPublishesTheAuthoredAction()
        {
            _reporter.PopupFeedback = new ScriptedPopup(confirm: true);

            _reporter.Report();
            EventBus.Instance.ProcessEvents(10);

            Assert.AreEqual("flag_safety_net", _publishedActionId);
            Assert.IsTrue(_reporter.HasReported);
        }

        [Test]
        public void CancellingPublishesNothingButIsCounted()
        {
            _reporter.PopupFeedback = new ScriptedPopup(confirm: false);

            _reporter.Report();
            EventBus.Instance.ProcessEvents(10);

            Assert.AreEqual(0, _publishCount);
            Assert.IsFalse(_reporter.HasReported);
            Assert.AreEqual(1, _reporter.CancelledReportCount,
                "A cancelled report is evidence of hesitation and must be observable.");
        }

        [Test]
        public void ReportingTwicePublishesOnce()
        {
            _reporter.PopupFeedback = new ScriptedPopup(confirm: true);

            _reporter.Report();
            _reporter.Report();
            EventBus.Instance.ProcessEvents(10);

            Assert.AreEqual(1, _publishCount);
        }

        [Test]
        public void ResetSessionClearsReportedState()
        {
            _reporter.PopupFeedback = new ScriptedPopup(confirm: true);
            _reporter.Report();
            EventBus.Instance.ProcessEvents(10);

            _reporter.ResetSession();

            Assert.IsFalse(_reporter.HasReported);
            Assert.AreEqual(0, _reporter.CancelledReportCount);
        }
    }
}
