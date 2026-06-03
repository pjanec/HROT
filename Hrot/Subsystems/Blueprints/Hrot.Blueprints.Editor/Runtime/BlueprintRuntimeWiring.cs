using System;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Systems;

namespace Hrot.Blueprints.Editor.Runtime;

/// <summary>
/// Single source of truth for wiring the Instance-Blueprint runtime into a real
/// <see cref="ModuleHostKernel"/> composition. Both the production editor
/// (<c>EditorSubsystem</c>) and the headless integration harness
/// (<c>Hrot.ClusterRunner.Integration.Tests/EditorHarness</c>) call this so the blueprint
/// systems, blackboard tier components, and the shared registry are wired identically — no
/// separate or sandbox world (per the MVE architectural rule).
/// </summary>
public static class BlueprintRuntimeWiring
{
    /// <summary>
    /// Performs the two composition-root steps that can be done uniformly across hosts and
    /// returns the <see cref="BlueprintTickSystem"/> for the caller to splice into its own
    /// Simulation-phase group (each host wraps its sim systems differently, so the tick
    /// system cannot be registered here).
    /// </summary>
    /// <remarks>
    /// <para>Steps performed here:</para>
    /// <list type="number">
    ///   <item>
    ///     Register the three blackboard tier components
    ///     (<see cref="BlueprintBlackboard1024"/>/<see cref="BlueprintBlackboard4096"/>/<see cref="BlueprintBlackboard16384"/>)
    ///     on <paramref name="world"/>. Component tables reserve virtual address space lazily
    ///     (per 64 KB chunk) and cost no physical RAM until populated, so registering the 16 KB
    ///     tier is cheap. MUST happen before kernel initialization so the tick/maintenance
    ///     queries can be built.
    ///   </item>
    ///   <item>
    ///     Register <see cref="BlueprintMaintenanceSystem"/> as a global system (it is
    ///     <c>[UpdateInPhase(BeforeSync)]</c>, which the kernel permits as a global system).
    ///   </item>
    /// </list>
    /// <para>
    /// The returned <see cref="BlueprintTickSystem"/> is <c>[UpdateInPhase(Simulation)]</c>,
    /// which the kernel forbids as a global system; the caller must place it inside a module's
    /// Simulation-phase system list (e.g. the editor's <c>TogglableSimulationGroup</c> array or
    /// the harness's simulation-systems list).
    /// </para>
    /// </remarks>
    /// <param name="kernel">The kernel to register the BeforeSync maintenance system into (pre-Initialize).</param>
    /// <param name="world">The live world whose component registry the tier components are added to (pre-Initialize).</param>
    /// <param name="registry">
    /// The shared <see cref="BlueprintRegistry"/> the host compiles blueprints into; the tick
    /// system ticks against this same instance so editor-registered blueprints run live.
    /// </param>
    /// <returns>The Simulation-phase tick system for the caller to schedule.</returns>
    public static BlueprintTickSystem WireBlueprintRuntime(
        ModuleHostKernel kernel,
        EntityRepository world,
        BlueprintRegistry registry)
    {
        if (kernel is null)   throw new ArgumentNullException(nameof(kernel));
        if (world is null)    throw new ArgumentNullException(nameof(world));
        if (registry is null) throw new ArgumentNullException(nameof(registry));

        RegisterTierComponents(world);

        var maintenance = new BlueprintMaintenanceSystem();
        kernel.RegisterGlobalSystem(maintenance);

        return new BlueprintTickSystem(registry);
    }

    /// <summary>
    /// Registers the three blackboard tier components on <paramref name="world"/> if they are
    /// not already present. Idempotent-safe to expose separately for hosts that register
    /// components in a dedicated block.
    /// </summary>
    public static void RegisterTierComponents(EntityRepository world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        world.RegisterComponent<BlueprintBlackboard1024>();
        world.RegisterComponent<BlueprintBlackboard4096>();
        world.RegisterComponent<BlueprintBlackboard16384>();
    }
}
