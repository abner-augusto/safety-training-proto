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
