namespace SafetyProto.Core
{
    /// <summary>
    /// Stable violation and refusal codes shared by <see cref="Events.SafetyViolationEventArgs"/>
    /// and <see cref="Events.ActionRefusedEventArgs"/>. These strings are recorded in session logs
    /// and compared across scenario revisions, so a value here must never change once shipped —
    /// add a new constant instead of repurposing one.
    /// </summary>
    public static class ViolationCodes
    {
        public const string NoActiveGroup = "NO_ACTIVE_GROUP";
        public const string WrongAction = "WRONG_ACTION";
        public const string PrerequisitePending = "PREREQUISITE_PENDING";
        public const string PpeMissing = "PPE_MISSING";
        public const string TaskNotPerformed = "TASK_NOT_PERFORMED";

        /// <summary>Participant chose an incorrect classification option on a hazard-report
        /// popup (e.g. "report_damaged_safety_mesh"). The task still completes — this only
        /// charges the tier's base penalty (ScoringConfig.BasePenaltyFor).</summary>
        public const string HazardMisclassified = "HAZARD_MISCLASSIFIED";

        /// <summary>Participant tried to don a PPE item ahead of its step in a Sequential group.
        /// The slot refuses the snap; this is a log signal only and charges no points.</summary>
        public const string PpeOutOfOrder = "PPE_OUT_OF_ORDER";

        /// <summary>An action attempt arrived with an empty action id; nothing can be matched.</summary>
        public const string ActionIdMissing = "ACTION_ID_MISSING";

        /// <summary>Phase-advance gate: the group's tasks were completed in an order that deviates
        /// from the authored sequence. Charges the incidental tier's base penalty once per gate.</summary>
        public const string OrderViolation = "ORDER_VIOLATION";

        /// <summary>Score reason for the per-task charge applied when the inspection gate is pressed
        /// with tasks still pending. A reason, not a violation: the violation raised alongside it is
        /// <see cref="GateFailed"/> or <see cref="InspectionIncomplete"/>.</summary>
        public const string GatePenalty = "GATE_PENALTY";

        /// <summary>Inspection gate pressed with a pending task that has a scripted consequence.</summary>
        public const string GateFailed = "GATE_FAILED";

        /// <summary>Inspection gate pressed with a pending task that has no scripted consequence.</summary>
        public const string InspectionIncomplete = "INSPECTION_INCOMPLETE";

        /// <summary>A distractor (non-required) PPE item was snapped. Always logged; charged once per
        /// unique item.</summary>
        public const string WrongPpeSelected = "WRONG_PPE_SELECTED";
    }
}
