#nullable enable
using System;

namespace SafetyProto.Core.Events
{
    /// <summary>
    /// The shared popup panel just became visible. Published by the UI layer so a system that
    /// must react to the panel itself — rather than to the session pause it also raises — can
    /// do so without taking a dependency on the UI assembly. The popup camera's hand overlay
    /// listens to this pair to render the hands above the panel while it is up.
    /// </summary>
    [Serializable]
    public struct PopupShownEventArgs
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;
    }
}
