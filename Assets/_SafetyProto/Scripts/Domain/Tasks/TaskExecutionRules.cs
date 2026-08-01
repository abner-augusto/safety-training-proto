#nullable enable
using System;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.Domain.Tasks
{
    /// <summary>Canonical task execution rules shared by the task and safety engines.</summary>
    public static class TaskExecutionRules
    {
        public static TaskExecutionModeShared EffectiveMode(ITaskGroup group) =>
            SessionModeState.Current == SessionMode.Evaluation
                ? TaskExecutionModeShared.FreeOrder
                : group.executionMode;

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
