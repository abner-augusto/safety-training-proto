#nullable enable
using System;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.Domain.Tasks
{
    /// <summary>Canonical task execution rules shared by the task and safety engines.</summary>
    public static class TaskExecutionRules
    {
        /// <summary>Evaluation mode overrides every group to free-order semantics;
        /// Guided respects the authored mode. All mode branches in the task and safety
        /// engines must go through here — reading group.executionMode directly
        /// reintroduces sequential enforcement in Evaluation.</summary>
        public static TaskExecutionModeShared EffectiveMode(ITaskGroup group, SessionMode mode) =>
            mode == SessionMode.Evaluation ? TaskExecutionModeShared.FreeOrder : group.executionMode;

        /// <summary>Reads the process-wide default mode, for hosts that have not opted
        /// into injection; the cores use the mode-explicit overload.</summary>
        public static TaskExecutionModeShared EffectiveMode(ITaskGroup group) =>
            EffectiveMode(group, SessionModeState.Current);

        public static bool IsEquipTask(ISafetyTask? task) =>
            task != null &&
            string.IsNullOrEmpty(task.ResolveExpectedActionId()) &&
            task.requiredPPE != null && task.requiredPPE.Count > 0;

        public static bool MatchesAction(ISafetyTask? task, string actionId)
        {
            if (task == null || string.IsNullOrWhiteSpace(actionId)) return false;
            var expected = task.ResolveExpectedActionId();
            return !string.IsNullOrEmpty(expected) &&
                   string.Equals(expected, actionId.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
