namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Converts a Blueprint asset GUID to the 32-bit integer blueprint ID used at runtime.
/// Uses FNV-1a 32-bit hash of the 16 GUID bytes.
/// Per Runtime DD §2.6 and Compiler DD §12.2 (M-5).
/// </summary>
public static class BlueprintIdHash
{
    /// <summary>
    /// Computes the 32-bit blueprint ID from the asset GUID using FNV-1a.
    /// </summary>
    public static int Compute(Guid assetId)
        => global::Fdp.Toolkit.Behavior.Shared.BlueprintIdFnv.Compute(assetId);   // ⭐ CE-2036 — the one formula
}
