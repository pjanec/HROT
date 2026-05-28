using System;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class StaleBadgeWatcherTests
{
    private static ComparisonSessionState MakeSession(Guid assetId) =>
        new ComparisonSessionState(assetId, new ComparisonResponse(
            null, "Summary.", Array.Empty<ComparisonChange>(), Array.Empty<string>()));

    // ---- OnAssetSaved marks session stale -----------------------------------

    [Fact]
    public void OnAssetSaved_ActiveSession_MarksStale()
    {
        var assetId  = Guid.NewGuid();
        var registry = new ComparisonSessionRegistry();
        registry.SetSession(MakeSession(assetId));

        var watcher = new StaleBadgeWatcher(registry);
        watcher.OnAssetSaved(assetId);

        Assert.True(registry.GetSession(assetId)!.IsStale);
    }

    // ---- OnAssetSaved without session does not throw -----------------------

    [Fact]
    public void OnAssetSaved_NoSession_DoesNotThrow()
    {
        var assetId  = Guid.NewGuid();
        var registry = new ComparisonSessionRegistry();
        var watcher  = new StaleBadgeWatcher(registry);

        var ex = Record.Exception(() => watcher.OnAssetSaved(assetId));
        Assert.Null(ex);
    }

    // ---- Re-applying response (SetSession) resets stale --------------------

    [Fact]
    public void SetNewSession_AfterStale_NewSessionIsNotStale()
    {
        var assetId  = Guid.NewGuid();
        var registry = new ComparisonSessionRegistry();
        registry.SetSession(MakeSession(assetId));

        var watcher = new StaleBadgeWatcher(registry);
        watcher.OnAssetSaved(assetId);
        Assert.True(registry.GetSession(assetId)!.IsStale);

        // Replace session with a fresh one.
        registry.SetSession(MakeSession(assetId));

        Assert.False(registry.GetSession(assetId)!.IsStale);
    }
}
