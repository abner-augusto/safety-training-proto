using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Gameplay.Events;

namespace SafetyProto.CliHarness;

public sealed class TranscriptRecorder : IDisposable
{
    private readonly IEventBus _bus;

    private readonly Action<SessionStartedEventArgs> _onSessionStarted;
    private readonly Action<SessionCompletedEventArgs> _onSessionCompleted;
    private readonly Action<PPEStateChangedEventArgs> _onPpeStateChanged;
    private readonly Action<ActionAttemptedEvent> _onActionAttempt;
    private readonly Action<TaskGroupEventArgs> _onGroupLifecycle;
    private readonly Action<TaskEventArgs> _onTaskLifecycle;
    private readonly Action<ScoreChangedEventArgs> _onScoreChanged;
    private readonly Action<SafetyViolationEventArgs> _onSafetyViolation;

    private bool _subscribed;

    public TranscriptRecorder(IEventBus bus)
    {
        _bus = bus;
        _onSessionStarted     = a => Write("SessionStarted",   "");
        _onSessionCompleted   = a => Write("SessionCompleted",
            string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Time={0:F2}s, Score={1}, Tasks={2}/{3}",
                a.totalElapsedTime, a.totalScore, a.tasksCompleted, a.totalTasks));
        _onPpeStateChanged    = a => Write("PpeStateChanged",  $"{a.PpeType}={(a.IsWearing ? "WORN" : "REMOVED")}");
        _onActionAttempt      = a => Write("ActionAttempt",    a.ActionId);
        _onGroupLifecycle     = a => Write($"Group{a.Phase}",  a.Group?.groupName ?? "<null>");
        _onTaskLifecycle      = a => Write($"Task{a.Phase}",   a.Task?.taskName ?? "<null>");
        _onScoreChanged       = a => Write("ScoreChanged",     $"Delta={a.Delta}, Total={a.TotalScore}");
        _onSafetyViolation    = a => Write("SafetyViolation",  $"{a.ViolationCode} | {a.Message}");
    }

    public void Subscribe()
    {
        if (_subscribed) return;
        _bus.Subscribe(_onSessionStarted);
        _bus.Subscribe(_onSessionCompleted);
        _bus.Subscribe(_onPpeStateChanged);
        _bus.Subscribe(_onActionAttempt);
        _bus.Subscribe(_onGroupLifecycle);
        _bus.Subscribe(_onTaskLifecycle);
        _bus.Subscribe(_onScoreChanged);
        _bus.Subscribe(_onSafetyViolation);
        _subscribed = true;
    }

    public void Unsubscribe()
    {
        if (!_subscribed) return;
        _bus.Unsubscribe(_onSessionStarted);
        _bus.Unsubscribe(_onSessionCompleted);
        _bus.Unsubscribe(_onPpeStateChanged);
        _bus.Unsubscribe(_onActionAttempt);
        _bus.Unsubscribe(_onGroupLifecycle);
        _bus.Unsubscribe(_onTaskLifecycle);
        _bus.Unsubscribe(_onScoreChanged);
        _bus.Unsubscribe(_onSafetyViolation);
        _subscribed = false;
    }

    private static void Write(string eventName, string details)
    {
        var ts = DateTimeOffset.UtcNow.ToString("HH:mm:ss.fff");
        if (string.IsNullOrEmpty(details))
        {
            Console.WriteLine($"  {ts}  {eventName}");
        }
        else
        {
            Console.WriteLine($"  {ts}  {eventName,-18} | {details}");
        }
    }

    public void Dispose() => Unsubscribe();
}
