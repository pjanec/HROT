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
    /// ⭐ CE-4013 — the modules the BASE registers at <c>additional-modules</c> for this host: IG's resolved capabilities (protected
    /// <c>ResolveCapabilities</c>, via reflection) through the same <see cref="NodeCapabilityWalk"/> the base runs, after its systems
    /// phase (the null-context shape probe every host rail uses).
    /// </summary>
    private static IReadOnlyList<IEcsModule> InvokeGetAdditionalModules(IgNodeBootstrapper bootstrapper)
    {
        var walk = Walk(bootstrapper, out _, out _, out _);
        return walk.ProvideModules();
    }

    /// <summary>⭐ CE-4013 — IG's capabilities walked through their systems phase, exactly as the base does at <c>system-groups</c>.</summary>
    private static NodeCapabilityWalk Walk(IgNodeBootstrapper bootstrapper,
        out List<IEcsModuleSystem> input, out List<IEcsModuleSystem> sim, out List<IEcsModuleSystem> post)
    {
        var resolve = typeof(IgNodeBootstrapper)
            .GetMethod("ResolveCapabilities", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ResolveCapabilities not found via reflection.");
        var capabilities = (IReadOnlyList<INodeCapability>)resolve.Invoke(bootstrapper, new object?[] { null, Fdp.Core.NodeRole.Map2D })!;
        var walk = new NodeCapabilityWalk(capabilities);
        input = new List<IEcsModuleSystem>(); sim = new List<IEcsModuleSystem>(); post = new List<IEcsModuleSystem>();
        walk.PopulateSystems(null!, input, sim, post);
        return walk;
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

    /// <summary>⭐ CE-1042 E2 — the effect module is RETIRED on IG: the map pack's effect systems replace it on every map host
    /// (docs/DESIGN_Visual_Effects.md VE-A), so a non-headless IG must not ALSO run it — both would make every shot twice.</summary>
    [Fact]
    public void GetAdditionalModules_NonHeadless_NoLongerContainsEventEffectModule()
    {
        var sut     = CreateBootstrapper(headless: false);
        var modules = InvokeGetAdditionalModules(sut);

        Assert.DoesNotContain(modules, m => m is EventEffectModule);
    }

    // ── Order ─────────────────────────────────────────────────────────────────
    //
    // ⛔⛔ THE SIX TESTS ABOVE ASSERT MEMBERSHIP, NOT SEQUENCE — every one of them stays green under
    //    an arbitrary permutation. That is a real gap, because ModuleHostKernel.RegisterModule appends
    //    to a plain List the frame loop walks in order (ModuleHostKernel.cs:437), so the order these
    //    modules come back in IS this node's frame execution order. A refactor that reorders them is a
    //    behaviour change, and nothing here could see it.
    //
    // ⇒ these two rails pin the sequence, so the B4b switchover onto the capability seam is provably
    //   behaviour-preserving rather than assumed to be.
    // 📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1t.

    /// <summary>The exact presentation sequence this node has always registered, headless.</summary>
    private static readonly Type[] HeadlessOrder =
    {
        typeof(StyleResolutionModule),
        typeof(MapCullingModule),
        typeof(MapLayerModule),
        typeof(HistoryTrailModule),
    };

    [Fact]
    public void GetAdditionalModules_Headless_PreservesTheRegistrationSEQUENCE()
    {
        var sut     = CreateBootstrapper(headless: true);
        var modules = InvokeGetAdditionalModules(sut);

        Assert.Equal(HeadlessOrder, modules.Select(m => m.GetType()).ToArray());
    }

    [Fact]
    public void GetAdditionalModules_NonHeadless_IsTheSameSequence_TheEffectModuleRetired()
    {
        var sut     = CreateBootstrapper(headless: false);
        var modules = InvokeGetAdditionalModules(sut);

        Assert.Equal(HeadlessOrder, modules.Select(m => m.GetType()).ToArray());
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2150</c> (R-2010; 🔒 user, 2026-10-13: "IG is no different") — IG's system hook walks its capabilities, so the cross-role
    /// infrastructure it declares actually ticks there: damage application FIRST (the shared plan head, R-2008), then the unit hierarchy
    /// and the interaction appliers; the passenger derivation in the input group. ✅ Red-proof: an empty hook (as before) ⇒ red.
    /// </summary>
    [Fact]
    public void PopulateSystems_WalksTheCrossRoleCapabilities_HealthFirst_CE2150()
    {
        var sut = CreateBootstrapper(headless: true);
        Walk(sut, out var input, out var sim, out var post);   // ⭐ CE-4013 — the base's walk, not a host loop

        Assert.IsType<Fdp.Toolkit.Combat.Systems.HealthApplicationSystem>(sim[0]);
        Assert.Contains(sim, s => s is Hrot.Common.Systems.UnitHierarchySystem);
        Assert.Contains(sim, s => s is Fdp.Toolkit.Terrain.DoorCommandSystem);
        Assert.Contains(sim, s => s is Fdp.Toolkit.Behavior.Systems.EmbarkationSystem);
        Assert.Contains(input, s => s is Fdp.Toolkit.Behavior.Systems.EmbarkedStateSystem);
        // ⭐ CE-2301 ① (R-2058) — no brain here, so the report inbox comes as a capability: exactly one, logging every report IG sees.
        Assert.Single(sim, s => s is Fdp.Toolkit.Squad.Command.StaffInboxSystem);
    }
}
