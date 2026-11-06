using System;

namespace SafetyProto.Networking.Dashboard
{
    /// <summary>
    /// Implemented by MonoBehaviours that handle one evaluator dashboard inbound command
    /// (see EvaluatorDashboardBootstrap's "Command" envelope routing).
    /// </summary>
    public interface IDashboardCommandHandler
    {
        /// <summary>The command string this handler responds to (e.g. "recenter_player").</summary>
        string Command { get; }

        /// <summary>
        /// Attempts to execute the command now. Returns false plus a Portuguese operator-facing
        /// reason when the command cannot run in the current state. On success the same string
        /// may carry a short Portuguese result summary, shown to the evaluator and logged.
        /// </summary>
        bool TryExecute(out string reason);
    }

    /// <summary>A command that reads arguments from the envelope's <c>args</c> object.</summary>
    public interface IDashboardCommandWithArgs : IDashboardCommandHandler
    {
        bool TryExecute(DashboardCommandArgs args, out string reason);
    }

    /// <summary>
    /// The optional <c>args</c> object of a "Command" envelope. One flat type for every command,
    /// because JsonUtility needs the shape up front; fields a command does not use stay empty.
    /// </summary>
    [Serializable]
    public class DashboardCommandArgs
    {
        /// <summary>Normalized participant id ("P-07", "DEMO-01") for start_session.</summary>
        public string participantCode = string.Empty;
        /// <summary>Session mode token, "guiado" or "avaliacao", for start_session.</summary>
        public string mode = string.Empty;
        /// <summary>start_session: the evaluator armed the button while the headset showed a
        /// session, so ending it was confirmed. False means "start on the waiting screen".</summary>
        public bool replace;
    }
}
