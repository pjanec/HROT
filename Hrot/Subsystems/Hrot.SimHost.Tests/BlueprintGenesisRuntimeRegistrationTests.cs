using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Systems;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// BSA-WIRE: Unit tests for <see cref="BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems"/>.
///
/// Verifies that the seam registers EXACTLY <see cref="BlueprintMaterializationSystem"/>
/// and <see cref="BlueprintEventIngressSystem"/> into the kernel, with the supplied
/// registry instance, and that the two registrations are independent (different
/// <see cref="BlueprintRegistry"/> instances are not cross-wired).
/// </summary>
public sealed class BlueprintGenesisRuntimeRegistrationTests : IDisposable
{
    private readonly EntityRepository _world;
    private readonly ModuleHostKernel  _kernel;

    public BlueprintGenesisRuntimeRegistrationTests()
    {
        _world  = new EntityRepository();
        _world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
        _kernel = new ModuleHostKernel(_world, new EventAccumulator());
    }

    public void Dispose()
    {
        _kernel.Dispose();
        _world.Dispose();
    }

    // ── Reflection helper ─────────────────────────────────────────────────────

    /// <summary>
    /// Reads the private <c>_registeredGlobalSystems</c> list from the kernel via
    /// reflection so we can assert on what was registered without calling Initialize().
    /// </summary>
    private static IReadOnlyList<IEcsModuleSystem> GetRegisteredGlobalSystems(
        ModuleHostKernel kernel)
    {
        var field = typeof(ModuleHostKernel).GetField(
            "_registeredGlobalSystems",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(field); // Guard: field must exist; update if kernel internals change.

        var list = field!.GetValue(kernel) as IReadOnlyList<IEcsModuleSystem>;
        Assert.NotNull(list);
        return list!;
    }

    // ── TC1: seam registers both required system types ────────────────────────

    /// <summary>
    /// BSA-WIRE TC1 (primary): calling <see cref="BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems"/>
    /// must register exactly one <see cref="BlueprintMaterializationSystem"/> and one
    /// <see cref="BlueprintEventIngressSystem"/> into the kernel's global-systems list.
    /// </summary>
    [Fact]
    public void RegisterBlueprintGenesisSystems_RegistersBothRequiredTypes()
    {
        var registry = new BlueprintRegistry();

        BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems(_kernel, registry);

        var systems = GetRegisteredGlobalSystems(_kernel);

        // Both types must be present.
        Assert.Contains(systems, s => s is BlueprintMaterializationSystem);
        Assert.Contains(systems, s => s is BlueprintEventIngressSystem);
    }

    // ── TC2: seam registers exactly those two systems (count guard) ───────────

    /// <summary>
    /// BSA-WIRE TC2: the seam adds exactly 2 systems to a previously-empty kernel.
    /// This ensures no stray extras are registered and both are present (count = 2).
    /// </summary>
    [Fact]
    public void RegisterBlueprintGenesisSystems_AddsExactlyTwoSystems()
    {
        var registry = new BlueprintRegistry();

        var before = GetRegisteredGlobalSystems(_kernel).Count;

        BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems(_kernel, registry);

        var after = GetRegisteredGlobalSystems(_kernel).Count;

        Assert.Equal(2, after - before);
    }

    // ── TC3: registry instance is forwarded correctly ─────────────────────────

    /// <summary>
    /// BSA-WIRE TC3: the <see cref="BlueprintRegistry"/> instance passed to the seam
    /// must be the SAME instance stored inside both registered systems (not a copy or
    /// a different object).  Verified via reflection on the private <c>_registry</c>
    /// field that both systems declare.
    /// </summary>
    [Fact]
    public void RegisterBlueprintGenesisSystems_ForwardsRegistryInstanceToEachSystem()
    {
        var registry = new BlueprintRegistry();

        BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems(_kernel, registry);

        var systems = GetRegisteredGlobalSystems(_kernel);

        var matSys = Assert.Single(systems.OfType<BlueprintMaterializationSystem>());
        var evtSys = Assert.Single(systems.OfType<BlueprintEventIngressSystem>());

        var registryField = typeof(BlueprintMaterializationSystem).GetField(
            "_registry", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(registryField);
        var matRegistry = registryField!.GetValue(matSys);
        Assert.Same(registry, matRegistry);

        var evtRegistryField = typeof(BlueprintEventIngressSystem).GetField(
            "_registry", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(evtRegistryField);
        var evtRegistry = evtRegistryField!.GetValue(evtSys);
        Assert.Same(registry, evtRegistry);
    }

    // ── TC4: null registry throws (ArgumentNullException guard in ctors) ───────

    /// <summary>
    /// BSA-WIRE TC4: passing a null registry must throw
    /// <see cref="ArgumentNullException"/> (propagated from the system constructors)
    /// rather than silently accepting the null and failing at runtime.
    /// </summary>
    [Fact]
    public void RegisterBlueprintGenesisSystems_NullRegistry_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems(
                _kernel, null!));
    }
}
