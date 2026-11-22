#nullable enable
using System;

namespace SafetyProto.Core.Events
{
    /// <summary>Whether an onboarding step was shown or the onboarding ended.</summary>
    public enum OnboardingPhase
    {
        StepShown,
        Finished
    }

    /// <summary>
    /// The onboarding as the participant went through it: each step shown, in which part, and how
    /// the whole ended. Nothing in it is scored; the analysis reads the reading time of each step
    /// from the gap to the next entry.
    /// </summary>
    [Serializable]
    public struct OnboardingEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        public OnboardingPhase Phase;

        /// <summary><c>practice</c> (the steps shown in the practice area) or <c>presentation</c>
        /// (the steps shown in the work site after it); empty on finish.</summary>
        public string Stage;

        /// <summary>1-based position of the step within its stage; 0 on finish.</summary>
        public int Step;

        /// <summary>The step's title as the participant read it; empty on finish.</summary>
        public string Title;

        /// <summary>How the onboarding ended (<c>completed</c>, <c>skipped</c>); empty for a step.</summary>
        public string Reason;

        /// <summary>Seconds from the start of the onboarding to its end; 0 for a step.</summary>
        public float Seconds;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }
    }
}
