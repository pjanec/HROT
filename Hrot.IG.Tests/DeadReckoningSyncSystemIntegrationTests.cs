using Hrot.IG;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// MODINIT-S302 success condition 6: IgApplication uses NedReplicationModule with
/// <c>driveFromNetwork=true</c>, so <see cref="Hrot.Common.Systems.DeadReckoningSyncSystem"/>
/// drives ALL non-authority entities (not just ghosts in <c>EntityLifecycle.Ghost</c> state).
/// </summary>
public sealed class DeadReckoningSyncSystemIntegrationTests : System.IDisposable
{
    private readonly IgApplication _app;

    public DeadReckoningSyncSystemIntegrationTests()
    {
        _app = new IgApplication();
        // Headless: no Raylib window. Domain 231 to avoid collision with other test suites.
        _app.InitializeEmbedded(headless: true, domainIdOverride: 231);
    }

    public void Dispose() => _app.Dispose();

    // ── SC6: structural property check ───────────────────────────────────────

    /// <summary>
    /// SC6-A: After init, NedReplicationModule is wired into IgApplication.
    /// Prerequisite for SC6-B.
    /// </summary>
    [Fact]
    public void IgApplication_AfterInit_NedReplicationIsWired()
    {
        Assert.NotNull(_app.TestHook_NedReplication);
    }

    /// <summary>
    /// SC6-B: IgApplication wires NedReplicationModule with <c>driveFromNetwork=true</c>.
    ///
    /// <para>
    /// With the old <c>new DeadReckoningSyncSystem()</c> inline registration, the DR system
    /// used the default constructor (driveFromNetwork=true), but it only processed
    /// <c>EntityLifecycle.Ghost</c> entities (that was the wrong old default).
    /// After the MODINIT-S302 migration, NedReplicationModule.RegisterSystems creates
    /// <c>DeadReckoningSyncSystem(_driveFromNetwork)</c> where <c>_driveFromNetwork = true</c>
    /// for <c>NodeRole.ImageGenerator</c> — no lifecycle filter applied.
    /// </para>
    /// </summary>
    [Fact]
    public void IgApplication_AfterInit_NedReplication_DriveFromNetworkIsTrue()
    {
        var ned = _app.TestHook_NedReplication;
        Assert.NotNull(ned);
        Assert.True(ned.DriveFromNetwork,
            "IG NedReplicationModule must have DriveFromNetwork=true " +
            "(ImageGenerator role: all non-authority entities are remote replicas).");
    }
}
