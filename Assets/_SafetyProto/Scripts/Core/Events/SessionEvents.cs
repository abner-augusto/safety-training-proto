using SafetyProto.Core;

namespace SafetyProto.Core.Events
{
    public static class SessionEvents
    {
        public static void RaiseSessionStarted(SessionStartedEventArgs args = new SessionStartedEventArgs())
        {
            EventBus.Instance.Publish(args);
        }

        public static void RaiseSessionPaused(SessionPausedEventArgs args = new SessionPausedEventArgs())
        {
            EventBus.Instance.Publish(args);
        }

        public static void RaiseSessionResumed(SessionResumedEventArgs args = new SessionResumedEventArgs())
        {
            EventBus.Instance.Publish(args);
        }

        public static void RaiseSessionEnded(SessionEndedEventArgs args = new SessionEndedEventArgs())
        {
            EventBus.Instance.Publish(args);
        }

        public static void RaiseSessionCompleted(SessionCompletedEventArgs args)
        {
            EventBus.Instance.Publish(args);
        }
    }
}
