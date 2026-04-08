using System;
using SafetyProto.Gameplay.Safety;
using UnityEngine;

namespace SafetyProto.Core.Events
{
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
