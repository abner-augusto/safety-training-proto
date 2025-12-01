using SafetyProto.Core;

namespace SafetyProto.Core.Events
{
    public static class ScoreEvents
    {
        public static void RaiseScoreChanged(ScoreChangedEventArgs args)
        {
            EventBus.Instance.RaiseScoreChanged(args);
        }
    }
}
