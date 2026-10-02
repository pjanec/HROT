using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Abstractions;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Map.Common;

/// <summary>
/// ⭐⭐⭐ <b>HROT's ownership groups — one per <see cref="NodeRole"/>.</b> 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c>
/// §1 (the classification each member comes from) and §2 (the groups). 🔒 R-171 (perception is its own group; damage
/// and perception intents stay with the Brain), R-172 (groups are per role, never per node).
///
/// <para>⭐ A component is in a group when that role's logic is its steady-state writer. Members that no descriptor
/// carries are LINKED to the group's anchor descriptor by the network binding (R-165, D-4). Everything in no group and
/// not <see cref="OwnershipGroupTable.Local"/> stays with the entity's creator (R-160).</para>
///
/// <para>⚠ Lives beside <see cref="HrotRoleComponentSets"/> in the same home (D-5). The role tables stay the source of
/// claims until build step S4 retires them; this table is the source of GRANTS and the descriptor binding.</para>
/// </summary>
public static class HrotOwnershipGroups
{
    /// <summary>The table every host and every network binding uses — one instance, so nodes cannot disagree.</summary>
    public static OwnershipGroupTable Table { get; } = Build();

    private static OwnershipGroupTable Build()
    {
        // ── Brain (§1.1) — behaviour, missions, perception INTENTS, damage application ──────────────
        var brain = default(BitMask512);
        brain.SetBit(ComponentType<BehaviorState>.ID);
        brain.SetBit(ComponentType<LocomotionChannel>.ID);
        brain.SetBit(ComponentType<WeaponChannel>.ID);
        brain.SetBit(ComponentType<InteractionChannel>.ID);
        brain.SetBit(ComponentType<PreviousCapabilities>.ID);
        brain.SetBit(ComponentType<BrainInterrupts>.ID);
        brain.SetBit(ComponentType<BTreeTraceWorkingMemory1024>.ID);
        brain.SetBit(ComponentType<HsmTraceWorkingMemory1024>.ID);
        brain.SetBit(GlobalComponentIds.BlueprintBlackboard256);
        brain.SetBit(GlobalComponentIds.BlueprintBlackboard1024);
        brain.SetBit(GlobalComponentIds.BlueprintBlackboard4096);
        brain.SetBit(GlobalComponentIds.BlueprintBlackboard16384);
        brain.SetBit(NavigationContractsComponentIds.NavigationIntent);
        brain.SetBit(GlobalComponentIds.MissionPlanQueue);
        brain.SetBit(BehaviorApplicationComponentIds.ActiveMissionPlan);
        brain.SetBit(ComponentType<Fdp.Toolkit.Perception.Components.PerceptionReceptor>.ID);
        brain.SetBit(GlobalComponentIds.EqsSensor);
        brain.SetBit(GlobalComponentIds.TargetMemory);
        brain.SetBit(Fdp.Toolkit.Perception.PerceptionApplicationComponentIds.ActiveSensorTracks);
        brain.SetBit(GlobalComponentIds.WeaponState);
        brain.SetBit(GlobalComponentIds.CombatHealth);
        brain.SetBit(GlobalComponentIds.ActorCapabilityState);
        // Animation intents (dormant: no production host composes animation replication — design §1.5).
        brain.SetBit(GlobalComponentIds.StanceIntent);
        brain.SetBit(GlobalComponentIds.AnimationMontageQueue);

        // ── MuscleGround (§1.2) — dtWorldPos WHOLE (R-170), navigation status, its never-sent state ───
        var muscle = default(BitMask512);
        muscle.SetBit(GlobalComponentIds.SimTransform);
        muscle.SetBit(GlobalComponentIds.SimVelocity);
        muscle.SetBit(GlobalComponentIds.VehicleState);
        muscle.SetBit(GlobalComponentIds.VehicleParams);
        muscle.SetBit(GlobalComponentIds.NavState);
        muscle.SetBit(NavigationContractsComponentIds.NavigationStatus);
        muscle.SetBit(GlobalComponentIds.FrustrationTicks);
        // Animation statuses (dormant, as above).
        muscle.SetBit(GlobalComponentIds.StanceStatus);
        muscle.SetBit(GlobalComponentIds.AnimationMontageQueueState);

        // ── Perception (§1.2b, R-171) — perception EXECUTION ─────────────────────────────────────────
        var perception = default(BitMask512);
        perception.SetBit(GlobalComponentIds.EqsCognitiveBuffer);
        perception.SetBit(GlobalComponentIds.SensorEvalState);
        perception.SetBit(Fdp.Toolkit.Perception.PerceptionApplicationComponentIds.SensorContactList);

        // ── LOCAL (§1.4) — owned by nobody; only those that appear on a descriptor matter to the binding ──
        var local = default(BitMask512);
        local.SetBit(GlobalComponentIds.NetworkTransform);
        local.SetBit(GlobalComponentIds.NetworkVelocity);
        local.SetBit(Fdp.Toolkit.Geographic.GeographicComponentIds.GroundClampingConfig);

        return new OwnershipGroupTable(new[]
        {
            new OwnershipGroup(NodeRole.Brain,            brain,      HasBrain),
            new OwnershipGroup(NodeRole.MuscleGround,     muscle,     HasKinematics),
            new OwnershipGroup(NodeRole.Perception,       perception, HasPerception),
            // Empty today (§2 G-9): the solver writes no component of its own (requests/responses are events);
            // Map2D claims nothing (R-161).
            new OwnershipGroup(NodeRole.NavigationSolver, default,    _ => false),
            new OwnershipGroup(NodeRole.Map2D,            default,    _ => false),
        }, local);
    }

    // G-4 — the same conditions the TKB translators use to provision each group's components.
    private static bool HasBrain(TkbTemplate t)      => t.GetDescriptor<BehaviorProfileDto>() is { BrainTier: not 0 };
    private static bool HasKinematics(TkbTemplate t) => t.GetDescriptor<VehicleParametersDto>() != null;
    private static bool HasPerception(TkbTemplate t) => t.GetDescriptor<SensorCapabilitiesDto>() is { VisionRange: > 0f };
}
