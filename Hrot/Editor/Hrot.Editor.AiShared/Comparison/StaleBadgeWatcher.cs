namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// Watches for asset-save events and marks any active comparison session as stale.
/// See design section 6.9.
/// </summary>
public sealed class StaleBadgeWatcher
{
    private readonly ComparisonSessionRegistry _registry;

    public StaleBadgeWatcher(ComparisonSessionRegistry registry)
        => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    /// <summary>
    /// Call this when any asset is saved.
    /// If the asset has an active comparison session, marks it stale.
    /// No-op when the asset has no active session.
    /// </summary>
    public void OnAssetSaved(Guid assetId)
    {
        var session = _registry.GetSession(assetId);
        session?.MarkStale();
    }
}
