namespace SafetyProto.Domain.Feedback
{
    /// <summary>Where a held piece sits relative to its install socket.</summary>
    public enum PlacementPose
    {
        /// <summary>Not close enough to the socket.</summary>
        Away,
        /// <summary>In position, but turned the wrong way.</summary>
        WrongAngle,
        /// <summary>Releasing now installs it.</summary>
        Valid,
    }

    /// <summary>The sound a placement state asks for on this frame.</summary>
    public enum PlacementCue
    {
        None,
        NearMiss,
        Valid,
    }

    /// <summary>
    /// When the install cues sound while a participant holds a piece: once on entering the
    /// near-miss or the valid pose, then again every repeat interval while it stays there, so a
    /// single short beep lost in room noise is not the only chance to hear it. Away stays silent,
    /// and letting go resets everything.
    /// </summary>
    public sealed class PlacementCuePolicy
    {
        private readonly float _nearMissRepeatSeconds;
        private readonly float _validRepeatSeconds;
        private PlacementPose _pose = PlacementPose.Away;
        private float _nextCueAt;

        /// <param name="nearMissRepeatSeconds">Repeat interval in the near-miss pose; 0 plays once.</param>
        /// <param name="validRepeatSeconds">Repeat interval in the valid pose; 0 plays once.</param>
        public PlacementCuePolicy(float nearMissRepeatSeconds, float validRepeatSeconds)
        {
            _nearMissRepeatSeconds = nearMissRepeatSeconds;
            _validRepeatSeconds = validRepeatSeconds;
        }

        public PlacementCue Tick(float now, bool held, PlacementPose pose)
        {
            if (!held)
            {
                _pose = PlacementPose.Away;
                return PlacementCue.None;
            }

            bool entered = pose != _pose;
            _pose = pose;
            if (pose == PlacementPose.Away) return PlacementCue.None;

            float repeat = pose == PlacementPose.Valid ? _validRepeatSeconds : _nearMissRepeatSeconds;
            if (!entered && (repeat <= 0f || now < _nextCueAt)) return PlacementCue.None;

            _nextCueAt = now + repeat;
            return pose == PlacementPose.Valid ? PlacementCue.Valid : PlacementCue.NearMiss;
        }
    }
}
