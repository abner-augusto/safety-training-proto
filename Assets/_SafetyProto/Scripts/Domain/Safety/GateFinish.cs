#nullable enable
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Domain.Tasks;

namespace SafetyProto.Domain.Safety
{
    /// <summary>Rules for the moment the Evaluation inspection gate is confirmed.</summary>
    public static class GateFinish
    {
        /// <summary>
        /// The tasks of <paramref name="capturedAtPress"/> still open now. The gate captures its
        /// pending list on the press and runs consequences only after the participant confirms;
        /// whatever they finished in between is no longer an omission.
        /// </summary>
        public static List<RuntimeSafetyTask> StillPending(IEnumerable<RuntimeSafetyTask> capturedAtPress)
        {
            var open = new List<RuntimeSafetyTask>();
            foreach (var task in capturedAtPress)
                if (task != null && !TaskManagerCore.IsTerminal(task.State)) open.Add(task);
            return open;
        }
    }
}
