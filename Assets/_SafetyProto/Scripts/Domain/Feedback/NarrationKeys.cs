namespace SafetyProto.Domain.Feedback
{
    /// <summary>
    /// Stable keys that tie a narrated popup to its voice-over clip. The popup's caller names the
    /// key; the PopupService narration table maps it to a clip. Keys derive from scenario ids, never
    /// from the popup text, so rewording a warning cannot silently unhook its narration.
    /// </summary>
    public static class NarrationKeys
    {
        public const string PhaseCompleted = "fase_concluida";
        public const string InspectionPassed = "inspecao_concluida";
        public const string PrerequisitePending = "conecte_se_primeiro";

        private const string DistractorPrefix = "distractor:";
        private const string HazardReportPrefix = "report:";

        /// <summary>The "EPI Incorreto" warning for one decoy, keyed by its scenario <c>distractorId</c>.
        /// Empty for an empty id, so an unauthored decoy stays silent.</summary>
        public static string ForDistractor(string distractorId) => Prefixed(DistractorPrefix, distractorId);

        /// <summary>The classification popup of a hazard-report task, keyed by the task id.</summary>
        public static string ForHazardReport(string taskId) => Prefixed(HazardReportPrefix, taskId);

        private static string Prefixed(string prefix, string id)
            => string.IsNullOrWhiteSpace(id) ? string.Empty : prefix + id.Trim();
    }

    /// <summary>Timing rules shared by narrated popups.</summary>
    public static class NarrationTiming
    {
        /// <summary>
        /// Auto-close delay for a self-closing popup that carries narration: never shorter than the
        /// authored delay, and long enough for the line to finish (plus <paramref name="tailSeconds"/>)
        /// after waiting <paramref name="waitSeconds"/> for a line already playing. An authored delay of
        /// zero means "closes manually" and is kept.
        /// </summary>
        public static float AutoCloseSeconds(float authoredSeconds, float waitSeconds, float clipSeconds, float tailSeconds)
        {
            if (authoredSeconds <= 0f || clipSeconds <= 0f) return authoredSeconds;
            float needed = (waitSeconds > 0f ? waitSeconds : 0f) + clipSeconds + (tailSeconds > 0f ? tailSeconds : 0f);
            return needed > authoredSeconds ? needed : authoredSeconds;
        }
    }
}
