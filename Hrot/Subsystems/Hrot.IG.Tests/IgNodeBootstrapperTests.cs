using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.ModuleHost.Abstractions;
using Hrot.Common.Infrastructure;
using Hrot.IG.Systems;
using Hrot.IG.Modules;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="IgNodeBootstrapper"/> (SC_SM010_2).
///
/// Validates that <see cref="IgNodeBootstrapper.GetAdditionalModules"/> returns the
/// correct set of presentation modules depending on the headless flag.
/// </summary>
public class IgNodeBootstrapperTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IgNodeBootstrapper CreateBootstrapper(bool headless)
        => new IgNodeBootstrapper(
            networkFactory:        null,
            effectiveInstanceId:   300,
            headless:              headless,
            igTranslatorsProvider: null,
            userConfig:            new MapUserConfig(),
            cameraViewport:        new MapCameraViewport(),
            eventHistoryService:   null,
            hrotConfig:            new HrotNodeConfig { NodeId = 300, SubsystemName = "IG" });

    /// <summary>
    /// Invokes the protected <c>GetAdditionalModules()</c> via reflection so tests
    /// can validate the returned set without changing the access modifier.
    /// </summary>
    private static IReadOnlyList<IEcsModule> InvokeGetAdditionalModules(IgNodeBootstrapper bootstrapper)
    {
        var method = typeof(IgNodeBootstrapper)
            .GetMethod("GetAdditionalModules", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("GetAdditionalModules not found via reflection.");

        var result = method.Invoke(bootstrapper, null)
            ?? throw new InvalidOperationException("GetAdditionalModules returned null.");

        return ((IEnumerable<IEcsModule>)result).ToList();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GetAdditionalModules_Headless_ContainsStyleResolutionModule()
    {
        var sut     = CreateBootstrapper(headless: true);
        var modules = InvokeGetAdditionalModules(sut);

        Assert.Contains(modules, m => m is StyleResolutionModule);
    }

    [Fact]
    public void GetAdditionalModules_Headless_ContainsMapCullingModule()
    {
        var sut     = CreateBootstrapper(headless: true);
        var modules = InvokeGetAdditionalModules(sut);

        Assert.Contains(modules, m => m is MapCullingModule);
    }

    [Fact]
    public void GetAdditionalModules_Headless_ContainsMapLayerModule()
    {
        var sut     = CreateBootstrapper(headless: true);
        var modules = InvokeGetAdditionalModules(sut);

        Assert.Contains(modules, m => m is MapLayerModule);
    }

    [Fact]
    public void GetAdditionalModules_Headless_ContainsHistoryTrailModule()
    {
        var sut     = CreateBootstrapper(headless: true);
        var modules = InvokeGetAdditionalModules(sut);

        Assert.Contains(modules, m => m is HistoryTrailModule);
    }

    [Fact]
    public void GetAdditionalModules_Headless_DoesNotContainEventEffectModule()
    {
        var sut     = CreateBootstrapper(headless: true);
        var modules = InvokeGetAdditionalModules(sut);

        Assert.DoesNotContain(modules, m => m is EventEffectModule);
    }

    [Fact]
    public void GetAdditionalModules_NonHeadless_ContainsEventEffectModule()
    {
        var sut     = CreateBootstrapper(headless: false);
        var modules = InvokeGetAdditionalModules(sut);

        Assert.Contains(modules, m => m is EventEffectModule);
    }
}
