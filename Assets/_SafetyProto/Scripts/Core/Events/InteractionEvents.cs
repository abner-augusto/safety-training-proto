namespace SafetyProto.Core.Events
{
    /// <summary>Unity-side facade for the grab and release of interactive objects.</summary>
    public static class InteractionEvents
    {
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
