using SafetyProto.Core;

namespace SafetyProto.Core.Events
{
    public static class SafetyEvents
    {
        public static void RaiseSafetyViolation(SafetyViolationEventArgs args)
        {
            EventBus.Instance.Publish(args);
        }

        public static void RaiseCriticalSafetyFailure(CriticalSafetyFailureEventArgs args)
        {
            EventBus.Instance.Publish(args);
        }

        public static void RaiseSafetyError(SafetyErrorEventArgs args)
        {
            EventBus.Instance.Publish(args);
        }
    }
}
