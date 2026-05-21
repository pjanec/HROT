namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Converts a Blueprint asset GUID to the 32-bit integer blueprint ID used at runtime.
/// Uses FNV-1a 32-bit hash of the 16 GUID bytes.
/// Per Runtime DD §2.6 and Compiler DD §12.2 (M-5).
/// </summary>
public static class BlueprintIdHash
{
    // FNV-1a 32-bit constants
    private const uint OffsetBasis = 2166136261u;
    private const uint FnvPrime    = 16777619u;

    /// <summary>
    /// Computes the 32-bit blueprint ID from the asset GUID using FNV-1a.
    /// </summary>
    public static int Compute(Guid assetId)
    {
        Span<byte> bytes = stackalloc byte[16];
        assetId.TryWriteBytes(bytes);
        uint hash = OffsetBasis;
        for (int i = 0; i < 16; i++)
        {
            hash ^= bytes[i];
            hash *= FnvPrime;
        }
        return (int)hash;
    }
}
