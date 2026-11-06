#nullable enable
using System;

namespace SafetyProto.Domain.Tasks
{
    /// <summary>What the phase-advance gate must do for a given press of the advance button.</summary>
    public enum PhaseAdvanceAction
    {
        /// <summary>The press does not belong to this gate — the target group is neither
        /// running nor closed. Nothing happens.</summary>
        Ignore = 0,

        /// <summary>The target group is still running: close it (pending tasks become
        /// NotPerformed) and then advance.</summary>
        CloseThenAdvance = 1,

        /// <summary>The target group already completed on its own, so there is nothing left to
        /// close — the press only confirms and advances.</summary>
        AdvanceOnly = 2,
    }

    /// <summary>
    /// Decision rule behind the Evaluation-mode advance button.
    ///
    /// Pure so the rule can be tested without a scene. A group that completes naturally stops
    /// being the current group immediately (<see cref="TaskManagerCore"/> publishes
    /// GroupCompleted and moves straight to the next group), so the rule must accept the closed
    /// target group as well as the current one, or it does nothing for a participant who
    /// performed every task.
    /// </summary>
    public static class PhaseAdvanceGate
    {
        public static PhaseAdvanceAction Decide(string? currentGroupId, string? targetGroupId, bool targetGroupCompleted)
        {
            if (string.IsNullOrWhiteSpace(targetGroupId)) return PhaseAdvanceAction.Ignore;

            if (string.Equals(currentGroupId, targetGroupId, StringComparison.Ordinal))
                return PhaseAdvanceAction.CloseThenAdvance;

            return targetGroupCompleted ? PhaseAdvanceAction.AdvanceOnly : PhaseAdvanceAction.Ignore;
        }
    }
}
