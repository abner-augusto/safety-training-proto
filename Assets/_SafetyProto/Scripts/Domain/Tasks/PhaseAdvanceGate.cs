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
    /// Decision rule behind the Phase 1 advance button (modo Avaliação).
    ///
    /// Pure so the rule can be tested without a scene: the interesting case is that a group
    /// which completes naturally stops being the current group immediately —
    /// <see cref="TaskManagerCore"/> publishes GroupCompleted and moves straight to the next
    /// group. A gate that only accepted "target group is current" therefore did nothing for a
    /// participant who performed every task, and worked only for one who skipped a task.
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
