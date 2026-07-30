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
        /// reason when the command cannot run in the current state.
        /// </summary>
        bool TryExecute(out string reason);
    }
}
