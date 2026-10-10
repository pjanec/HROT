#nullable enable
using System;
using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Hrot.CGF;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.SimHost;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Systems;
using Fdp.Core;

namespace Hrot.Editor;

/// <summary>
/// The editor's role-selected capabilities — host (d) on the capability axis.
/// </summary>
/// <remarks>
/// <para><b>Why the editor is on this seam at all.</b> It is the last ECS composition root that
/// hand-assembles its unit list. SimHost, IG and CGF all resolve a <see cref="NodeCompositionPlan"/>;
/// the editor did not, and what stood in for it was <c>EditorSubsystem.MuscleModuleFactory</c> — a
/// private, single-slot substitute that can swap the muscle tier and nothing else, cannot express a
/// shared resource, and has exactly one production caller (Stride's mode 1). One declaration replaces
/// it. See <c>DESIGN_Subsystem_Composition_Unification.md</c> §4.1ac.</para>
///
/// <para><b>⛔ REGISTRATION ORDER IS EXECUTION ORDER</b> — <c>ModuleHostKernel.RegisterModule</c>
/// appends to a plain list the frame loop walks in sequence. Every ordering choice below reproduces
/// the hand-written block exactly; none of them is a preference.</para>
///
/// <para><b>⚠ The editor is NOT SimHost with extra windows, and two differences matter here.</b>
/// First, it fuses <b>both</b> the Brain and the MuscleGround packs into one world, so their system
/// lists must be de-duplicated by type when concatenated — two of the same system is not a waste but
/// a corruption (<c>CE-165</c>: a second <c>UnitHierarchySystem</c> re-reads the same non-destructive
/// events and appends a duplicate roster entry). Second, the default arm <b>does not register its
/// muscle pack as a module at all</b> — only its system lists are spliced into the togglable groups.
/// That asymmetry is why <see cref="MuscleGround"/> implements <c>PopulateSystems</c> and not
/// <c>Register</c>.</para>
/// </remarks>
public static class EditorCapabilities
{
    /// <summary>
    /// The role the editor declares.
    ///
    /// <para>🔒 <b>User ruling, 2026-09-07:</b> <i>"editor's subsystem role could be 'everything' what
    /// Cgf+SimHost (probably not so much the IG) do now, for the purpose of editing scenarios and AI
    /// behaviors and testing them quickly."</i></para>
    ///
    /// <para>So: CGF ∪ SimHost, and deliberately <b>not</b> <c>ImageGenerator</c>. The purpose clause is
    /// the load-bearing half — the editor exists to author scenarios and behaviours and test them fast
    /// in one process, which is precisely "be both nodes at once". A role this wide weakens
    /// role-selection as a <i>narrowing</i> device, and that is fine here: what the editor takes from
    /// the seam is one shared declaration and shared units, not narrowing.</para>
    ///
    /// <para>⚠ <c>NavigationSolver</c> is declared because the editor's muscle pack contains the
    /// navigation systems, and <c>§4.1v</c> is the reason to say so rather than leave it off: SimHost
    /// declared <c>MuscleGround|Perception</c> while composing all three, and narrowing to that
    /// declaration would have silently dropped two modules. The declaration is made true, not
    /// convenient.</para>
    /// </summary>
    public const NodeRole DefaultRole =
        NodeRole.Brain | NodeRole.MuscleGround | NodeRole.Perception | NodeRole.NavigationSolver;

