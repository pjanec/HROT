using System;
using System.Linq;
using System.Reflection;
using Hrot.ReplayBrowser;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// FND-T11: Verifies that ReplayBrowserSubsystem is discoverable by ScanForSubsystems.
/// </summary>
public class ReplayBrowserSubsystemDiscoveryTests
{
    [Fact]
    public void FND_T11_ReplayBrowserSubsystem_ImplementsISubsystem()
    {
        // Ensure the type exists and implements ISubsystem.
        var t = typeof(ReplayBrowserSubsystem);
        Assert.True(typeof(ISubsystem).IsAssignableFrom(t));
        Assert.False(t.IsAbstract);
    }

    [Fact]
    public void FND_T11_ReplayBrowserSubsystem_HasINetworkFactoryCtor()
    {
        // ScanForSubsystems creates subsystems via ctor(INetworkFactory).
        var t = typeof(ReplayBrowserSubsystem);
        bool found = t.GetConstructors().Any(c =>
        {
            var parms = c.GetParameters();
            return parms.Length == 1 && parms[0].ParameterType.Name == "INetworkFactory";
        });
        Assert.True(found);
    }

    [Fact]
    public void FND_T11_ReplayBrowserSubsystem_CLIName_IsReplayBrowser()
    {
        // ScanForSubsystems strips "Subsystem" suffix to derive the CLI name.
        string typeName = typeof(ReplayBrowserSubsystem).Name;
        const string suffix = "Subsystem";
        string cliName = typeName.EndsWith(suffix)
            ? typeName[..^suffix.Length]
            : typeName;

        Assert.Equal("ReplayBrowser", cliName);
    }

    [Fact]
    public void FND_T11_ReplayBrowserSubsystem_Assembly_IsLoadedInAppDomain()
    {
        // When Hrot.ReplayBrowser.csproj is referenced by Hrot.ClusterRunner, its
        // assembly is loaded into the AppDomain and ScanForSubsystems can find it.
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        bool found = assemblies.Any(a => a.GetName().Name == "Hrot.ReplayBrowser");
        Assert.True(found, "Hrot.ReplayBrowser assembly must be loaded in the AppDomain.");
    }
}
