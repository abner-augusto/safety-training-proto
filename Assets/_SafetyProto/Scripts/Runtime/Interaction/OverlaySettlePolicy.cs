namespace SafetyProto.Runtime.Interaction
{
    /// <summary>
    /// Decides when a head-following canvas may sit on its compositor layer. On the Quest 3 a
    /// layer canvas that moves or fades shows its content offset from its background and leaves
    /// stale copies behind, so the layer is reserved for a canvas that is opaque and at rest.
    /// </summary>
    public static class OverlaySettlePolicy
    {
        public const float OpaqueAlpha = 0.99f;

        /// <summary>True when the canvas is opaque, not following and has held still long enough.</summary>
        public static bool ShouldUseLayer(bool following, float alpha, float stillFor, float settleSeconds)
        {
            return !following && alpha >= OpaqueAlpha && stillFor >= settleSeconds;
        }
    }
}
