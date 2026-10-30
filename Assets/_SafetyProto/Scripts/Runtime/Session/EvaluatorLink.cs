using UnityEngine;

namespace SafetyProto.Runtime.Session
{
    /// <summary>
    /// Whether an evaluator dashboard is connected right now. Written by the dashboard bootstrap
    /// (Networking) every frame and read by the pre-session flow (UI), which cannot reference
    /// Networking, to decide when to stop waiting for the evaluator.
    /// </summary>
    public static class EvaluatorLink
    {
        public static bool IsConnected { get; private set; }

        public static void SetConnected(bool connected) => IsConnected = connected;

        // Domain reload is disabled on Play Mode entry; static state persists between runs.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => IsConnected = false;
    }
}
