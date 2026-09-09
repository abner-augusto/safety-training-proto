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
    }
}
