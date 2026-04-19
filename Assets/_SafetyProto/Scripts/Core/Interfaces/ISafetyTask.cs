using System.Collections.Generic;

namespace SafetyProto.Core.Interfaces
{
    /// <summary>
    /// Engine-independent view of a safety training task.
    /// Implemented by the Unity <c>SafetyTask</c> ScriptableObject and by pure-C#
    /// records used in the CLI harness.
    /// </summary>
    public interface ISafetyTask
    {
        string taskName { get; }
        string taskDescription { get; }
        int successPoints { get; }
        int failurePenalty { get; }
        int ppePenalty { get; }
        IReadOnlyList<PPEType> requiredPPE { get; }
        string hintText { get; }
        string failureAdvice { get; }
        string ppeAdvice { get; }

        /// <summary>
        /// Returns the canonical action id this task expects. Mirrors
        /// <c>SafetyTask.ResolveExpectedActionId()</c>.
        /// </summary>
        string ResolveExpectedActionId();
    }
}
