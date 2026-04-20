#nullable enable
using System.Collections.Generic;

namespace SafetyProto.Core.Interfaces
{
    /// <summary>
    /// Engine-independent PPE compliance query.
    /// <para>
    /// Implementations: <c>PPEComplianceAdapter</c> (Unity, backed by the
    /// PPEManager) and <c>HarnessPPEComplianceChecker</c> (Part 8, backed by a
    /// dictionary driven by <c>PPEStateChangedEventArgs</c>).
    /// </para>
    /// </summary>
    public interface IPPEComplianceChecker
    {
        /// <summary>
        /// Returns true when every PPE type in <paramref name="requiredPpe"/> is
        /// currently worn and compliant (e.g., in range, active). An empty or null
        /// list means "no PPE required" and returns true.
        /// </summary>
        bool IsCompliant(IReadOnlyCollection<PPEType> requiredPpe);
    }
}
