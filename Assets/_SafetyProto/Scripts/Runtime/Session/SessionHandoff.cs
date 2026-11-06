using SafetyProto.Domain.Sessions;
using UnityEngine;

namespace SafetyProto.Runtime.Session
{
    /// <summary>
    /// Carries the next session's identity across the scene reload of a reset, which destroys
    /// every scene object. The evaluator's explicit request (start_session over a live session)
    /// is filed first; the reset then hands the outgoing session over and
    /// <see cref="SessionLineage"/> decides what the reloaded scene starts with.
    /// </summary>
    public static class SessionHandoff
    {
        private static SessionStartRequest _requested;
        private static SessionStartRequest _pending;

        /// <summary>The evaluator set who comes next; applied by the reset that follows.</summary>
        public static void Request(SessionStartRequest request) => _requested = request;

        /// <summary>Called by the reset with the session it tears down. Returns whether the next
        /// scene will start a session on its own.</summary>
        public static bool HandOver(SessionSnapshot outgoing)
        {
            _pending = SessionLineage.Next(outgoing, _requested, carried: _pending);
            _requested = null;
            return _pending != null;
        }

        /// <summary>Takes the start the previous scene left, if any. A request never handed over
        /// (no session manager saw the reset) still applies, so an evaluator start is not lost.</summary>
        public static bool TryConsume(out SessionStartRequest start)
        {
            start = _pending ?? _requested;
            _pending = null;
            _requested = null;
            return start != null;
        }

        // Domain reload is disabled on Play Mode entry; static state persists between runs.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            _requested = null;
            _pending = null;
        }
    }
}
