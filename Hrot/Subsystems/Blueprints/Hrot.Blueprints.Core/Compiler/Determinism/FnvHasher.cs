namespace Hrot.Blueprints.Core.Compiler.Determinism;

public static class FnvHasher
{
    private const uint Fnv32Prime   = 16777619u;
    private const uint Fnv32Offset  = 2166136261u;
    private const ulong Fnv64Prime  = 1099511628211UL;
    private const ulong Fnv64Offset = 14695981039346656037UL;

    public static uint Hash32(ReadOnlySpan<byte> data)
    {
        uint hash = Fnv32Offset;
        foreach (var b in data)
            hash = (hash ^ b) * Fnv32Prime;
        return hash;
    }

    public static ulong Hash64(ReadOnlySpan<byte> data)
    {
        ulong hash = Fnv64Offset;
        foreach (var b in data)
            hash = (hash ^ b) * Fnv64Prime;
        return hash;
    }
}
