using SafetyProto.Core;
using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.Utils
{
    /// <summary>
    /// Contains extension methods for Unity's MonoBehaviour class to reduce boilerplate code.
    /// </summary>
    public static class MonoBehaviourExtensions
    {
        /// <summary>
        /// Checks if the EventBus singleton instance is available. If not, it logs a formatted error
        /// and disables the calling MonoBehaviour component.
        /// </summary>
        /// <param name="monoBehaviour">The component instance calling the method.</param>
        /// <returns>True if the EventBus is available, otherwise false.</returns>
        public static bool IsEventBusReady(this MonoBehaviour monoBehaviour)
        {
            if (EventBus.Instance != null)
            {
                return true;
            }

            SafetyLog.Error(
                $"[{monoBehaviour.GetType().Name}] EventBus instance not found. " +
                "Please ensure your EventBus.asset is in a 'Resources' folder. Disabling this component.",
                monoBehaviour);

            monoBehaviour.enabled = false;
            return false;
        }
    }
}
