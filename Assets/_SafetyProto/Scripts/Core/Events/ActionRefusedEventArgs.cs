#nullable enable
using System;

namespace SafetyProto.Core.Events
{
    /// <summary>
    /// An <see cref="ActionAttemptedEvent"/> the rule engine declined to turn into a task
    /// completion. Carries the identity of the attempt (not of the task) so the object that
    /// performed it can undo whatever it did optimistically — a scaffold piece that snapped
    /// itself into place before learning the participant is not anchored yet.
    ///
    /// The refusal is already explained to the participant through the accompanying
    /// <see cref="SafetyViolationEventArgs"/>; this event exists for the world state, not the UI.
    /// </summary>
    [Serializable]
    public struct ActionRefusedEventArgs
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        /// <summary>Canonical id of the refused action.</summary>
        public string ActionId;

        /// <summary>Emitter of the attempt (defaults to the GameObject name), so two objects
        /// sharing an action id can tell whose attempt was refused.</summary>
        public string? SourceId;

        /// <summary>Violation code that refused it, e.g. <c>PREREQUISITE_PENDING</c>.</summary>
        public string ReasonCode;

        public ActionRefusedEventArgs(string actionId, string? sourceId, string reasonCode)
        {
            SessionId = string.Empty;
            PlayerId = string.Empty;
            ScenarioId = string.Empty;
            TimestampMs = 0L;

            ActionId = actionId ?? string.Empty;
            SourceId = sourceId;
            ReasonCode = reasonCode ?? string.Empty;
        }
    }
}
