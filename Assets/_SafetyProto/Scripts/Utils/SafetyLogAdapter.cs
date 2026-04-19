using SafetyProto.Core.Logging;

namespace SafetyProto.Utils
{
    public sealed class SafetyLogAdapter : IHarnessLogger
    {
        public void Info(string message) => SafetyLog.Info(message);
        public void Warning(string message) => SafetyLog.Warning(message);
        public void Error(string message) => SafetyLog.Error(message);
    }
}