    /// <summary>
    /// Builds the plan for the editor's <b>default</b> composition — the arm that runs when no host
    /// has supplied a replacement muscle tier.
    /// </summary>
    /// <remarks>
    /// <b>Plan order is deliberate and mirrors the hand-written block:</b> Brain first, then
    /// MuscleGround — because the fused system lists are built as
    /// <c>DistinctByType(cgfLogicPack…, muscle…)</c>, so a CGF system wins the slot when both packs
    /// carry the same type. Reversing these two would silently change which instance runs.
    /// </remarks>
        // ⭐⭐ CE-221 — cross-role infrastructure, declared LAST on BOTH arms.
        //    Declared last => tail of the Simulation phase, which is where every carrier already put
        //    these two. ⚠ On the editor that IS a behaviour change: DistinctByType(cgf…, muscle…) kept
        //    CGF's copy, which sat ahead of the whole muscle tier, so UnitHierarchySystem consumed
        //    CmdAssignSubordinate (published by VehicleCommandSystem in GroundKinematicsModule) one
        //    frame late. SimHost always consumed it the same frame. This makes the editor agree.
        //    ⭐ On the INJECTED arm the muscle tier also declares these (Stride resolves them into
        //    this plan); Resolve de-duplicates by Key in declaration order, so the Brain-side
        //    instance below wins — the same first-wins rule DistinctByType already encoded.
    /// <param name="navigationSolver">⭐ <c>CE-3017</c> — SimHost's own <see cref="SimHostCapabilities.NavigationSolver"/>
    /// (never a copy, <c>R-174</c>), built over the muscle pack's trajectory pool and the editor's navmesh. ⛔ Required:
    /// the role DECLARES <c>NavigationSolver</c>, and a plan without it left every editor path request unanswered.</param>
    public static NodeCompositionPlan BuildDefault(
        CgfLogicPack cgfPack,
        SimHostCoreLogicPack musclePack,
        SimHostCapabilities.NavigationSolver navigationSolver)
    {
        if (cgfPack is null)          throw new ArgumentNullException(nameof(cgfPack));
        if (musclePack is null)       throw new ArgumentNullException(nameof(musclePack));
        if (navigationSolver is null) throw new ArgumentNullException(nameof(navigationSolver));

        // ⭐ CE-3017 — the solver is APPENDED after the EQS solver: it contributes modules only (no PopulateSystems), so
        //    every system sequence the order rails pin is unchanged, and every module registered before it keeps its slot.
        return new NodeCompositionPlan()
            .Capability(NodeRole.Brain,        new Brain(cgfPack))
            .Capability(NodeRole.MuscleGround, new MuscleGround(musclePack))
            .Capability(NodeRole.Perception,   new PerceptionEqsSolver())
            .Capability(NodeRole.NavigationSolver, navigationSolver)
            .Capability(NodeRole.Brain,        new CoreInfrastructureCapabilities.UnitHierarchy())
            .Capability(NodeRole.Brain,        new EqsResultUpdateCapability())
            // ⭐ CE-2121 — the character body (stance) over the fake animation backend; module-only, appended last so every
            //   pinned system sequence and module slot is unchanged. 📄 docs/DESIGN_Decision_Layer.md §3.3g.
            .Capability(NodeRole.MuscleGround, new AnimationMuscleCapability());
    }

    /// <summary>
    /// Builds the plan for the <b>injected</b> arm — a host (today only Stride's mode 1) supplies its
    /// own muscle modules through <c>MuscleModuleFactory</c>.
    /// </summary>
    /// <remarks>
    /// The injected arm has no <c>SimHostCoreLogicPack</c> and no EQS module of its own: the
    /// supplying host owns both. It still gets the Brain *(and, until the AreaQuery pipeline was retired
    /// on 2026-10-01, the area-query materialisation — EQS design §18)*. ⚠ Keeping the two arms as two plan shapes — rather than one plan
    /// with nullable capabilities — is what stops a null capability being registered as if it were
    /// real, which is the silent-default shape this programme keeps finding.
    /// </remarks>
    public static NodeCompositionPlan BuildWithInjectedMuscle(
        CgfLogicPack cgfPack,
        IReadOnlyList<INodeCapability> injectedMuscleCapabilities)
    {
        if (cgfPack is null) throw new ArgumentNullException(nameof(cgfPack));
        if (injectedMuscleCapabilities is null) throw new ArgumentNullException(nameof(injectedMuscleCapabilities));

        var plan = new NodeCompositionPlan()
            .Capability(NodeRole.Brain, new Brain(cgfPack));

        // ⭐ S2b — the host hands over CAPABILITIES, not bare modules. They are added in the host's
        //    own order, after Brain and before the Brain's EQS result update, which is where it sat when
        //    this was a Func returning IEcsModule (registration order is execution order).
        foreach (INodeCapability capability in injectedMuscleCapabilities)
            plan = plan.Capability(NodeRole.MuscleGround, capability);

        return plan
            .Capability(NodeRole.Brain,      new CoreInfrastructureCapabilities.UnitHierarchy())
            .Capability(NodeRole.Brain,      new EqsResultUpdateCapability());
    }

