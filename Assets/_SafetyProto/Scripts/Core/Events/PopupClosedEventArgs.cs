#nullable enable
using System;

namespace SafetyProto.Core.Events
{
    /// <summary>
    /// The shared popup panel just went away — dismissed by the participant, by its action
    /// button, or by its auto-close timer. Published by the UI layer so gameplay objects can
    /// wait for the participant to have read a warning before changing the world under them,
    /// without taking a dependency on the UI assembly.
    /// </summary>
    [Serializable]
    public struct PopupClosedEventArgs
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;
    }
}
