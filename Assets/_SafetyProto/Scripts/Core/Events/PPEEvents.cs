using SafetyProto.Core;

namespace SafetyProto.Core.Events
{
    public static class PPEEvents
    {
        public static void RaisePpeStateChanged(PPEStateChangedEventArgs args)
        {
            EventBus.Instance.RaisePpeStateChanged(args);
        }
    }
}
