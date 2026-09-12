using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
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
    /// FC-1·G2 (Q#20 review G2) -- inserts <paramref name="bpTick"/> into
    /// <paramref name="simulationSystems"/> at the position its own <c>[UpdateBefore]</c>
    /// declarations require: immediately BEFORE the first system whose type is named by one of
    /// <see cref="BlueprintTickSystem"/>'s <c>[UpdateBefore]</c> attributes (the Locomotion/Weapon/
    /// Interaction dispatchers). Module-group execution order is ARRAY POSITION -- the kernel does
    /// not re-apply ordering attributes inside a module's system list (see
    /// <c>ChannelArbitrationSystem</c>'s comments and the Q#20 review's G2 finding: both real
    /// compositions used to APPEND the tick after the dispatchers, silently downgrading the
    /// architect-approved Q#16-B "intent is read the same tick" contract to write-visible-next-tick).
    /// This helper makes the declared contract hold BY CONSTRUCTION and is the single splice both
    /// hosts (<c>EditorSubsystem</c>, <c>EditorHarness</c>) use.
    /// <para>
    /// The targets are read off the attributes (not hardcoded) so a future <c>[UpdateBefore]</c>
    /// addition on <see cref="BlueprintTickSystem"/> re-positions the splice automatically. A list
    /// containing NO target system (degenerate/test compositions without dispatchers) appends the
    /// tick at the end -- exactly the old behavior, where no ordering contract exists to honor.
    /// </para>
    /// </summary>
    public static List<IEcsModuleSystem> SpliceIntoSimulation(
        IEnumerable<IEcsModuleSystem> simulationSystems, BlueprintTickSystem bpTick)
    {
        if (simulationSystems is null) throw new ArgumentNullException(nameof(simulationSystems));
        if (bpTick is null)            throw new ArgumentNullException(nameof(bpTick));

        var targets = typeof(BlueprintTickSystem)
            .GetCustomAttributes<UpdateBeforeAttribute>()
            .Select(a => a.Target)
            .ToArray();

        var result = new List<IEcsModuleSystem>(simulationSystems);
        int firstTarget = result.FindIndex(s => targets.Contains(s.GetType()));
        if (firstTarget >= 0)
            result.Insert(firstTarget, bpTick);
        else
            result.Add(bpTick);
        return result;
    }

    /// <summary>
    /// Registers the blackboard tier components on <paramref name="world"/>. Idempotent.
    ///
    /// <para>⚠⚠ <b>THE LIST MOVED <c>2026-09-03</c>.</b> This method used to hold the three
    /// <c>RegisterComponent</c> calls itself, and because its only production caller is the Editor,
    /// <b>every other host was missing them</b> — a <c>--mode all</c> cluster aborted on its first live
    /// load with <i>"Component BlueprintBlackboard1024 is not registered"</i> from
    /// <c>BehaviorIngressSystem</c> on CGF.</para>
    ///
    /// <para>⭐ The list now lives at
    /// <see cref="Fdp.Toolkit.Blueprints.Components.BlueprintBlackboardTiers"/> — in the same assembly as
    /// the components and the system that needs them — and is called for every node by
    /// <c>HrotSharedComponentRegistry.RegisterAll</c>. ⛔ This method is kept as a forwarder so the
    /// Editor's wiring path is unchanged; it is no longer a second list.
    /// 📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §2.3b.</para>
    /// </summary>
    public static void RegisterTierComponents(EntityRepository world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        Fdp.Toolkit.Blueprints.Components.BlueprintBlackboardTiers.RegisterAll(world);
    }
}
