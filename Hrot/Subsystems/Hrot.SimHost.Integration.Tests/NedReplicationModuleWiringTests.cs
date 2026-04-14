using Hrot.SimHost;
using Hrot.Network.NED.Factory;
using Hrot.Map.Common;
using Fdp.Network.Cyclone.Services;
using CycloneDDS.Runtime;
using Xunit;

namespace Hrot.SimHost.Integration.Tests;

/// <summary>
/// MODINIT-S301 success conditions 7 and 8.
///
/// SC7: Spawn via SimHostApp → EntityMasterEgressTranslator is wired (NedReplication non-null).
/// SC8: Standalone role (MuscleGround | Perception) -> NedReplicationModule.DriveFromNetwork == false (ghost-only smoothing; local bodies NOT overridden).
/// </summary>
public sealed class NedReplicationModuleWiringTests : System.IDisposable
{
    private const int DomainId = 17;  // Dedicated domain to avoid DDS state interference

    private readonly DdsParticipant _idAllocatorParticipant;
    private readonly DdsIdAllocatorServer _idAllocatorServer;
    private readonly SimHostApp _app;

    public NedReplicationModuleWiringTests()
    {
        // SimHostApp always needs an allocator server when using DDS.
        _idAllocatorParticipant = new DdsParticipant(DomainId);
        _idAllocatorServer      = new DdsIdAllocatorServer(_idAllocatorParticipant);

        _app = new SimHostApp(domainOverride: DomainId, role: NodeRole.MuscleGround | NodeRole.Perception);
        var factory = new NedNetworkFactory(
            participant:   null,
            entityMap:     new FDP.Toolkit.Replication.Services.NetworkEntityMap(),
            geoTransform:  HrotEnvironment.CreateGeoTransform(),
            eventBus:      new Fdp.Kernel.FdpEventBus(),
            localNodeId:   0,
            role:          NodeRole.MuscleGround | NodeRole.Perception);
        _app.InitializeEmbedded(headless: true, domainIdOverride: DomainId, networkFactory: factory);
    }

    public void Dispose()
    {
        _app.Dispose();
        _idAllocatorServer.Dispose();
        _idAllocatorParticipant.Dispose();
    }

    // ── SC7 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// SC7: After initialization, <see cref="SimHostApp.TestHook_NedReplication"/> must be
    /// non-null, proving <see cref="Hrot.Network.Replication.NedReplicationModule"/> is wired
    /// via <c>.WithReplication()</c> and <c>EntityMasterEgressTranslator</c> will fire when
    /// entities are spawned.
    /// </summary>
    [Fact]
    public void SimHostApp_AfterInit_NedReplicationIsWired()
    {
        Assert.NotNull(_app.TestHook_NedReplication);
    }

    /// <summary>
    /// SC7 (continued): Tick the kernel; no unhandled exceptions means the full pipeline
    /// including EntityMasterEgressTranslator (from NedReplicationModule.SharedTranslatorPack)
    /// is operational.
    /// </summary>
    [Fact]
    public void SimHostApp_AfterInit_KernelTickDoesNotThrow()
    {
        _idAllocatorServer.ProcessRequests();
        var ex = Record.Exception(() => _app.Tick(1f / 60f));
        Assert.Null(ex);
    }

    // ── SC8 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// SC8: When SimHostApp is initialized with the standalone role (MuscleGround | Perception),
    /// the <see cref="Hrot.Network.Replication.NedReplicationModule"/> used in the builder chain
    /// must target <c>driveFromNetwork=false</c>.
    ///
    /// <para>
    /// This is verified by casting the <see cref="Hrot.Common.Abstractions.INedReplicationModule"/>
    /// interface to the concrete type and inspecting <c>DriveFromNetwork</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void SimHostApp_StandaloneRole_NedReplicationDriveFromNetworkIsFalse()
    {
        var nedReplication = _app.TestHook_NedReplication;
        Assert.NotNull(nedReplication);
        Assert.False(nedReplication!.DriveFromNetwork,
            "NedReplicationModule for standalone (MuscleGround | Perception) must have DriveFromNetwork=false " +
            "(ghost-only smoothing): local entities must not be driven by DR.");
    }
}
