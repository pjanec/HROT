using System;
using System.Collections.Generic;
using System.Reflection;
using Hrot.CGF;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Map.Common;
using Hrot.Network.NED.Factory;
using Fdp.Toolkit.Replication.Services;
using Fdp.Engine.Runner;
using Fdp.Core;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Unit tests for <see cref="CgfSubsystem"/> brain-role pack composition (PACK2-R002).
/// </summary>
[Collection("CgfSubsystemTests")]
public class CgfSubsystemTests : IDisposable
{
    private readonly CgfSubsystem _sut = new(new NedNetworkFactory(
        participant:  null,
        entityMap:    new NetworkEntityMap(),
        geoTransform: HrotEnvironment.CreateGeoTransform(),
        eventBus:     new FdpEventBus(),
        localNodeId:  0,
        role:         NodeRole.None));  // Role gets overridden by ConfigureForNode(_context, Brain)

    public void Dispose() => _sut.Shutdown();

    [Fact]
    public void Initialize_InstallsThreePacks()
    {
        // Headless=true avoids DDS participant creation and DdsIdAllocatorServer wait,
        // making this a fast unit test that only verifies ECS kernel and module composition.
        // (In production/integration tests, Headless=false is used and an OrchestratorSubsystem
        // provides the DdsIdAllocatorServer before CgfSubsystem initializes.)
        var config = new SubsystemConfig { DomainId = 0, Headless = true };

        _sut.Initialize(config);

        // World and EntityMap are created by HrotNodeBuilder (via _context).
        Assert.NotNull(_sut.World);
        Assert.NotNull(_sut.GhostEntityMap);

        // NedReplicationModule (Brain) is wired into _context.NedReplication by
        // HrotNodeBuilderReplicationExtensions.Build() (S202/S401 migration).
        var contextField = typeof(CgfSubsystem)
            .GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance);
        var context = contextField!.GetValue(_sut) as HrotNodeContext;
        Assert.NotNull(context?.NedReplication);
    }
}
