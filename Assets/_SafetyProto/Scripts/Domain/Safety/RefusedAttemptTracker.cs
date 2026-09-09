#nullable enable
using System;

namespace SafetyProto.Domain.Safety
{
    /// <summary>
    /// Pure policy behind "the rule engine refused my attempt — may I undo what I did
    /// optimistically yet?" Answers two questions an emitter needs and nothing else: whether a
    /// given refusal belongs to it, and when the resulting revert is allowed to happen.
    ///
    /// Time-injected and engine-free so it is headlessly testable, modelled on
    /// <see cref="Tasks.PhaseAdvanceGate"/> and <see cref="Feedback.AudioFeedbackArbiter"/>. Each
    /// emitter composes its own instance — there is no shared coordinator, per the spine
    /// architecture: features attach to the bus, they do not wire to each other.
    /// </summary>
    public sealed class RefusedAttemptTracker
    {
        private bool _pending;
        private bool _waitForPopup;
        private string _reasonCode = string.Empty;
        private float _fallbackDeadline;
        private bool _hasFallbackDeadline;
        private bool _released;

        /// <summary>
        /// Records a refusal aimed at <paramref name="actionId"/>/<paramref name="sourceId"/>.
        /// Ignored when the attempt is not this emitter's — matched by
        /// <paramref name="myActionId"/> and, when <paramref name="sourceId"/> is non-empty, by
        /// <paramref name="mySourceId"/> too. An empty <paramref name="sourceId"/> means the
        /// emitter that attempted did not identify itself, so it is accepted rather than left
        /// stuck.
        ///
        /// A second matching refusal while one is already pending does not restart the clock —
        /// the participant is still looking at the first warning (or already past it).
        /// </summary>
        public void Observe(string actionId, string? sourceId, string reasonCode,
                             string myActionId, string mySourceId, bool waitForPopup,
                             float now, float fallbackSeconds)
        {
            if (_pending) return;

            if (string.IsNullOrEmpty(myActionId) ||
                !string.Equals(actionId, myActionId, StringComparison.Ordinal)) return;

            if (!string.IsNullOrEmpty(sourceId) &&
                !string.Equals(sourceId, mySourceId, StringComparison.Ordinal)) return;

            _pending = true;
            _released = false;
            _waitForPopup = waitForPopup;
            _reasonCode = reasonCode ?? string.Empty;

            _hasFallbackDeadline = waitForPopup && fallbackSeconds > 0f;
            _fallbackDeadline = _hasFallbackDeadline ? now + fallbackSeconds : 0f;
        }

        /// <summary>Notifies the tracker that the shared popup closed with the given reason code.
        /// Releases a pending, popup-gated refusal when the code matches.</summary>
        public void NotifyPopupClosed(string reasonCode)
        {
            if (!_pending || !_waitForPopup) return;
            if (!string.Equals(reasonCode, _reasonCode, StringComparison.Ordinal)) return;

            _released = true;
        }

        /// <summary>
        /// True, exactly once per refusal, when the revert may happen now — immediately for a
        /// refusal that does not wait for a popup, on a matching <see cref="NotifyPopupClosed"/>,
        /// or once <paramref name="now"/> passes the fallback deadline (a fallback of 0 means
        /// wait indefinitely for the popup). Consumes the pending state only when it returns
        /// true, so a caller polling every frame keeps waiting until the revert is due and then
        /// sees it exactly once.
        /// </summary>
        public bool TryTakeRevert(float now)
        {
            if (!_pending) return false;

            bool ready = !_waitForPopup || _released ||
                         (_hasFallbackDeadline && now >= _fallbackDeadline);
            if (!ready) return false;

            _pending = false;
            return true;
        }

        public void Reset()
        {
            _pending = false;
            _released = false;
            _waitForPopup = false;
            _reasonCode = string.Empty;
            _hasFallbackDeadline = false;
            _fallbackDeadline = 0f;
        }
    }
}
