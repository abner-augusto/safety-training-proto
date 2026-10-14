namespace SafetyProto.Core
{
    public enum PPEType
    {
        None       = 0,
        Helmet     = 1,
        // Value 2 is intentionally unused: serialized assets depend on these explicit values.
        Goggles    = 3,
        Harness    = 4,
        Vest       = 5,
        Boots      = 6,
        GloveLeft  = 7,
        GloveRight = 8,
    }
}
