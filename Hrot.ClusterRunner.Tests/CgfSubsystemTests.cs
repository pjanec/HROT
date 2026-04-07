using System;
using System.Collections.Generic;
using System.Reflection;
using Hrot.ClusterRunner.Services;
using FDP.Framework.Runner;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Unit tests for <see cref="CgfSubsystem"/> brain-role pack composition (PACK2-R002).
/// </summary>
[Collection("CgfSubsystemTests")]
public class CgfSubsystemTests : IDisposable
{
    private readonly CgfSubsystem _sut = new();

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

        // NedReplicationModule (Brain) is registered.
        var nedField = typeof(CgfSubsystem)
            .GetField("_nedReplicationModule", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(nedField!.GetValue(_sut));
    }
}
