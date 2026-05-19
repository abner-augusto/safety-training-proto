namespace SafetyProto.Core
{
    public enum PPEType
    {
        None      = 0,
        Helmet    = 1,
        Gloves    = 2, // Legacy — kept for existing serialized assets. Prefer GloveLeft/GloveRight.
        Goggles   = 3,
        Harness   = 4,
        Vest      = 5,
        Boots     = 6,
        GloveLeft  = 7,
        GloveRight = 8,
    }
}
