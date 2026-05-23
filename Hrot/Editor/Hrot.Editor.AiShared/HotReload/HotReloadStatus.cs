namespace Hrot.Editor.AiShared.HotReload;

/// <summary>Snapshot of the last hot-reload result, for status indicator display.</summary>
public sealed record HotReloadStatus(
    HotReloadTier Tier,
    int LiveInstanceCount)
{
    /// <summary>True when Tier is Hard and there are live instances to reset.</summary>
    public bool RequiresConfirmation => Tier == HotReloadTier.Hard && LiveInstanceCount > 0;
}
