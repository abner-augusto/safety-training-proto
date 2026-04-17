using SafetyProto.Core.Logging;

namespace SafetyProto.CliHarness;

public sealed class ConsoleHarnessLogger : IHarnessLogger
{
    public void Info(string message) => Console.WriteLine($"[INFO]  {message}");
    public void Warning(string message) => Console.WriteLine($"[WARN]  {message}");
    public void Error(string message) => Console.Error.WriteLine($"[ERROR] {message}");
}
