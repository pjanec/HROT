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
        // Use a unique domain to avoid participant conflicts with other tests.
        var config = new SubsystemConfig { DomainId = 199 };

        _sut.Initialize(config);

        // Access the internal CgfApplication via reflection.
        var appField = typeof(CgfSubsystem)
            .GetField("_app", BindingFlags.NonPublic | BindingFlags.Instance);
        var app = appField!.GetValue(_sut);

        var namesProp = app!.GetType().GetProperty("InstalledModuleNames");
        var names = (IReadOnlyList<string>)namesProp!.GetValue(app)!;

        Assert.Contains("CgfLogicPack",         names);
        Assert.Contains("GhostCleanup",          names);
        Assert.Contains("EntityStatesIngress",   names);
        Assert.Contains("ActuatorIntentsEgress", names);
        Assert.Equal(4, names.Count);
    }
}
