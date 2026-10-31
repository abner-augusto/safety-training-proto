#nullable enable
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Domain.Safety;

namespace SafetyProto.Domain.Feedback
{
    /// <summary>What kind of interactive thing an attention target is.</summary>
    public enum AttentionTargetKind
    {
        Ppe,
        Distractor,
        BodySlot,
        ScaffoldPiece,
        ScaffoldSocket,
        Anchor,
        Lanyard,
        ActionObject,
        Tool,
    }

    /// <summary>
    /// One thing the app can make pulse on request: a stable id the dashboard sends back, a
    /// Portuguese label for the evaluator, and the key that ties it to scenario data (a PPE type
    /// name, an action id), so a hint can find the target of a task.
    /// </summary>
    public sealed class AttentionTargetInfo
    {
        public AttentionTargetInfo(string id, AttentionTargetKind kind, string label, string key)
        {
            Id = id;
            Kind = kind;
            Label = label;
            Key = key;
        }

        public string Id { get; }
        public AttentionTargetKind Kind { get; }
        public string Label { get; }
        public string Key { get; }
    }

    /// <summary>Ids, labels and limits of on-demand attention cues.</summary>
    public static class AttentionTargets
    {
        public const float DefaultSeconds = 8f;
        public const float MinSeconds = 2f;
        public const float MaxSeconds = 30f;

        /// <summary>Key of the lanyard and anchor targets: the action that connects them.</summary>
        public const string ConnectLanyardKey = "connect_harness";

        /// <summary>The requested duration, or the default when absent, kept inside the allowed range.</summary>
        public static float ClampSeconds(float requested)
        {
            if (requested <= 0f || float.IsNaN(requested)) return DefaultSeconds;
            return requested < MinSeconds ? MinSeconds : requested > MaxSeconds ? MaxSeconds : requested;
        }

        /// <summary>Stable id built from the kind and the scene object's name.</summary>
        public static string IdFor(AttentionTargetKind kind, string objectName)
            => $"{Token(kind)}:{(objectName ?? string.Empty).Trim()}";

        public static AttentionTargetInfo ForPpe(string objectName, PPEType type)
            => new AttentionTargetInfo(IdFor(AttentionTargetKind.Ppe, objectName), AttentionTargetKind.Ppe,
                PpeLabels.DisplayName(type), type.ToString());

        public static AttentionTargetInfo ForDistractor(string objectName, string? distractorName, string distractorId)
            => new AttentionTargetInfo(IdFor(AttentionTargetKind.Distractor, objectName), AttentionTargetKind.Distractor,
                $"Distrator: {(string.IsNullOrWhiteSpace(distractorName) ? objectName : distractorName)}", distractorId ?? string.Empty);

        public static AttentionTargetInfo ForBodySlot(string objectName, IReadOnlyList<PPEType> accepts)
        {
            var labels = new List<string>(accepts.Count);
            foreach (var type in accepts) labels.Add(PpeLabels.DisplayName(type));
            string what = labels.Count > 0 ? string.Join(" / ", labels) : objectName;
            var names = new List<string>(accepts.Count);
            foreach (var type in accepts) names.Add(type.ToString());
            string key = string.Join(",", names);
            return new AttentionTargetInfo(IdFor(AttentionTargetKind.BodySlot, objectName), AttentionTargetKind.BodySlot,
                $"Encaixe no corpo: {what}", key);
        }

        /// <summary>Whether a body-slot target takes <paramref name="type"/>.</summary>
        public static bool SlotTakes(AttentionTargetInfo slot, PPEType type)
        {
            if (slot.Kind != AttentionTargetKind.BodySlot || string.IsNullOrEmpty(slot.Key)) return false;
            foreach (var name in slot.Key.Split(','))
                if (name == type.ToString()) return true;
            return false;
        }

        public static AttentionTargetInfo ForScaffoldPiece(string objectName, string actionId, string? taskName)
            => new AttentionTargetInfo(IdFor(AttentionTargetKind.ScaffoldPiece, objectName), AttentionTargetKind.ScaffoldPiece,
                $"Peça: {TaskOr(taskName, objectName)}", actionId);

        public static AttentionTargetInfo ForScaffoldSocket(string objectName, string actionId, string? taskName)
            => new AttentionTargetInfo(IdFor(AttentionTargetKind.ScaffoldSocket, objectName), AttentionTargetKind.ScaffoldSocket,
                $"Local de instalação: {TaskOr(taskName, objectName)}", actionId);

        public static AttentionTargetInfo ForAnchor(string objectName)
            => new AttentionTargetInfo(IdFor(AttentionTargetKind.Anchor, objectName), AttentionTargetKind.Anchor,
                "Ponto de ancoragem", ConnectLanyardKey);

        public static AttentionTargetInfo ForLanyard(string objectName)
            => new AttentionTargetInfo(IdFor(AttentionTargetKind.Lanyard, objectName), AttentionTargetKind.Lanyard,
                "Talabarte (mosquetão)", ConnectLanyardKey);

        public static AttentionTargetInfo ForActionObject(string objectName, string actionId, string? taskName)
            => new AttentionTargetInfo(IdFor(AttentionTargetKind.ActionObject, objectName), AttentionTargetKind.ActionObject,
                TaskOr(taskName, objectName), actionId);

        public static AttentionTargetInfo ForTool(string objectName)
            => new AttentionTargetInfo(IdFor(AttentionTargetKind.Tool, objectName), AttentionTargetKind.Tool,
                objectName, string.Empty);

        /// <summary>Wire token of a kind, as the dashboard groups targets.</summary>
        public static string Token(AttentionTargetKind kind) => kind switch
        {
            AttentionTargetKind.Ppe => "ppe",
            AttentionTargetKind.Distractor => "distractor",
            AttentionTargetKind.BodySlot => "slot",
            AttentionTargetKind.ScaffoldPiece => "piece",
            AttentionTargetKind.ScaffoldSocket => "socket",
            AttentionTargetKind.Anchor => "anchor",
            AttentionTargetKind.Lanyard => "lanyard",
            AttentionTargetKind.ActionObject => "action",
            _ => "tool",
        };

        private static string TaskOr(string? taskName, string objectName)
            => string.IsNullOrWhiteSpace(taskName) ? objectName : taskName!.Trim();
    }
}
