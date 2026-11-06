#nullable enable

namespace SafetyProto.Domain.Dashboard
{
    /// <summary>What a <c>start_session</c> command does to the headset.</summary>
    public enum StartSessionAction
    {
        /// <summary>No session is running: start the requested one in this scene.</summary>
        StartNow,

        /// <summary>The evaluator confirmed ending the running session: reset into the requested one.</summary>
        Replace,

        /// <summary>A session is running but the evaluator never saw it, so the command was meant for
        /// the waiting screen: refuse instead of ending a session nobody chose to end.</summary>
        RefuseLive,
    }

    /// <summary>
    /// Decides a <c>start_session</c> from what the headset is doing and what the evaluator saw
    /// when arming the button. Commands are only echoed, never de-duplicated, so a second tab,
    /// a retry after an ack timeout, or a keypad start racing the evaluator all deliver a "start"
    /// to a session that has just begun; without the intent it would silently become a replace.
    /// </summary>
    public static class StartSessionPolicy
    {
        public static StartSessionAction Decide(bool sessionStarted, bool replaceConfirmed)
        {
            if (!sessionStarted) return StartSessionAction.StartNow;
            return replaceConfirmed ? StartSessionAction.Replace : StartSessionAction.RefuseLive;
        }
    }
}
