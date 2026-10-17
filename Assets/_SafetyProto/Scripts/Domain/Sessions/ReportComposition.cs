#nullable enable
using System.Collections.Generic;
using SafetyProto.Domain.Scoring;

namespace SafetyProto.Domain.Sessions
{
    /// <summary>Which card the finish report leads with.</summary>
    public enum PrimaryCardKind
    {
        /// <summary>A task needs improvement.</summary>
        Issue,

        /// <summary>No task is an issue, but points were taken off.</summary>
        ChargesOnly,

        /// <summary>A clean run.</summary>
        Safe,
    }

    /// <summary>
    /// Decisions the finish report makes from the session outcome, kept free of UI so they can be
    /// tested. The participant-facing wording stays with the UI and is passed in.
    /// </summary>
    public static class ReportComposition
    {
        /// <summary>Issue wins over charges, and charges win over the clean-run card. An order
        /// deviation never takes part: it costs nothing and is only a note.</summary>
        public static PrimaryCardKind DecidePrimaryCard(bool hasIssueTask, int totalCharged)
        {
            if (hasIssueTask) return PrimaryCardKind.Issue;
            return totalCharged > 0 ? PrimaryCardKind.ChargesOnly : PrimaryCardKind.Safe;
        }

        /// <summary>Appends the out-of-order sentence to a card body when any task was
        /// completed out of order.</summary>
        public static string AppendOrderNote(string body, bool anyCompletedOutOfOrder, string orderNote)
        {
            if (!anyCompletedOutOfOrder) return body;
            return string.IsNullOrEmpty(body) ? orderNote.TrimStart() : body + orderNote;
        }

        /// <summary>Fills the {0} of the charges-only body with the points taken off.</summary>
        public static string FormatChargesBody(string template, int totalCharged) =>
            string.Format(template, totalCharged);

        /// <summary>A task row's detail: advice, then its charges, then the out-of-order line.
        /// The order line carries no points and does not change the row's tone.</summary>
        public static string BuildRowDetail(string? advice, IReadOnlyList<ScoreCharge> charges,
                                            bool completedOutOfOrder, string orderLine)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(advice)) lines.Add(advice!);
            foreach (var charge in charges) lines.Add(ScoreChargeLabels.FormatLine(charge));
            if (completedOutOfOrder) lines.Add(orderLine);
            return string.Join("\n", lines);
        }
    }
}
