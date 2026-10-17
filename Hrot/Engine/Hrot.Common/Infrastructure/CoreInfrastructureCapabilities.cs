#nullable enable
using System;
using System.Collections.Generic;
using Fdp.Core;
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
    /// ⭐⭐ <c>CE-2144</c> (R-2008) — THE way a host starts its plan: a new plan whose first declaration is <see cref="HealthApplication"/>,
    /// so damage is applied at the HEAD of Simulation on every live-simulation node (before the cognitive runtime and the dispatchers
    /// read the capabilities it strips). 📄 docs/DESIGN_Orders_Params_Entity_Classes.md §11.2.
    /// </summary>
    /// <remarks>⛔ A host never writes <c>new NodeCompositionPlan()</c> — a source rail pins that every production plan comes from here.
    /// 🔒 User, <c>2026-10-13</c>: "provided it runs unified on any ECS enabled node running live simulation - shared code, shared
    /// bootstrap".</remarks>
    public static NodeCompositionPlan LiveSimulationPlan(NodeRole role)
        => new NodeCompositionPlan().Capability(role, new HealthApplication());

    /// <summary>
    /// ⭐ <c>CE-2144</c> (R-2008) — <c>HealthApplicationSystem</c>, the owner side of damage: applies a <c>DamageAssessedEvent</c> where
    /// this node holds the entity's Health claim and strips <c>CanMove</c>/<c>CanShoot</c> at 0 HP. Cross-role because a passenger, a
    /// door or a soldier may be owned by a node of any role; inert where nothing publishes damage (an unregistered event reads empty).
    /// </summary>
    /// <remarks>⛔ Declared ONLY by <see cref="LiveSimulationPlan"/> — its position is load-bearing (§11.1): at the head it strips the
    /// capabilities before the cognitive runtime and the dispatchers of the same frame read them, as it did inside <c>CgfLogicPack</c>.</remarks>
    public sealed class HealthApplication : INodeCapability
    {
        private readonly Fdp.Toolkit.Combat.Systems.HealthApplicationSystem _system = new();
        // ⭐ CE-2155 (R-2011 I3) — an incapacitated person bleeds out where his Health is owned: the other Health writer, beside damage.
        private readonly Fdp.Toolkit.Combat.Systems.BleedOutSystem _bleedOut = new();

        public string Key => CapabilityKeys.HealthApplication;

        public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

        /// <summary>The systems, for a hand-wired harness that must match production (CE-493); run them FIRST in its sim list.</summary>
        public IReadOnlyList<IEcsModuleSystem> Systems => new IEcsModuleSystem[] { _system, _bleedOut };

        public void PopulateSystems(
            HrotNodeContext context,
            List<IEcsModuleSystem> input,
            List<IEcsModuleSystem> simulation,
            List<IEcsModuleSystem> postSimulation)
        {
            if (simulation is null) throw new ArgumentNullException(nameof(simulation));
            simulation.Add(_system);
            simulation.Add(_bleedOut);
        }
    }

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
    /// ⭐⭐ <c>CE-3110</c> (R-221 / R-224) — entity interactions, the OWNER side, on EVERY node that can own a live entity:
    /// <c>DoorCommandSystem</c> (a door command applied where the door's <c>dtDoorState</c> is owned — 📄 DESIGN_Building_Interiors.md
    /// §3j 5d), <c>EmbarkationSystem</c> (a boarding applied where the vehicle's <c>dtPassengerBuffer</c> is owned) and
    /// <c>EmbarkedStateSystem</c> (the passenger side derived from the list each node holds). 📄 docs/DESIGN_Entity_Interactions.md §7.1d.
    /// </summary>
    /// <remarks>
    /// <para>⛔ <b>Why not in a role pack.</b> Ownership is per descriptor and push-only (the creator claims everything), so a door, a
    /// vehicle or a passenger can be owned by a node of any role — a role pack leaves every other host unable to apply the command it
    /// receives. Every system here gates itself on what this node owns or holds, so running everywhere costs nothing where there is
    /// nothing to do. ⭐ A new interaction kind adds its owner-side system HERE, never in a host or a pack.</para>
    /// <para>⭐ <b>Order inside its phase is free.</b> The appliers read commands published the frame before; <c>Embarkation.Board</c>
    /// writes the passenger's tag and strips its capabilities in one call, so <c>CognitiveInterruptSystem</c> sees the tag in whichever
    /// frame it notices the lost <c>CanMove</c>. ⇒ declared at the tail like <see cref="UnitHierarchy"/>.</para>
    /// </remarks>
    public sealed class Interactions : INodeCapability
    {
        private readonly Fdp.Toolkit.Behavior.Systems.EmbarkedStateSystem _passengerSide = new();
        private readonly Fdp.Toolkit.Terrain.DoorCommandSystem            _doors         = new();
        private readonly Fdp.Toolkit.Behavior.Systems.EmbarkationSystem   _embarkation   = new();
        private readonly Fdp.Toolkit.Combat.WeaponDeploySystem            _deploy        = new();   // ⭐ Weapon systems WS-1 (W-C)
        private readonly Hrot.Common.EntityCreation.EjectionSystem        _ejection      = new();   // ⭐ Airborne insertion B-8 (§7d D-14)
        private readonly Fdp.Toolkit.Combat.Systems.AreaEffectCloudSystem _clouds    = new();   // ⭐ CE-3219 part 3 — the owner drifts, irritates, ends it
        private readonly Fdp.Toolkit.Combat.Systems.RoundPickUpSystem     _pickUp    = new();   // ⭐ CE-3235 — the round's flier grants, the brain owner receives
        private readonly Fdp.Toolkit.Combat.Systems.HandsFuzeSystem       _handsFuze = new();   // ⭐ CE-3235 — a live round burns in the hand
        private readonly Fdp.Toolkit.Behavior.CarryStationSystem          _stations  = new();   // ⭐ CE-3233 — draw / stow / conceal on the list owner

        public string Key => CapabilityKeys.Interactions;

        public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

        /// <summary>The systems, for a hand-wired harness that must match production (CE-493).</summary>
        public IReadOnlyList<IEcsModuleSystem> Systems => new IEcsModuleSystem[] { _passengerSide, _doors, _embarkation, _deploy, _ejection, _clouds, _pickUp, _handsFuze, _stations };

        public void PopulateSystems(
            HrotNodeContext context,
            List<IEcsModuleSystem> input,
            List<IEcsModuleSystem> simulation,
            List<IEcsModuleSystem> postSimulation)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            if (simulation is null) throw new ArgumentNullException(nameof(simulation));
            input.Add(_passengerSide);
            simulation.Add(_doors);
            simulation.Add(_embarkation);
            simulation.Add(_deploy);
            simulation.Add(_ejection);
            simulation.Add(_clouds);
            simulation.Add(_pickUp);
            simulation.Add(_handsFuze);
            simulation.Add(_stations);
        }
    }
}
