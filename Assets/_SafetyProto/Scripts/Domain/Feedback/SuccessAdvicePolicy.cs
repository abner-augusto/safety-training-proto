using SafetyProto.Core;

namespace SafetyProto.Domain.Feedback
{
    /// <summary>Decides whether correct-path advice may be revealed for a task completion.</summary>
    public static class SuccessAdvicePolicy
    {
        public static bool ShouldShow(
            TaskPhase phase,
            TaskState outcome,
            bool hasMisclassifiedReport,
            SessionMode sessionMode,
            string successAdvice)
        {
            return phase == TaskPhase.Completed &&
                   outcome == TaskState.CompletedSuccess &&
                   !hasMisclassifiedReport &&
                   sessionMode != SessionMode.Evaluation &&
                   !string.IsNullOrWhiteSpace(successAdvice);
        }
    }
}
