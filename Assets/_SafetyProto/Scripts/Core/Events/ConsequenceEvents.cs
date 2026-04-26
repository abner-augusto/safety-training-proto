using System;
using UnityEngine;

namespace SafetyProto.Core.Events
{
    /// <summary>
    /// Minimal consequence type enum in Core to avoid Runtime dependency.
    /// Mirrors the values in Runtime.Safety.ConsequenceType.
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

    // Intentionally synchronous — not routed through EventBus. The caller
    // (InspectionGateValidator) is a coroutine that starts an animation immediately
    // after raising ConsequenceStarted and waits for it to finish before raising
    // ConsequenceEnded. Deferring through the queue would fire subscribers a frame
    // late, breaking the animation timing. Subscribers here are audio/visual only
    // and have no ordering dependency with queued EventBus events.
    public static class ConsequenceEvents
    {
        public static event Action<ConsequenceStartedEventArgs> OnConsequenceStarted;
        public static event Action OnConsequenceEnded;

        public static void RaiseConsequenceStarted(ConsequenceStartedEventArgs args)
            => OnConsequenceStarted?.Invoke(args);

        public static void RaiseConsequenceEnded()
            => OnConsequenceEnded?.Invoke();
    }
}
