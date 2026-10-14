using System;
using UnityEngine;

namespace SafetyProto.Core.Events
{
    /// <summary>
    /// Kind of scripted consequence a failed inspection gate can play. Lives in Core so the
    /// synchronous channel below can carry it without the event core depending on Runtime;
    /// <c>InspectionGateValidator</c> maps scene objects to these values.
    /// </summary>
    public enum ConsequenceType
    {
        ObjectFall = 0,
        PlayerFallSimulation = 1,
        VisualAlert = 2,
    }

    public class ConsequenceStartedEventArgs
    {
        public ConsequenceType ConsequenceType;
        public GameObject TargetObject;
        public string MappingId;
    }

    /// <summary>
    /// Synchronous channel that deliberately bypasses the deferred <c>EventBus</c> queue, so
    /// subscribers run in the same frame as the raise.
    /// </summary>
    public static class DirectConsequenceChannel
    {
        public static event Action<ConsequenceStartedEventArgs> OnConsequenceStarted;
        public static event Action OnConsequenceEnded;

        public static void RaiseConsequenceStarted(ConsequenceStartedEventArgs args)
            => OnConsequenceStarted?.Invoke(args);

        public static void RaiseConsequenceEnded()
            => OnConsequenceEnded?.Invoke();
    }
}
