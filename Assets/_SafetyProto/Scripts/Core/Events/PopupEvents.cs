namespace SafetyProto.Core.Events
{
    /// <summary>
    /// Unity-side facade for popup lifecycle events. <see cref="PopupClosedEventArgs"/> has no
    /// dedicated <c>UnityEvent</c> field on <see cref="EventBus"/> — it goes through the generic
    /// <see cref="EventBus.Publish{T}"/> path, which stamps it via <c>EventMetadata.Stamp</c>.
    /// </summary>
    public static class PopupEvents
    {
        /// <summary>
        /// Announces that the shared popup panel just went away. <paramref name="reasonCode"/>
        /// echoes the violation code that opened it (empty for a popup opened for anything else).
        /// </summary>
        public static void RaisePopupClosed(string reasonCode)
        {
            EventBus.Instance.Publish(new PopupClosedEventArgs { ReasonCode = reasonCode ?? string.Empty });
        }

        /// <summary>
        /// Announces that the shared popup panel just became visible. Mirrors
        /// <see cref="RaisePopupClosed"/> so a subscriber that must react to the panel itself,
        /// not to the session pause it also raises, can bracket its lifetime without reaching
        /// into the UI assembly.
        /// </summary>
        public static void RaisePopupShown()
        {
            EventBus.Instance.Publish(new PopupShownEventArgs());
        }
    }
}
