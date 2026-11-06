namespace SafetyProto.Domain.Feedback
{
    /// <summary>
    /// Which popup may take the shared panel from the one showing. The panel has one slot and
    /// replacing a popup drops its callbacks, so a notice must never replace a prompt that
    /// someone is waiting on (the phase-transition confirm, the inspection confirm, a hazard
    /// report choice, the keypad): its owner would wait forever.
    /// </summary>
    public static class PopupPreemption
    {
        /// <param name="promptShowing">A prompt (a popup with buttons the participant must answer) is visible.</param>
        /// <param name="incomingIsPrompt">The popup asking for the panel is itself a prompt.</param>
        public static bool MayReplace(bool promptShowing, bool incomingIsPrompt) =>
            !promptShowing || incomingIsPrompt;
    }
}
