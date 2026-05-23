namespace Hrot.Editor.AiShared.Identity;

public static class AssetIdHash
{
    private const uint OffsetBasis = 2166136261u;
    private const uint Prime = 16777619u;

    public static int Fnv1a32(ReadOnlySpan<byte> bytes)
    {
        uint hash = OffsetBasis;
        for (int i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= Prime;
        }
        return unchecked((int)hash);
    }
}