    /// <summary>The Brain tier — CGF's logic pack, contributed as systems, never as a module.</summary>
    public sealed class Brain : INodeCapability
    {
        private readonly CgfLogicPack _pack;
        internal Brain(CgfLogicPack pack) => _pack = pack;

        public string Key => CapabilityKeys.Brain;
        public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

        public void PopulateSystems(
            HrotNodeContext context,
            List<IEcsModuleSystem> input,
            List<IEcsModuleSystem> simulation,
            List<IEcsModuleSystem> postSimulation)
        {
            // ⚠ CgfLogicPack has NO post-simulation list — measured, not assumed. The hand-written
            //   block builds togglePostSim from the MUSCLE list alone, and this mirrors that exactly.
            foreach (IEcsModuleSystem s in _pack.InputSystems)      input.Add(s);
            foreach (IEcsModuleSystem s in _pack.SimulationSystems) simulation.Add(s);
        }
    }

    /// <summary>
    /// The default MuscleGround tier — SimHost's core pack, contributed as systems only.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Deliberately no <c>Register</c>.</b> SimHost registers this pack as a module; the editor
    /// never has, and splicing only its system lists is the behaviour being preserved. Adding a module
    /// registration here would double-run the pack.
    /// </remarks>
    public sealed class MuscleGround : INodeCapability
    {
        private readonly SimHostCoreLogicPack _pack;
        internal MuscleGround(SimHostCoreLogicPack pack) => _pack = pack;

        public string Key => CapabilityKeys.MuscleGround;
        public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

        public void PopulateSystems(
            HrotNodeContext context,
            List<IEcsModuleSystem> input,
            List<IEcsModuleSystem> simulation,
            List<IEcsModuleSystem> postSimulation)
        {
            foreach (IEcsModuleSystem s in _pack.InputSystems)          input.Add(s);
            foreach (IEcsModuleSystem s in _pack.SimulationSystems)     simulation.Add(s);
            foreach (IEcsModuleSystem s in _pack.PostSimulationSystems) postSimulation.Add(s);
        }
    }

    // ⛔ `InjectedMuscle` (a capability that wrapped a host's bare IEcsModule list) was DELETED by
    //    S2b. It only existed to adapt MuscleModuleFactory's return type; now that hosts hand over
    //    INodeCapability directly there is nothing to adapt, and keeping the wrapper would be a
    //    second way to express the same thing.

    // ⛔ PerceptionSpatial (CognitiveSpatialModule) DELETED by CE-3038 — vision is a sensor solved by the EQS module
    //    PerceptionEqsSolver registers. docs/DESIGN_Sensors_And_Doctrine.md §5.5.

    /// <summary>
    /// The EQS 1.3 solver and its template registry — default arm only (an injected muscle brings its
    /// own, as Stride's does).
    /// </summary>
    /// <remarks>
    /// ⭐ This arm already ran the area query's solver (inside <see cref="CognitiveSpatialModule"/>) but no
    /// EQS solver, so an EQS sensor authored here — including the area query's EQS form — never got an
    /// answer. 📄 <c>docs/designs/eqs-2/EQS_Design_v1.3_final.md</c> §17.3.
    /// </remarks>
    public sealed class PerceptionEqsSolver : INodeCapability
    {
        public string Key => CapabilityKeys.Perception + ":eqs";
        public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

        public void Register(HrotNodeContext context, NodeBootValues values)
            => EqsSolverStartup.Register(context);

        public void PopulateSystems(HrotNodeContext context, List<IEcsModuleSystem> input,
                                    List<IEcsModuleSystem> simulation, List<IEcsModuleSystem> postSimulation)
            => EqsSolverStartup.PopulateSystems(simulation);   // CE-3061
    }

}
