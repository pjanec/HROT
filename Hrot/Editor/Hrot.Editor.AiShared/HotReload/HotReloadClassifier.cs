namespace Hrot.Editor.AiShared.HotReload;

/// <summary>
/// Classifies a hot-reload based on hash deltas.
/// Each subsystem provides its own StructureHash and ParamHash computations;
/// this classifier is agnostic to what the hashes represent.
/// </summary>
public static class HotReloadClassifier
{
    /// <summary>
    /// Classify a reload by comparing before/after structure and param hashes.
    /// </summary>
    public static HotReloadTier Classify(
        int previousStructureHash, int newStructureHash,
        int previousParamHash, int newParamHash)
    {
        if (previousStructureHash != newStructureHash) return HotReloadTier.Hard;
        if (previousParamHash != newParamHash) return HotReloadTier.Soft;
        return HotReloadTier.Cosmetic;
    }

    /// <summary>
    /// When multiple changes coalesce (e.g. layout + soft), returns the most impactful tier.
    /// Hard > Soft > Cosmetic.
    /// </summary>
    public static HotReloadTier MostImpactful(HotReloadTier a, HotReloadTier b) =>
        (HotReloadTier)Math.Max((int)a, (int)b);
}
