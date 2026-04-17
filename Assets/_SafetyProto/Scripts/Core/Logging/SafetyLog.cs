using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SafetyProto.Core.Logging
{
    /// <summary>
    /// Simple helper to prepend a consistent prefix to logs from SafetyProto scripts.
    ///
    /// Routing:
    /// - If an <see cref="IHarnessLogger"/> has been registered via <see cref="SetLogger"/>,
    ///   messages are forwarded to it (used by the CLI harness to write to stdout).
    /// - Otherwise, messages go to UnityEngine.Debug as before.
    ///
    /// Info and Warning remain conditional on UNITY_EDITOR or DEVELOPMENT_BUILD for
    /// Unity calls; the harness-logger path is unconditional because the harness
    /// defines neither symbol.
    /// </summary>
    public static class SafetyLog
    {
        private const string Prefix = "[SafetyProto] ";

        private static IHarnessLogger _logger;

        public static void SetLogger(IHarnessLogger logger) => _logger = logger;

        public static void Info(string message, Object context = null)
        {
            if (_logger != null) { _logger.Info(Prefix + message); return; }
            UnityInfo(message, context);
        }

        public static void Warning(string message, Object context = null)
        {
            if (_logger != null) { _logger.Warning(Prefix + message); return; }
            UnityWarning(message, context);
        }

        public static void Error(string message, Object context = null)
        {
            if (_logger != null) { _logger.Error(Prefix + message); return; }
            UnityError(message, context);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private static void UnityInfo(string message, Object context)
            => Debug.Log(Prefix + message, context);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private static void UnityWarning(string message, Object context)
            => Debug.LogWarning(Prefix + message, context);

        private static void UnityError(string message, Object context)
            => Debug.LogError(Prefix + message, context);
    }
}
