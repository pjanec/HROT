using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization.Migrations.Adapters;
using Hrot.Editor.Migration;
using Xunit;

namespace Hrot.Editor.Tests.Migration;

public class MigrationAlertManagerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DocumentMeta Meta(int version) =>
        new DocumentMeta("Hrot.Scenario", version);

    private static MigrationLoadResult MakeResult(
        bool wasMigrated, bool isDegraded = false,
        int from = 1, int to = 2) =>
        new MigrationLoadResult
        {
            Dom          = new System.Text.Json.Nodes.JsonObject(),
            OriginalMeta = Meta(wasMigrated ? from : to),
            CurrentMeta  = Meta(to),
            IsDegraded   = isDegraded,
        };

    // ── OnScenarioLoaded ──────────────────────────────────────────────────────

    [Fact]
    public void OnScenarioLoaded_WasMigrated_QueuesPendingAlert()
    {
        var mgr = new MigrationAlertManager();
        mgr.OnScenarioLoaded(MakeResult(wasMigrated: true));
        Assert.True(mgr.HasPendingAlert);
    }

    [Fact]
    public void OnScenarioLoaded_WasNotMigrated_NoPendingAlert()
    {
        var mgr = new MigrationAlertManager();
        mgr.OnScenarioLoaded(MakeResult(wasMigrated: false, from: 2, to: 2));
        Assert.False(mgr.HasPendingAlert);
    }

    [Fact]
    public void OnScenarioLoaded_IsDegraded_SetsDegradedMode()
    {
        var mgr = new MigrationAlertManager();
        mgr.OnScenarioLoaded(MakeResult(wasMigrated: true, isDegraded: true));
        Assert.True(mgr.IsDegradedMode);
    }

    [Fact]
    public void OnScenarioLoaded_NotDegraded_NotDegradedMode()
    {
        var mgr = new MigrationAlertManager();
        mgr.OnScenarioLoaded(MakeResult(wasMigrated: false, from: 2, to: 2));
        Assert.False(mgr.IsDegradedMode);
    }

    [Fact]
    public void OnScenarioLoaded_Null_NoEffect()
    {
        var mgr = new MigrationAlertManager();
        mgr.OnScenarioLoaded(null);
        Assert.False(mgr.HasPendingAlert);
        Assert.False(mgr.IsDegradedMode);
    }

    // ── Session suppression ───────────────────────────────────────────────────

    [Fact]
    public void SuppressForSession_SubsequentMigratedLoad_NoPendingAlert()
    {
        var mgr = new MigrationAlertManager();
        mgr.OnScenarioLoaded(MakeResult(wasMigrated: true));   // first load: alert queued
        mgr.SuppressAlertsForSession();                         // user checks checkbox
        mgr.OnScenarioLoaded(MakeResult(wasMigrated: true));   // second load: suppressed
        Assert.False(mgr.HasPendingAlert);
    }

    // ── OnScenarioCleared ─────────────────────────────────────────────────────

    [Fact]
    public void OnScenarioCleared_ClearsCurrentResultAndPendingAlert()
    {
        var mgr = new MigrationAlertManager();
        mgr.OnScenarioLoaded(MakeResult(wasMigrated: true, isDegraded: true));
        mgr.OnScenarioCleared();
        Assert.False(mgr.HasPendingAlert);
        Assert.False(mgr.IsDegradedMode);
    }
}
