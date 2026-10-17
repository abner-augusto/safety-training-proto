#nullable enable
using System;
using System.Collections.Generic;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.Domain.Safety
{
    /// <summary>A decoy item together with the equip task that authored it.</summary>
    public sealed class DistractorMatch
    {
        public DistractorMatch(ISafetyTask task, IDistractor distractor)
        {
            Task = task;
            Distractor = distractor;
        }

        public ISafetyTask Task { get; }
        public IDistractor Distractor { get; }
    }

    /// <summary>
    /// Maps a scene item's <c>distractorId</c> to the decoy and the equip task that authored it,
    /// and builds the rejection feedback from them. The feedback comes from the item that was
    /// snapped, never from whichever task happens to be current: a participant can fumble a decoy
    /// for a later task while an earlier one is still open.
    /// </summary>
    public static class DistractorResolver
    {
        /// <summary>Returns null for an empty or unknown id.</summary>
        public static DistractorMatch? Resolve(IEnumerable<ITaskGroup>? groups, string? distractorId)
        {
            if (groups == null || string.IsNullOrWhiteSpace(distractorId)) return null;
            var wanted = distractorId!.Trim();

            foreach (var group in groups)
            {
                if (group?.tasks == null) continue;
                foreach (var task in group.tasks)
                {
                    if (task?.distractors == null) continue;
                    foreach (var distractor in task.distractors)
                    {
                        if (string.Equals(distractor.Id?.Trim(), wanted, StringComparison.Ordinal))
                            return new DistractorMatch(task, distractor);
                    }
                }
            }

            return null;
        }

        /// <summary>What a decoy's charge is counted against. Items sharing a <c>distractorId</c>
        /// are one wrong choice (the left and right rubber gloves are a pair), so the id wins; an
        /// item without one falls back to its own name.</summary>
        public static string ChargeKey(string? distractorId, string? itemName)
        {
            return string.IsNullOrWhiteSpace(distractorId)
                ? "item:" + (itemName ?? string.Empty)
                : "id:" + distractorId!.Trim();
        }

        /// <summary>The rejection advice, followed by the task's hint when it has one.</summary>
        public static string BuildRejectionBody(DistractorMatch match)
        {
            var body = match.Distractor.RejectionAdvice;
            var hint = match.Task.hintText;
            return string.IsNullOrWhiteSpace(hint) ? body : $"{body}\n\nDica: {hint}";
        }
    }
}
