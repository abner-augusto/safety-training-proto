using SafetyProto.Core;

namespace SafetyProto.Core.Events
{
    public static class ActionEvents
    {
        public static void RaiseActionAttempt(ActionAttemptEventArgs args)
        {
            EventBus.Instance.RaiseActionAttempt(args);
        }
    }
}
