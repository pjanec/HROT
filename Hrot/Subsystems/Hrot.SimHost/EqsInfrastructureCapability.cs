#nullable enable
using System;
using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Hrot.Common.Infrastructure;
using Hrot.SimHost.Systems;

namespace Hrot.SimHost;

/// <summary>
/// EQS result ingestion as cross-role infrastructure — <c>CE-221</c>, the sibling of
/// <see cref="CoreInfrastructureCapabilities.UnitHierarchy"/>.
/// </summary>
/// <remarks>
/// <para><c>EqsResultUpdateSystem</c> was a member of <c>CgfLogicPack</c>, <c>SimHostCoreLogicPack</c>
/// and <c>StrideMuscleModuleSet</c> alike — the same mis-assignment, and the second of the two types
/// that made the Stride editor's <c>[SingleInstance]</c> boot failure fatal. See
/// <see cref="CoreInfrastructureCapabilities"/> for the full reasoning; it is not repeated here.</para>
///
/// <para>⚠ <b>Why it lives in <c>Hrot.SimHost</c> and not beside its sibling in <c>Hrot.Common</c>.</b>
/// 📐 Measured: <c>EqsResultUpdateSystem</c> is declared in <c>Hrot.SimHost.Systems</c>, and
/// <c>Hrot.IG</c> references only <c>Hrot.Common</c>. Homing this capability in <c>Hrot.Common</c>
/// would need either a new IG → SimHost project reference or a symbol move. IG never carried this
/// system, so the split costs nothing today and keeps every host's system set byte-for-byte what it
/// was — which is what makes <c>CE-221</c>'s fix purely structural and cheap to gate.</para>
///
/// <para>⭐ The end-state worth doing later, separately: move <c>EqsResultUpdateSystem</c> into
/// <c>Hrot.Common.Systems</c> beside <c>UnitHierarchySystem</c> and merge the two capabilities. That
/// is a Roslyn symbol move (⛔ never a text replace) and is deliberately not bundled here.</para>
/// </remarks>
public sealed class EqsResultUpdateCapability : INodeCapability
{
    private readonly EqsResultUpdateSystem _system = new();
    // ⭐ CE-3072 B3/B4 — the danger-area sensor's Brain half (apply answers, rate, edges) rides the same capability: every
    //   host that takes in sensor answers takes in this family's too. 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7.
    private readonly Fdp.Toolkit.Squad.Systems.DangerAreaSensorSystem _dangerAreas = new();

    public string Key => CapabilityKeys.EqsResultUpdate;

    public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

    public void PopulateSystems(
        HrotNodeContext context,
        List<IEcsModuleSystem> input,
        List<IEcsModuleSystem> simulation,
        List<IEcsModuleSystem> postSimulation)
    {
        if (simulation is null) throw new ArgumentNullException(nameof(simulation));
        simulation.Add(_system);
        simulation.Add(_dangerAreas);
    }
}

/// <summary>
/// ⭐⭐ <b>The EQS solver's startup, written ONCE</b> — every host that solves EQS (SimHost, Stride, the
/// editor's in-process muscle) calls this from its perception capability. 📄
/// <c>docs/designs/eqs-2/EQS_Design_v1.3_final.md</c> §17.8.
/// </summary>
/// <remarks>
/// 🔒 User, <c>2026-09-30</c>: <i>"unification and sharing desired"</i>. ⛔ It was three hand-written copies
/// (SimHost <c>PerceptionSolver</c>, Stride <c>PerceptionSolver</c>, editor <c>PerceptionEqsSolver</c>), and
/// before <c>CE-465</c> none of them installed a template registry. The capability CLASSES stay per host
/// (their keys and plan positions are pinned by rails); what they DO is this one call.
/// </remarks>
public static class EqsSolverStartup
{
    /// <summary>
    /// ⭐ <c>CE-3061</c> — the main-loop systems that FEED the perception sensors (heat now, sound with <c>CE-3062</c>). They
    /// run where the entities move and fire; the solver reads them on its background snapshot. ⭐ ONE helper, called by every
    /// host's perception capability (SimHost, editor, Stride), so no host composes the solver without its stimuli.
    /// docs/DESIGN_Thermal_And_Acoustic_Sensing.md §3.
    /// </summary>
    public static void PopulateSystems(List<IEcsModuleSystem> simulation)
    {
        if (simulation is null) throw new ArgumentNullException(nameof(simulation));
        simulation.Add(new Fdp.Toolkit.Perception.Signatures.ThermalHeatSystem());
        simulation.Add(new Fdp.Toolkit.Perception.Signatures.SoundEmissionSystem());   // CE-3062
    }

    /// <summary>Installs the template registry on the node's world, then registers the solver module (returned, so a
    /// host that hot-swaps its logic tier can uninstall it).</summary>
    public static Modules.EqsModule Register(HrotNodeContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        // CE-465: without a registry every sensor gets the empty stub. Installed BEFORE the module so
        // its first tick sees it.
        Fdp.Toolkit.Spatial.Eqs.EqsTemplateRegistry.InstallDefault(context.World);
        // ⭐ CE-3061 — the schema the thermal template reads, owed by whoever composes the solver (Stride's mode 2 registers
        //   only a muscle subset, so it would otherwise be missing there). Idempotent.
        if (!context.World.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Signatures.ThermalState>())
            context.World.RegisterComponent<Fdp.Toolkit.Perception.Signatures.ThermalState>();
        if (!context.World.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>())
            context.World.RegisterComponent<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>();
        if (!context.World.Bus.IsRegistered<Fdp.Toolkit.Perception.Events.SoundContactEvent>())
            context.World.RegisterEvent<Fdp.Toolkit.Perception.Events.SoundContactEvent>();
        // ⭐ CE-3038 — the module carries vision too (its grid, the visual template, 3-D sight): design §5.5.
        var module = Modules.EqsModule.ForTerrainHost(context.World);
        context.Kernel.RegisterModule(module);
        return module;
    }
}
