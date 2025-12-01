using SafetyProto.Core;

namespace SafetyProto.Core.Events
{
    public static class TaskEvents
    {
        public static void RaiseTaskStarted(TaskEventArgs args)
        {
            EventBus.Instance.RaiseTaskStarted(args);
        }

        public static void RaiseTaskCompleted(TaskEventArgs args)
        {
            EventBus.Instance.RaiseTaskCompleted(args);
        }

        public static void RaiseTaskTimeout(TaskEventArgs args)
        {
            EventBus.Instance.RaiseTaskTimeout(args);
        }

        public static void RaiseGroupStarted(TaskGroupEventArgs args)
        {
            EventBus.Instance.RaiseGroupStarted(args);
        }

        public static void RaiseGroupCompleted(TaskGroupEventArgs args)
        {
            EventBus.Instance.RaiseGroupCompleted(args);
        }
    }
}
