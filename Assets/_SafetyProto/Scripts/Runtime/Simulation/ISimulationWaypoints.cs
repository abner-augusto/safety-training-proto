#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace SafetyProto.Runtime.Simulation
{
    /// <summary>
    /// Named places the session simulator can move the player to. Implemented outside this
    /// assembly (the dashboard scene layout already names the floors and task spots), so the
    /// simulator finds it through this interface instead of a reference.
    /// </summary>
    public interface ISimulationWaypoints
    {
        /// <summary>Every id <see cref="TryGetStandPoint"/> accepts, in authoring order.</summary>
        IReadOnlyList<string> WaypointIds { get; }

        /// <summary>World floor point to stand on for <paramref name="id"/>, and the point to face
        /// (equal to the stand point when there is nothing to face).</summary>
        bool TryGetStandPoint(string id, out Vector3 standPoint, out Vector3 lookAt);
    }
}
