using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SafetyProto.Core.Logging
{
    /// <summary>
    /// Simple helper to prepend a consistent prefix to logs from SafetyProto scripts.
    /// </summary>
    public static class SafetyLog
    {
        private const string Prefix = "[SafetyProto] ";

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Info(string message, Object context = null)
        {
            Debug.Log(Prefix + message, context);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Warning(string message, Object context = null)
        {
            Debug.LogWarning(Prefix + message, context);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Error(string message, Object context = null)
        {
            Debug.LogError(Prefix + message, context);
        }
    }
}
