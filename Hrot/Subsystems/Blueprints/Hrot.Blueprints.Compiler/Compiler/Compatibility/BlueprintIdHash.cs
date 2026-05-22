namespace Fdp.Toolkit.Blueprints;

public static class BlueprintIdHash
{
    private const uint OffsetBasis = 2166136261u;
    private const uint FnvPrime = 16777619u;

    public static int Compute(Guid assetId)
    {
        var bytes = assetId.ToByteArray();
        uint hash = OffsetBasis;
        for (int i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= FnvPrime;
        }
        return (int)hash;
    }
}
