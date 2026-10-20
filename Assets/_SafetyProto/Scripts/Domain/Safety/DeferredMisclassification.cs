#nullable enable
using System;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.Domain.Safety
{
    /// <summary>
    /// Holds a wrong report-popup choice until the rule engine settles the report it travelled
    /// with. A report is published optimistically and may still be refused (no active group, the
    /// group's precondition pending, ...); charging the misclassification at publish time would
    /// cost points for a report that never landed, and again on every retry. The charge is due
    /// only when the reported task completes.
    ///
    /// Engine-free so the rule is headlessly testable; the reporter feeds it bus events.
    /// </summary>
    public sealed class DeferredMisclassification
    {
        private ISafetyTask? _task;

        public bool IsPending => _task != null;

        /// <summary>Remembers a wrong choice for <paramref name="task"/>, replacing any earlier one.</summary>
        public void Hold(ISafetyTask task) => _task = task;

        /// <summary>
        /// Returns the task to charge when <paramref name="completed"/> is the held one, and
        /// clears the hold so the charge happens once. Null for any other task.
        /// </summary>
        public ISafetyTask? TakeOnCompleted(ISafetyTask? completed)
        {
            if (_task == null || completed == null) return null;
            if (!string.Equals(completed.id, _task.id, StringComparison.OrdinalIgnoreCase)) return null;

            var due = _task;
            _task = null;
            return due;
        }

        /// <summary>Forgets the held choice: the report it belonged to was refused.</summary>
        public void Drop() => _task = null;
    }
}
