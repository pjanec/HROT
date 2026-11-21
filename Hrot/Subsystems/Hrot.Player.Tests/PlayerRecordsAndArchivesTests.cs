using Fdp.Toolkit.Orchestration.Handlers;
using Xunit;

namespace Hrot.Player.Tests;

/// <summary>
/// ⭐⭐⭐ <c>P5c′</c> (Q91 §4-G / §4-G′, <c>R-4003</c>, <c>R-4011</c>) — <b>IG records and archives like every ECS host</b>, asserted on
/// the CONSTRUCTED node. ⛔ Replaces <c>NodeRolePersistenceRails.IgCompositionRoot_RegistersNoCheckpointSaveHandler</c>'s
/// archive half, whose premise ("IG carries no ECS frame data, so it must not record") predates the unification. ⛔ Not a
/// source scan: after P5b no host's source names the archive handler, so a text rail would be vacuous both ways.
/// </summary>
public sealed class PlayerRecordsAndArchivesTests : System.IDisposable
{
    private readonly PlayerApplication _app;

    public PlayerRecordsAndArchivesTests()
    {
        _app = new PlayerApplication();
        // Headless, its own DDS domain (232) so it cannot collide with the other IG suites.
        _app.InitializeEmbedded(headless: true, domainIdOverride: 232, networkFactory: PlayerTestFactory.CreateHeadless());
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Player_holds_the_ECS_recording_controller_not_the_listen_only_one()
    {
        Assert.NotNull(_app.TestHook_RecordReplayController);
    }

    /// <summary>
    /// ⭐ CE-4331 ① (Recording Storage §9a) — the ONE composition hands the recording controller the node's identity, so every recording says which node made it:
    /// its name and subsystem as the heartbeat says them (an AAR on another cluster places files by name, then by kind). Silent-default rule: asserted on the
    /// CONSTRUCTED controller. 🔴 Mutation: Compose not setting RecordingSubsystem ⇒ empty ⇒ red.
    /// </summary>
    [Fact]
    public void CE4331_ThePlayersRecordingController_CarriesTheNodesNameAndSubsystem()
    {
        var c = _app.TestHook_RecordReplayController!;
        var slave = _app.TestHook_ClusterSlave!;
        Assert.Equal(slave.SubsystemName, c.RecordingSubsystem);
        Assert.False(string.IsNullOrEmpty(c.RecordingSubsystem));
        Assert.Equal(slave.NodeName, c.RecordingNodeName);
    }

    [Fact]
    public void Player_registers_the_archive_handler_so_an_export_includes_its_recording()
    {
        var slave = _app.TestHook_ClusterSlave;
        Assert.NotNull(slave);
        Assert.True(slave!.IsHandlerRegistered<ReferenceArchiveHandler>());
    }
}
