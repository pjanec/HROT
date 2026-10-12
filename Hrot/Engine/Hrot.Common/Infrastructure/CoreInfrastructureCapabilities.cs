#nullable enable
using System;
using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Hrot.Common.Systems;

namespace Hrot.Common.Infrastructure;

/// <summary>
/// Node infrastructure that answers to no role — <c>CE-221</c>.
/// </summary>
/// <remarks>
/// <para><b>Why this type exists.</b> A handful of systems are needed by a node because it is a NODE,
/// not because it plays a particular role. They were carried by the role packs instead, so every
/// composition root that fused a Brain with a Muscle registered each of them twice. Three roots
/// hand-rolled a de-duplication, a fourth forgot, <c>CE-165</c> added a shared helper and a
/// <c>[SingleInstance]</c> guard — and the guard then turned the remaining case into a hard boot
/// failure on the one host nobody could run (<c>CE-221</c>: the Stride editor died in
/// <c>BeginRun()</c>).</para>
///
/// <para>⭐⭐ <b>The fix is to stop duplicating, not to de-duplicate better.</b> A capability is declared
/// once per plan, and <see cref="NodeCompositionPlan.Resolve"/> already de-duplicates by
/// <see cref="INodeCapability.Key"/> in declaration order — the property
/// <c>NodeCompositionPlanRails</c> pins. So a host that declares this capability, and a muscle tier
/// injected from another host that also declares it, resolve to ONE instance, with the earlier
/// declaration winning. That is the same first-wins rule <c>DistinctByType(cgf…, muscle…)</c> already
/// encoded, expressed once instead of at every fusing root.</para>
///
/// <para>⛔ <b>Do not put a role-specific system in here.</b> The test is the one in the
/// <c>CE-221</c> analysis: every carrier appended these at the TAIL of the Simulation phase, never
/// positioned relative to its own systems. A system whose position depends on its pack belongs to
/// that pack.</para>
///
/// <para>⚠ <b>Execution-order note, and it is a deliberate behaviour change.</b> Declaring these last
/// puts them at the end of the Simulation phase, which is where every carrier already had them —
/// except the editor. There, <c>DistinctByType(cgfLogicPack…, muscle…)</c> kept CGF's copy, which sat
/// at the tail of CGF's list and therefore ahead of the whole muscle tier, so
/// <c>UnitHierarchySystem</c> consumed <c>CmdAssignSubordinate</c> (published by
/// <c>VehicleCommandSystem</c> in <c>GroundKinematicsModule</c>) one frame late. SimHost has always
/// consumed it the same frame. This change makes the editor agree with every other host; it is a
/// correction, not a preference, and it is argued in the batch report rather than taken silently.</para>
/// </remarks>
public static class CoreInfrastructureCapabilities
{
    /// <summary>
    /// Maintains the ECS commander/subordinate hierarchy (<c>CS016</c>).
    /// </summary>
    /// <remarks>
    /// <c>PopulateSystems</c> rather than <c>ProvideModules</c>, deliberately: contributing through the
    /// phase lists is what lets a host's ordering stay visible in one place. It was contribution
    /// through the <i>module</i> path — invisible to the host's de-duplication — that produced
    /// <c>CE-221</c>.
    /// </remarks>
    public sealed class UnitHierarchy : INodeCapability
    {
        private readonly UnitHierarchySystem _system = new();

        public string Key => CapabilityKeys.UnitHierarchy;

        public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

        public void PopulateSystems(
            HrotNodeContext context,
            List<IEcsModuleSystem> input,
            List<IEcsModuleSystem> simulation,
            List<IEcsModuleSystem> postSimulation)
        {
            if (simulation is null) throw new ArgumentNullException(nameof(simulation));
            simulation.Add(_system);
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3110</c> I-2a/I-2b (R-224) — embarkation on EVERY node that can own a live entity: <c>EmbarkationSystem</c>
    /// applies a boarding where the vehicle's <c>dtPassengerBuffer</c> is owned (it gates itself), and <c>EmbarkedStateSystem</c>
    /// derives the passenger side from the list each node holds. 📄 docs/DESIGN_Entity_Interactions.md §7.1d.
    /// </summary>
    /// <remarks>
    /// <para>⛔ <b>Why not in a role pack.</b> Ownership is per descriptor and push-only (the creator claims everything), so a vehicle
    /// or a passenger can be owned by a node of any role — a role pack would leave every other host unable to board or to see who is
    /// aboard. Both systems gate themselves on what this node owns or holds, so running them everywhere costs nothing where there is
    /// nothing to do.</para>
    /// <para>⭐ <b>Order inside its phase is free.</b> <c>Embarkation.Board</c> writes the passenger's tag and strips its capabilities in
    /// one call, so <c>CognitiveInterruptSystem</c> sees the tag in whichever frame it notices the lost <c>CanMove</c>; the applier reads
    /// commands published the frame before. ⇒ declared at the tail like <see cref="UnitHierarchy"/>.</para>
    /// </remarks>
    public sealed class Embarkation : INodeCapability
    {
        private readonly Fdp.Toolkit.Behavior.Systems.EmbarkedStateSystem _derivation = new();
        private readonly Fdp.Toolkit.Behavior.Systems.EmbarkationSystem   _applier    = new();

        public string Key => CapabilityKeys.Embarkation;

        public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

        /// <summary>The two systems, for a hand-wired harness that must match production (CE-493).</summary>
        public IReadOnlyList<IEcsModuleSystem> Systems => new IEcsModuleSystem[] { _derivation, _applier };

        public void PopulateSystems(
            HrotNodeContext context,
            List<IEcsModuleSystem> input,
            List<IEcsModuleSystem> simulation,
            List<IEcsModuleSystem> postSimulation)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            if (simulation is null) throw new ArgumentNullException(nameof(simulation));
            input.Add(_derivation);
            simulation.Add(_applier);
        }
    }
}
