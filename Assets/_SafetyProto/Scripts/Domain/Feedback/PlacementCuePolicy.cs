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
    /// When the install cues sound while a participant holds a piece: on entering the near-miss
    /// or the valid pose, then again every repeat interval while it stays there, so a single
    /// short beep lost in room noise is not the only chance to hear it. Two cues are never closer
    /// than the minimum gap, so hand jitter on a tolerance edge, which flips the pose every few
    /// frames, does not turn into a burst. Away stays silent, and letting go resets the pose.
    /// </summary>
    public sealed class PlacementCuePolicy
    {
        private readonly float _nearMissRepeatSeconds;
        private readonly float _validRepeatSeconds;
        private readonly float _minGapSeconds;
        private PlacementPose _pose = PlacementPose.Away;
        private bool _playedThisEntry;
        private float _lastCueAt = float.NegativeInfinity;
        private float _nextCueAt;

        /// <param name="nearMissRepeatSeconds">Repeat interval in the near-miss pose; 0 plays once.</param>
        /// <param name="validRepeatSeconds">Repeat interval in the valid pose; 0 plays once.</param>
        /// <param name="minGapSeconds">Shortest time between any two cues.</param>
        public PlacementCuePolicy(float nearMissRepeatSeconds, float validRepeatSeconds, float minGapSeconds = 0.25f)
        {
            _nearMissRepeatSeconds = nearMissRepeatSeconds;
            _validRepeatSeconds = validRepeatSeconds;
            _minGapSeconds = minGapSeconds > 0f ? minGapSeconds : 0f;
        }

        public PlacementCue Tick(float now, bool held, PlacementPose pose)
        {
            if (!held)
            {
                _pose = PlacementPose.Away;
                return PlacementCue.None;
            }

            if (pose != _pose)
            {
                _pose = pose;
                _playedThisEntry = false;
                _nextCueAt = System.Math.Max(now, _lastCueAt + _minGapSeconds);
            }
            if (pose == PlacementPose.Away || now < _nextCueAt) return PlacementCue.None;

            float repeat = pose == PlacementPose.Valid ? _validRepeatSeconds : _nearMissRepeatSeconds;
            if (_playedThisEntry && repeat <= 0f) return PlacementCue.None;

            _playedThisEntry = true;
            _lastCueAt = now;
            _nextCueAt = now + System.Math.Max(repeat, _minGapSeconds);
            return pose == PlacementPose.Valid ? PlacementCue.Valid : PlacementCue.NearMiss;
        }
    }
}
