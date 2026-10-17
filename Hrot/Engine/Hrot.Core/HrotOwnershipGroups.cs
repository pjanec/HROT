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
        // ⭐ CE-513 / R-180 — an animation channel crosses nodes, so it is split: the Brain owns the REQUEST.
        brain.SetBit(GlobalComponentIds.AnimationChannel);
        brain.SetBit(GlobalComponentIds.LookAtChannel);
        // ⭐ CE-3048 — the SOP slot and the ROE: brain-written, carried by dtBrainIntent (they were in no group).
        brain.SetBit(GlobalComponentIds.SopState);
        brain.SetBit(GlobalComponentIds.Roe);
        // ⭐ WL-2b (CE-1049, W-2/W-11) — the weapon brain's order to a turret (dtWeaponTask).
        brain.SetBit(GlobalComponentIds.WeaponTask);
        // ⭐ WL-6 (W-13c, R-272) — the weapon brain's at ease / ready / aimed (dtWeaponPosture).
        brain.SetBit(GlobalComponentIds.WeaponPosture);
        // ⭐ CE-3172 / CE-3173 S4e (docs/DESIGN_Entity_Control.md §8c) — possession: the operator's axes (dtManualDrive, written by
        //   OperatorControl) and who holds the unit (dtPossessedBy, PossessionSystem the one writer). OperatorInput stays brain-local.
        brain.SetBit(GlobalComponentIds.ManualDrive);
        brain.SetBit(GlobalComponentIds.PossessedBy);
        // ⭐ CE-3174 (C-7) — the ammunition per mount, mirrored by the brain from its WeaponStates (dtWeaponAmmo).
        brain.SetBit(GlobalComponentIds.WeaponAmmo);
        // ⭐ CE-1153 (§8h.6) — the brain's verdict on the operator's last use press (dtOperatorUseStatus).
        brain.SetBit(GlobalComponentIds.OperatorUseStatus);
        // ⭐ CE-3219 (civilians B-1, CV-6) — a person's stored non-lethal effects, written beside Health by its owner (dtEffectState).
        brain.SetBit(GlobalComponentIds.EffectState);
        // ⭐ CE-3224 (civilians B-6, CV-13) — what a person is doing with his arms, chosen by his behaviour (dtGesture).
        brain.SetBit(GlobalComponentIds.Gesture);
        // ⭐ CE-3238 (civilians B-16, AW-22) — a man's hostile act, committed by his behaviour (dtHostileAct).
        brain.SetBit(GlobalComponentIds.HostileAct);
        // ⭐ CE-3210 (hit location HL-6) — the damage per body part, written beside Health by its owner (dtPartDamage).
        brain.SetBit(GlobalComponentIds.PartDamage);
        // ⭐ CE-3110 I-2b (R-224) — the vehicle's passenger list: its brain's EmbarkationSystem is the one writer (dtPassengerBuffer).
        brain.SetBit(GlobalComponentIds.PassengerBuffer);
        // ⭐ Weapon systems WS-1 (W-C, R-136) — a weapon system's deploy state: its brain's WeaponDeploySystem writes it (dtWeaponSystemState).
        brain.SetBit(GlobalComponentIds.WeaponSystemState);
        // ⭐ CE-3216 S1-0 (R-3012, W-6) — the scenario clock: the Brain that loads the scenario creates and owns it, applies an edit (dtScenarioClock).
        brain.SetBit(GlobalComponentIds.ScenarioClock);
        // ⭐ CE-3216 S1 — authored weather: the Brain that creates the World Environment / a Weather Region owns it (dtWeather).
        brain.SetBit(GlobalComponentIds.WeatherConditions);

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
        // ⭐ CE-513 / R-180 — …and the Muscle owns the REPORT (Architect_Question_80 §0 A).
        muscle.SetBit(GlobalComponentIds.AnimationChannelStatus);
        muscle.SetBit(GlobalComponentIds.LookAtChannelStatus);
        // ⭐ WL-1b (CE-1049, DESIGN_Weapon_Layers W-9) — the weapon MUSCLE aims the turret, so it owns the pose (dtTurretPose).
        muscle.SetBit(GlobalComponentIds.TurretPose);
        // ⭐ CE-3210 (hit location HL-8) — where rounds struck a body: the Muscle resolves the hit (dtHitMarks).
        muscle.SetBit(GlobalComponentIds.HitMarks);
        // ⭐ Airborne insertion B-1 (D-1) — a man coming down a rope or under a canopy: moved by the node that owns his position (dtDescent).
        muscle.SetBit(GlobalComponentIds.Descent);
        muscle.SetBit(GlobalComponentIds.WeaponTaskStatus);   // ⭐ WL-2b (W-3) — the muscle's report on a turret's task
        muscle.SetBit(GlobalComponentIds.WeaponMuscleState);  // ⭐ WL-3 — its never-sent fire timing (like FrustrationTicks)
        // ⭐ CE-3199 / R-3005 — ONE Muscle role moves every body, ground or air: a flyer's never-sent flight state and its flight
        //   parameters sit here beside VehicleState / VehicleParams (docs/designs/motion-models/DESIGN_Motion_Domains.md §5a).
        muscle.SetBit(GlobalComponentIds.FlightParams);
        muscle.SetBit(GlobalComponentIds.FlightState);

        // ── Perception (§1.2b, R-171) — perception EXECUTION ─────────────────────────────────────────
        var perception = default(BitMask512);
        perception.SetBit(GlobalComponentIds.EqsCognitiveBuffer);
        perception.SetBit(GlobalComponentIds.SensorEvalState);
        perception.SetBit(Fdp.Toolkit.Perception.PerceptionApplicationComponentIds.SensorContactList);

        // ── Traffic (AQ93 §12 S2, R-4056) — the scenario's traffic SETTINGS: the creating Brain stays the primary owner (lifetime, saving)
        //   and grants this group to the least-loaded Traffic node, which SERVES them (runs SUMO) and applies an edit (dtTrafficDemand).
        var traffic = default(BitMask512);
        traffic.SetBit(GlobalComponentIds.TrafficDemand);

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
            new OwnershipGroup(NodeRole.Traffic,          traffic,    HasTrafficDemand),
        }, local);
    }

    // G-4 — the same conditions the TKB translators use to provision each group's components.
    private static bool HasBrain(TkbTemplate t)      => t.GetDescriptor<BehaviorProfileDto>() is { BrainTier: not 0 };
    // ⭐ CE-3199 / R-3005 — a FLYER (Gen.Flight) is moved by the Muscle too: there is one Muscle role (MuscleGround keeps its name).
    // ⭐⭐ Weapon systems WS-4 (DESIGN_Weapon_Systems §2, §9) — and a WEAPON SYSTEM: it has no locomotion, but the Muscle aims its turret
    //   (TurretPose, WeaponTaskStatus) and carries it in its host's station (SimTransform, like any passenger whose position the Muscle owns).
    //   Without it the turret's pose stays with the node that created the weapon, and on a split cluster the Muscle never fires it.
    private static bool HasKinematics(TkbTemplate t)
        => t.GetDescriptor<VehicleParametersDto>() != null || t.GetDescriptor<FlightParamsDto>() != null
           || t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.WeaponSystemDto>() != null;
    // ⭐ AQ93 S2 — the scenario's traffic settings entity (TKB "Traffic") is served by a Traffic node.
    private static bool HasTrafficDemand(TkbTemplate t) => t.GetDescriptor<Fdp.Toolkit.Traffic.TrafficDemandDto>() != null;

    // ⭐ CE-3036 — a unit with any SENSOR is perceived by the Perception group too (V3: its results publish from there).
    private static bool HasPerception(TkbTemplate t)
        => t.GetDescriptor<SensorCapabilitiesDto>() is { } s && (s.VisionRange > 0f || s.Sensors.Count > 0);
}
