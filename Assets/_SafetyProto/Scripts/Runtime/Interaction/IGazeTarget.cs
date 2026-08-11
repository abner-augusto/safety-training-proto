namespace SafetyProto.Runtime.Interaction
{
    /// <summary>
    /// Receives head-gaze notifications from the single scene-level <see cref="HeadGazeSource"/>.
    ///
    /// There is deliberately no enter/exit pair: the source only knows which target the ray is on
    /// <em>this</em> frame, and a target that lost the ray still needs to keep ticking in order to
    /// drain. Targets therefore treat "no call this frame" as "not gazed" and decay on their own.
    /// </summary>
    public interface IGazeTarget
    {
        /// <summary>Called once per frame, only while the head ray rests on this target.</summary>
        void OnGazed(float deltaTime);
    }
}
