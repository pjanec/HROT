using Fdp.Toolkit.Orchestration.Handlers;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// ⭐⭐⭐ <c>P5c′</c> (Q91 §4-G / §4-G′, <c>R-4003</c>, <c>R-4011</c>) — <b>IG records and archives like every ECS host</b>, asserted on
/// the CONSTRUCTED node. ⛔ Replaces <c>NodeRolePersistenceRails.IgCompositionRoot_RegistersNoCheckpointSaveHandler</c>'s
/// archive half, whose premise ("IG carries no ECS frame data, so it must not record") predates the unification. ⛔ Not a
/// source scan: after P5b no host's source names the archive handler, so a text rail would be vacuous both ways.
/// </summary>
public sealed class IgRecordsAndArchivesTests : System.IDisposable
{
    private readonly IgApplication _app;

    public IgRecordsAndArchivesTests()
    {
        _app = new IgApplication();
        // Headless, its own DDS domain (232) so it cannot collide with the other IG suites.
        _app.InitializeEmbedded(headless: true, domainIdOverride: 232, networkFactory: IgTestFactory.CreateHeadless());
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public void IG_holds_the_ECS_recording_controller_not_the_listen_only_one()
    {
        Assert.NotNull(_app.TestHook_RecordReplayController);
    }

    [Fact]
    public void IG_registers_the_archive_handler_so_an_export_includes_its_recording()
    {
        var slave = _app.TestHook_ClusterSlave;
        Assert.NotNull(slave);
        Assert.True(slave!.IsHandlerRegistered<ReferenceArchiveHandler>());
    }
}
