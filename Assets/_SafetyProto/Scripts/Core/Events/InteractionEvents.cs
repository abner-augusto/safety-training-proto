namespace SafetyProto.Core.Events
{
    /// <summary>Unity-side facade for the grab and release of interactive objects and the guided hints.</summary>
    public static class InteractionEvents
    {
        public static void RaiseHintShown(int level, string taskId, string taskName, float secondsWithoutProgress)
        {
            EventBus.Instance?.Publish(new HintShownEventArgs
            {
                Level = level,
                TaskId = taskId ?? string.Empty,
                TaskName = taskName ?? string.Empty,
                SecondsWithoutProgress = secondsWithoutProgress
            });
        }

        public static void RaisePracticeStarted()
        {
            EventBus.Instance?.Publish(new PracticeEventArgs { Phase = PracticePhase.Started, Reason = string.Empty });
        }

        public static void RaisePracticeEnded(string reason, float seconds, string[] tried)
        {
            EventBus.Instance?.Publish(new PracticeEventArgs
            {
                Phase = PracticePhase.Ended,
                Reason = reason ?? string.Empty,
                Seconds = seconds,
                Tried = tried
            });
        }

        public static void RaiseObjectGrabbed(string target)
        {
            EventBus.Instance?.Publish(new ObjectGrabbedEventArgs { Target = target ?? string.Empty });
        }

        public static void RaiseObjectReleased(string target, float heldSeconds, bool secured)
        {
            EventBus.Instance?.Publish(new ObjectReleasedEventArgs
            {
                Target = target ?? string.Empty,
                HeldSeconds = heldSeconds,
                Secured = secured
            });
        }
    }
}
