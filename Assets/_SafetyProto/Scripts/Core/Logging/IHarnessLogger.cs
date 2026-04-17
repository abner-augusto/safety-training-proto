namespace SafetyProto.Core.Logging
{
    /// <summary>
    /// Sink for diagnostic messages emitted by Core logic.
    /// Unity provides an adapter that forwards to <c>UnityEngine.Debug</c>;
    /// the CLI harness provides a stdout adapter.
    ///
    /// When no logger is set, <c>SafetyLog</c> retains its current Unity-only
    /// behaviour — this interface is purely additive in Part 1.
    /// </summary>
    public interface IHarnessLogger
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }
}
