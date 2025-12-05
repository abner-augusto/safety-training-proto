using UnityEngine;

namespace SafetyProto.Core.Logging
{
    /// <summary>
    /// Simple helper to prepend a consistent prefix to logs from SafetyProto scripts.
    /// </summary>
    public static class SafetyLog
    {
        private const string Prefix = "[SafetyProto] ";

        public static void Info(string message, Object context = null)
        {
            Debug.Log(Prefix + message, context);
        }

        public static void Warning(string message, Object context = null)
        {
            Debug.LogWarning(Prefix + message, context);
        }

        public static void Error(string message, Object context = null)
        {
            Debug.LogError(Prefix + message, context);
        }
    }
}
