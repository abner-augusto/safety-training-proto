namespace SafetyProto.Core
{
    public enum PPEType
    {
        None       = 0,
        Helmet     = 1,
        // 2 was the legacy 'Gloves'; replaced by GloveLeft/GloveRight. Slot intentionally skipped to keep
        // ordinal compatibility with serialized assets that already use Goggles=3, Harness=4, Vest=5, Boots=6.
        Goggles    = 3,
        Harness    = 4,
        Vest       = 5,
        Boots      = 6,
        GloveLeft  = 7,
        GloveRight = 8,
    }
}
