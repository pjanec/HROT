using System;
using System.Reflection;
using Fdp.Network.Cyclone.Services;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.NED.Descriptors;
using Hrot.Orchestrator;
using Hrot.SimHost;
using Xunit;

using CoreGeoPoint = Hrot.Core.Mission.GeoPoint;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Verifies that the DDS ID Allocator client is fully decoupled from
/// <see cref="HrotNodeBuilder"/> and <see cref="OrchestratorSubsystem"/>:
/// offline/mock factories supply a local <see cref="SequentialIdAllocator"/>
/// (no DDS publication-match wait), the orchestrator disposes its server handle
/// on shutdown, and neither class retains a hard field reference to any DDS
/// allocator type or background <see cref="System.Threading.Thread"/>.
/// </summary>
public sealed class DdsIdAllocatorDecouplingTests
{
    private static readonly CoreGeoPoint BerlinGeo =
        new CoreGeoPoint { Latitude = 52.521, Longitude = 13.406, Altitude = 0 };

    // ── Test 1: pure offline allocation ──────────────────────────────────────

    /// <summary>
    /// When a <see cref="MockNetworkFactory"/> is injected into
    /// <see cref="SimHostSubsystem"/>, <see cref="SimHostApp"/> must use the
    /// factory-provided <see cref="SequentialIdAllocator"/> rather than creating a
    /// <c>DdsIdAllocator</c> directly.  Network-ID allocation must succeed
    /// immediately (no publication-match wait) and return a positive value.
    /// </summary>
    [Fact]
    public void MockFactory_SpawnEntity_UsesSequentialIdAllocator()
    {
        var factory = new MockNetworkFactory();
        var simHostSvc = new SimHostSubsystem(factory);

        var options = new RunnerOptions { Headless = true, DomainId = 0 };
        var orchestrator = new SubsystemOrchestrator(new ISubsystem[] { simHostSvc }, options);
        orchestrator.Initialize();
        try
        {
            // TestHook_SpawnEntity internally calls _idAllocator.AllocateId().
            // With a MockNetworkFactory the allocator is SequentialIdAllocator, so this
            // returns immediately without any DDS handshake.
            long id = simHostSvc.TestHook_SpawnEntity(TkbEntityTypes.Tank_M1Abrams, BerlinGeo);

            Assert.True(id > 0,
                $"Expected a positive network ID from SequentialIdAllocator; got {id}.");
        }
        finally
        {
            orchestrator.Shutdown();
        }
    }

    // ── Test 2: deterministic teardown spy ───────────────────────────────────

    /// <summary>
    /// <see cref="OrchestratorSubsystem.Shutdown"/> must call
    /// <see cref="IDisposable.Dispose"/> on the handle returned by
    /// <see cref="INetworkFactory.CreateIdAllocatorServer"/> exactly once.
    /// </summary>
    [Fact]
    public void OrchestratorSubsystem_Shutdown_DisposesIdAllocatorServerHandle()
    {
        int disposeCount = 0;
        var spy = new SpyDisposable(() => disposeCount++);
        var factory = new SpyNetworkFactory(spy);

        var orchSvc = new OrchestratorSubsystem(factory);
        orchSvc.Initialize(new SubsystemConfig { NodeId = 0 });
        orchSvc.Shutdown();

        Assert.Equal(1, disposeCount);
    }

    // ── Test 3: static reflection boundary ───────────────────────────────────

    /// <summary>
    /// Neither <see cref="OrchestratorSubsystem"/> nor <see cref="HrotNodeBuilder"/>
    /// may contain a field whose declared type is <c>DdsIdAllocatorServer</c>,
    /// <c>DdsIdAllocator</c>, or <see cref="System.Threading.Thread"/>.
    /// This prevents concrete DDS types from leaking into infrastructure hosts.
    /// </summary>
    [Fact]
    public void OrchestratorAndBuilder_HaveNoDdsAllocatorFields()
    {
        var flags = BindingFlags.Instance | BindingFlags.Static
                  | BindingFlags.Public  | BindingFlags.NonPublic;

        var allFields = typeof(OrchestratorSubsystem).GetFields(flags)
            .Concat(typeof(HrotNodeBuilder).GetFields(flags));

        var forbidden = new[]
        {
            typeof(DdsIdAllocatorServer),
            typeof(DdsIdAllocator),
            typeof(System.Threading.Thread),
        };

        foreach (var field in allFields)
        {
            foreach (var t in forbidden)
            {
                Assert.False(field.FieldType == t,
                    $"Field '{field.DeclaringType!.Name}.{field.Name}' has forbidden type '{t.Name}'.");
            }
        }
    }

    // ── Spy helpers ───────────────────────────────────────────────────────────

    private sealed class SpyDisposable : IDisposable
    {
        private readonly Action _onDispose;
        public SpyDisposable(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }

    /// <summary>
    /// Extends <see cref="MockNetworkFactory"/> so that
    /// <see cref="CreateIdAllocatorServer"/> returns the supplied spy disposable.
    /// All other methods delegate to the base implementation.
    /// </summary>
    private sealed class SpyNetworkFactory : MockNetworkFactory
    {
        private readonly IDisposable _spy;
        public SpyNetworkFactory(IDisposable spy) => _spy = spy;
        public override IDisposable CreateIdAllocatorServer() => _spy;
    }
}
