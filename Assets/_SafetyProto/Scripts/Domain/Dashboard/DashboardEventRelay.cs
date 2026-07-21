// Assets/_SafetyProto/Scripts/Domain/Dashboard/DashboardEventRelay.cs
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.Domain.Dashboard
{
    /// <summary>
    /// Owns the dashboard's EventBus coupling: subscribes to every gameplay event via the
    /// engine-independent IEventBus, translates each to a wire DTO (trivial field copies here,
    /// task DTOs via DashboardDtoMapper), and pushes it through IDashboardHost.Broadcast.
    /// Pure C# — unit-tested with FakeEventBus + a fake host, no Unity Editor required.
    ///
    /// TaskEventArgs and TaskGroupEventArgs are single types carrying a Phase, so one
    /// subscription per type demuxes the phases (matching the former per-UnityEvent handlers).
    /// </summary>
    public sealed class DashboardEventRelay
    {
        private readonly IEventBus _bus;
        private readonly IDashboardHost _host;

        public DashboardEventRelay(IEventBus bus, IDashboardHost host)
        {
            _bus = bus;
            _host = host;
        }

        public void Subscribe()
        {
            _bus.Subscribe<SessionStartedEventArgs>(OnSessionStarted);
            _bus.Subscribe<SessionPausedEventArgs>(OnSessionPaused);
            _bus.Subscribe<SessionResumedEventArgs>(OnSessionResumed);
            _bus.Subscribe<SessionEndedEventArgs>(OnSessionEnded);
            _bus.Subscribe<SessionCompletedEventArgs>(OnSessionCompleted);
            _bus.Subscribe<TaskEventArgs>(OnTask);
            _bus.Subscribe<TaskGroupEventArgs>(OnGroup);
            _bus.Subscribe<ScoreChangedEventArgs>(OnScoreChanged);
            _bus.Subscribe<PPEStateChangedEventArgs>(OnPpeStateChanged);
            _bus.Subscribe<ActionAttemptedEvent>(OnActionAttempt);
            _bus.Subscribe<SafetyViolationEventArgs>(OnSafetyViolation);
            _bus.Subscribe<CriticalSafetyFailureEventArgs>(OnCriticalSafetyFailure);
            _bus.Subscribe<SafetyErrorEventArgs>(OnSafetyError);
        }

        public void Unsubscribe()
        {
            _bus.Unsubscribe<SessionStartedEventArgs>(OnSessionStarted);
            _bus.Unsubscribe<SessionPausedEventArgs>(OnSessionPaused);
            _bus.Unsubscribe<SessionResumedEventArgs>(OnSessionResumed);
            _bus.Unsubscribe<SessionEndedEventArgs>(OnSessionEnded);
            _bus.Unsubscribe<SessionCompletedEventArgs>(OnSessionCompleted);
            _bus.Unsubscribe<TaskEventArgs>(OnTask);
            _bus.Unsubscribe<TaskGroupEventArgs>(OnGroup);
            _bus.Unsubscribe<ScoreChangedEventArgs>(OnScoreChanged);
            _bus.Unsubscribe<PPEStateChangedEventArgs>(OnPpeStateChanged);
            _bus.Unsubscribe<ActionAttemptedEvent>(OnActionAttempt);
            _bus.Unsubscribe<SafetyViolationEventArgs>(OnSafetyViolation);
            _bus.Unsubscribe<CriticalSafetyFailureEventArgs>(OnCriticalSafetyFailure);
            _bus.Unsubscribe<SafetyErrorEventArgs>(OnSafetyError);
        }

        private void OnSessionStarted(SessionStartedEventArgs args)
        {
            var dto = new SessionDto(args.SessionId, _host.ResolveTimestamp(args.TimestampMs))
            {
                participantId = string.IsNullOrEmpty(args.PlayerId) ? "—" : args.PlayerId,
                mode = SessionModeState.CurrentName
            };
            _host.Broadcast("SessionStarted", dto);
            _host.Broadcast("SessionManifest", _host.BuildSessionManifest(args.SessionId));
        }

        private void OnSessionPaused(SessionPausedEventArgs args) =>
            _host.Broadcast("SessionPaused", new SessionDto(args.SessionId, _host.ResolveTimestamp(args.TimestampMs)));

        private void OnSessionResumed(SessionResumedEventArgs args) =>
            _host.Broadcast("SessionResumed", new SessionDto(args.SessionId, _host.ResolveTimestamp(args.TimestampMs)));

        private void OnSessionEnded(SessionEndedEventArgs args) =>
            _host.Broadcast("SessionEnded", new SessionDto(args.SessionId, _host.ResolveTimestamp(args.TimestampMs)));

        private void OnSessionCompleted(SessionCompletedEventArgs args)
        {
            var dto = new SessionCompletedDto
            {
                sessionId = args.SessionId,
                timestampMs = _host.ResolveTimestamp(args.TimestampMs),
                totalElapsedTime = args.totalElapsedTime,
                totalScore = args.totalScore,
                tasksCompleted = args.tasksCompleted,
                totalTasks = args.totalTasks,
                orderViolationCount = args.orderViolationCount
            };
            _host.Broadcast("SessionCompleted", dto);
            _host.QueueSessionLogBroadcast(args.SessionId, args.PlayerId);
        }

        private void OnTask(TaskEventArgs args)
        {
            string eventType;
            string status;
            switch (args.Phase)
            {
                case TaskPhase.Started: eventType = "TaskStarted"; status = "active"; break;
                case TaskPhase.Timeout: eventType = "TaskTimeout"; status = "failed"; break;
                case TaskPhase.Completed:
                default: eventType = "TaskCompleted"; status = "completed"; break;
            }
            var stamped = args;
            stamped.TimestampMs = _host.ResolveTimestamp(args.TimestampMs);
            _host.Broadcast(eventType, DashboardDtoMapper.BuildTaskDto(stamped, status, _host.KnownGroups, _host.Scoring));
        }

        private void OnGroup(TaskGroupEventArgs args)
        {
            var group = args.Group;
            var dto = new GroupDto
            {
                sessionId = args.SessionId,
                groupId = group != null ? group.groupName : string.Empty,
                groupName = group != null ? group.groupName : string.Empty,
                timestampMs = _host.ResolveTimestamp(args.TimestampMs)
            };

            if (args.Phase == TaskGroupPhase.Completed)
            {
                _host.Broadcast("GroupCompleted", dto);
            }
            else
            {
                if (group != null) _host.RegisterKnownGroup(group);
                _host.Broadcast("GroupStarted", dto);
            }
            _host.Broadcast("SessionManifest", _host.BuildSessionManifest(args.SessionId));
        }

        private void OnScoreChanged(ScoreChangedEventArgs args) =>
            _host.Broadcast("ScoreChanged", new ScoreDto
            {
                sessionId = args.SessionId,
                totalScore = args.TotalScore,
                delta = args.Delta,
                timestampMs = _host.ResolveTimestamp(args.TimestampMs)
            });

        private void OnPpeStateChanged(PPEStateChangedEventArgs args)
        {
            if (!_host.VerboseEvents) return;
            _host.Broadcast("PpeChanged", new PpeDto
            {
                sessionId = args.SessionId,
                ppeType = args.PpeType.ToString(),
                isWearing = args.IsWearing,
                timestampMs = _host.ResolveTimestamp(args.TimestampMs)
            });
        }

        private void OnActionAttempt(ActionAttemptedEvent args)
        {
            if (!_host.VerboseEvents) return;
            var hasPos = args.Position.HasValue;
            _host.Broadcast("ActionAttempt", new ActionAttemptDto
            {
                sessionId = args.SessionId,
                actionId = args.ActionId,
                sourceId = args.SourceId!,
                context = args.Context!,
                interactorId = args.InteractorId,
                px = hasPos ? args.Position!.Value.X : 0f,
                py = hasPos ? args.Position!.Value.Y : 0f,
                pz = hasPos ? args.Position!.Value.Z : 0f,
                hasPosition = hasPos,
                time = args.TimestampMs / 1000f,
                timestampMs = _host.ResolveTimestamp(args.TimestampMs)
            });
        }

        private void OnSafetyViolation(SafetyViolationEventArgs args) =>
            _host.Broadcast("SafetyViolation", new SafetyViolationDto
            {
                sessionId = args.SessionId,
                violationCode = args.ViolationCode,
                message = args.Message,
                taskId = args.TaskId,
                groupId = args.GroupId,
                timestampMs = _host.ResolveTimestamp(args.TimestampMs)
            });

        private void OnCriticalSafetyFailure(CriticalSafetyFailureEventArgs args) =>
            _host.Broadcast("CriticalSafetyFailure", new CriticalFailureDto
            {
                sessionId = args.SessionId,
                reason = args.Reason,
                violationCount = args.ViolationCount,
                windowSeconds = args.WindowSeconds,
                timestampMs = _host.ResolveTimestamp(args.TimestampMs)
            });

        private void OnSafetyError(SafetyErrorEventArgs args) =>
            _host.Broadcast("SafetyError", new SafetyErrorDto
            {
                sessionId = args.SessionId,
                source = args.Source,
                message = args.Message,
                details = args.Details,
                timestampMs = _host.ResolveTimestamp(args.TimestampMs)
            });
    }
}
