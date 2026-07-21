using System;
using System.Collections.Generic;
using System.Linq;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Scoring;

namespace SafetyProto.Domain.Dashboard
{
    /// <summary>
    /// Pure translation from domain event/task data to the dashboard wire DTOs.
    /// Engine-independent: no UnityEngine, no scene lookups, no singletons, no clock -
    /// every piece of ambient state (scoring config, the known task groups, the resolved
    /// timestamp on the event args) is supplied by the caller. This is the seam that lets
    /// the dashboard's mapping logic be unit-tested without the Unity Editor.
    /// </summary>
    public static class DashboardDtoMapper
    {
        public static string ResolveTaskStatus(TaskState state)
        {
            switch (state)
            {
                case TaskState.InProgress:
                    return "active";
                case TaskState.CompletedSuccess:
                case TaskState.CompletedSuccessButUnsafe:
                    return "completed";
                case TaskState.CompletedFailure:
                    return "failed";
                default:
                    return "pending";
            }
        }

        public static TaskMetadata BuildTaskMetadata(
            ISafetyTask? task,
            IReadOnlyList<ITaskGroup> knownGroups,
            ScoringConfig scoring,
            bool includeDetails = true)
        {
            if (task == null || knownGroups == null || knownGroups.Count == 0)
            {
                return TaskMetadata.Empty;
            }

            string groupName = string.Empty;
            string executionMode = string.Empty;
            int order = -1;
            int runningOrder = 1;

            foreach (var group in knownGroups)
            {
                if (group == null || group.tasks == null)
                    continue;

                foreach (var candidate in group.tasks)
                {
                    if (candidate == null)
                    {
                        runningOrder++;
                        continue;
                    }

                    if (candidate == task)
                    {
                        groupName = group.groupName;
                        executionMode = group.executionMode.ToString();
                        order = runningOrder;
                        goto Found;
                    }

                    runningOrder++;
                }
            }

        Found:
            var required = includeDetails && task.requiredPPE != null
                ? task.requiredPPE.Select(p => p.ToString()).ToArray()
                : Array.Empty<string>();

            return new TaskMetadata
            {
                groupName = groupName,
                executionMode = executionMode,
                order = order,
                description = task.taskDescription ?? string.Empty,
                hint = task.hintText ?? string.Empty,
                expectedAction = includeDetails ? task.ResolveExpectedActionId() : string.Empty,
                requiredPpe = required,
                successPoints = scoring.PointsFor(task.severity),
                failurePenalty = scoring.BasePenaltyFor(task.severity),
                ppePenalty = scoring.PointsFor(task.severity) - scoring.UnsafeEarnFor(task.severity)
            };
        }

        public static TaskDto BuildTaskDto(
            TaskEventArgs args,
            string status,
            IReadOnlyList<ITaskGroup> knownGroups,
            ScoringConfig scoring)
        {
            var task = args.Task;
            var name = task != null ? task.taskName : string.Empty;
            var id = name;
            var meta = BuildTaskMetadata(task, knownGroups, scoring);
            var severity = task?.severity ?? TaskSeverity.Moderate;
            return new TaskDto
            {
                sessionId = args.SessionId,
                taskId = id,
                taskName = name,
                taskDescription = meta.description,
                hint = meta.hint,
                groupName = meta.groupName,
                order = meta.order,
                executionMode = meta.executionMode,
                expectedAction = meta.expectedAction,
                requiredPpe = meta.requiredPpe,
                successPoints = scoring.PointsFor(severity),
                failurePenalty = scoring.BasePenaltyFor(severity),
                ppePenalty = scoring.PointsFor(severity) - scoring.UnsafeEarnFor(severity),
                status = status,
                timestampMs = args.TimestampMs
            };
        }

        public static TaskManifestItemDto BuildManifestItem(
            ISafetyTask task,
            IReadOnlyList<ITaskGroup> knownGroups,
            ScoringConfig scoring,
            string status)
        {
            var meta = BuildTaskMetadata(task, knownGroups, scoring, includeDetails: false);
            return new TaskManifestItemDto
            {
                taskName = task.taskName,
                groupName = meta.groupName,
                description = meta.description,
                order = meta.order,
                status = status
            };
        }
    }

    /// <summary>
    /// Intermediate, non-wire computation of a task's dashboard metadata (group, order,
    /// scoring). Not serialized - <see cref="DashboardDtoMapper"/> uses it to build the
    /// TaskDto and manifest items.
    /// </summary>
    public struct TaskMetadata
    {
        public string groupName;
        public string executionMode;
        public int order;
        public string description;
        public string hint;
        public string expectedAction;
        public string[] requiredPpe;
        public int successPoints;
        public int failurePenalty;
        public int ppePenalty;

        public static TaskMetadata Empty => new TaskMetadata
        {
            groupName = string.Empty,
            executionMode = string.Empty,
            order = -1,
            description = string.Empty,
            hint = string.Empty,
            expectedAction = string.Empty,
            requiredPpe = Array.Empty<string>(),
            successPoints = 0,
            failurePenalty = 0,
            ppePenalty = 0
        };
    }
}
