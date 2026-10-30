using SafetyProto.Core;

namespace SafetyProto.Runtime.Session
{
    /// <summary>
    /// The pre-session flow, as the evaluator dashboard sees it. Lives in Runtime so the
    /// Networking command handler can reach the UI implementation without referencing UI.
    /// </summary>
    public interface IEvaluatorSessionStarter
    {
        /// <summary>The headset shows the "waiting for the researcher" screen.</summary>
        bool IsAwaitingEvaluator { get; }

        /// <summary>No dashboard connected in time, so the participant may type the code in VR.</summary>
        bool IsParticipantKeypadOpen { get; }

        /// <summary>
        /// Starts the session now with an evaluator-set participant id and mode, closing any
        /// identification popup still open. False plus a Portuguese reason when the session has
        /// already started.
        /// </summary>
        bool TryStartFromEvaluator(string participantId, SessionMode mode, out string reason);
    }
}
