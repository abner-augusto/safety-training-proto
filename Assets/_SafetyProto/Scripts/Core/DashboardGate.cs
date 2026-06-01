namespace SafetyProto.Core
{
    /// <summary>
    /// Process-wide gate to temporarily suspend the EvaluatorDashboard's high-frequency
    /// pose broadcast during latency-sensitive moments — e.g. the scaffold phase teleport,
    /// where a blocking WebSocket send on the main thread causes a frame hitch that can drop
    /// the player through colliders.
    ///
    /// Only the droppable pose stream is gated here; discrete gameplay events keep flowing so
    /// no analytics data is lost. Set by gameplay (PhaseController) and honored by PoseSender.
    /// </summary>
    public static class DashboardGate
    {
        public static bool PoseBroadcastSuspended;
    }
}
