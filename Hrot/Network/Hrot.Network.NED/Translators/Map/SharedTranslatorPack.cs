using System.Collections.Generic;
using Hrot.Map.Common.Replication;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;

namespace Hrot.Map.Common.Translators
{
    /// <summary>
    /// Factory for the shared translator set that all <see cref="NodeRole"/> values
    /// install regardless of specialisation.
    ///
    /// <para><b>Translators created (in DDS ordinal order):</b></para>
    /// <list type="number">
    ///   <item><see cref="EntityMasterEgressTranslator"/>  — publishes entity births/deaths (ordinal 0).</item>
    ///   <item><see cref="EntityMasterIngressTranslator"/> — ghost-creates remote entities (ordinal -2).</item>
    ///   <item><see cref="EntityInfoEgressTranslator"/>    — publishes entity metadata such as affiliation (ordinal 21).</item>
    ///   <item><see cref="FireInteractionEventTranslator"/>— bidirectional fire-interaction events (Muscle egress / IG ingress).</item>
    ///   <item><see cref="EntityDamageEgressTranslator"/>  — publishes health changes to IG/ExCon (ordinal 30).</item>
    ///   <item><see cref="GeoSpatialEgressTranslator"/>    — publishes position/velocity for owned entities (ordinal 2); moved here
    ///     from <see cref="KinematicTranslatorPack"/> so Brain nodes can broadcast the initial WorldPos before
    ///     handing physics authority to the Muscle.</item>
    ///   <item><see cref="GeoSpatialIngressTranslator"/>   — ingests incoming WorldPos packets so every node (Brain, Muscle, IG)
    ///     can attach SimTransform to ghost entities and allow GhostPromotionSystem to advance lifecycle.</item>
    ///   <item><see cref="OwnershipUpdateTranslator"/>     — bidirectional authority-handover notification (Muscle egress / Brain ingress).</item>
    ///   <item><see cref="MapVisualOverlayEgressTranslator"/> — publishes tactical-graphic overlay geometry for owned area entities.
    ///     Moved here from <see cref="KinematicTranslatorPack"/> so Brain nodes (CGF) can publish overlays for area
    ///     entities they own — same rationale as <see cref="GeoSpatialEgressTranslator"/>.</item>
    ///   <item><see cref="MapVisualOverlayIngressTranslator"/> — applies incoming overlay geometry to ghost entities.
    ///     Needed on all roles (Brain and Muscle) so non-owning nodes maintain an up-to-date
    ///     <see cref="EditablePolyline"/> component for area entities published by the owning node.</item>
    ///   <item><see cref="MapRouteEgressTranslator"/> / <see cref="MapRouteIngressTranslator"/> — a route's <c>RoutePlan</c>, its
    ///     owner → every node (CE-2171: CGF owns scenario routes; SimHost follows them).</item>
    /// </list>
    /// </summary>
    public static class SharedTranslatorPack
    {
        /// <summary>
        /// Creates the shared translator instances.
        /// </summary>
        /// <param name="participant">Live DDS participant; passed to all translator constructors.</param>
        /// <param name="entityMap">Shared <see cref="NetworkEntityMap"/> for egress/ingress lookups.</param>
        /// <param name="localNodeId">Local node identifier; needed by <see cref="EntityMasterEgressTranslator"/>.</param>
        /// <param name="eventBus">
        ///   Application event bus; forwarded to <see cref="EntityMasterIngressTranslator"/>
        ///   so it can publish <c>DestroyEntityCommand</c> on entity teardown.
        /// </param>
        /// <param name="ghostCreationSystem">
        ///   Ghost-creation helper; used by <see cref="EntityMasterIngressTranslator"/>
        ///   and <see cref="MapVisualOverlayIngressTranslator"/> to materialise replicas of remote entities.
        /// </param>
        /// <param name="geoTransform">
        ///   Geodetic coordinate transform; passed to <see cref="GeoSpatialEgressTranslator"/>,
        ///   <see cref="GeoSpatialIngressTranslator"/>, <see cref="MapVisualOverlayEgressTranslator"/>,
        ///   and <see cref="MapVisualOverlayIngressTranslator"/> for coordinate conversions.
        /// </param>
        public static IEnumerable<INetworkTranslator> Create(
            DdsParticipant       participant,
            NetworkEntityMap     entityMap,
            long                 localNodeId,
            FdpEventBus          eventBus,
            GhostCreationSystem  ghostCreationSystem,
            IGeographicTransform geoTransform)
        {
            yield return new EntityMasterEgressTranslator(participant, entityMap, localNodeId);
            yield return new EntityMasterIngressTranslator(participant, entityMap, localNodeId, eventBus, ghostCreationSystem);
            yield return new EntityInfoEgressTranslator(participant, entityMap, localNodeId);
            yield return new EntityInfoIngressTranslator(participant, entityMap, eventBus, ghostCreationSystem, localNodeId);
            yield return new FireInteractionEventTranslator(participant, entityMap);
            yield return new EntityDamageEgressTranslator(participant, entityMap);
            // ⭐⭐⭐ CE-272 — the INGRESS half of EntityDamage belongs here, paired with its egress, exactly
            //   like EntityMaster/EntityInfo/GeoSpatial. It was ONLY in EntityStatesIngressPack (pure-IG),
            //   so a Muscle node never ingested Health: damage is applied on the Brain (authority), but the
            //   Muscle's copy stayed at the TKB default. 🔴 Measured on hill-attack (--mode all): targets
            //   at Health 0 on the Brain, Health 3000 on the Muscle — and the target-finding EQS runs on
            //   the MUSCLE (hill-attack DESIGN §1.3), so it never saw the targets die and the platoon
            //   re-engaged forever. That infinite loop is what CE-267's entity-removal was masking. The
            //   ingress guards on HasAuthority<Health>, so it never clobbers the owner's authoritative value.
            yield return new EntityDamageIngressTranslator(participant, entityMap, ghostCreationSystem, localNodeId);
            // ⭐ Buildings Stage 5b — a terrain door's live state, owner → every node (readers build their door table from their own view, R-219).
            yield return new EntityDoorStateEgressTranslator(participant);
            yield return new EntityDoorStateIngressTranslator(participant, entityMap, ghostCreationSystem, localNodeId);
            // ⭐ CE-3136 P-7a — a static obstacle's box, creator → every node (its ghost waits for it, [PerInstanceValue]).
            yield return new EntityObstacleShapeEgressTranslator(participant);
            yield return new EntityObstacleShapeIngressTranslator(participant, entityMap, ghostCreationSystem, localNodeId);
            // ⭐ WL-1b (CE-1049) — a turret part's pose, the dtTurretPose owner (the Muscle) → every node. 📄 DESIGN_Weapon_Layers §5b.
            yield return new EntityTurretPoseEgressTranslator(participant);
            yield return new EntityTurretPoseIngressTranslator(participant, entityMap);
            // ⭐ WL-2b (CE-1049) — a turret's order (Brain → Muscle) and the muscle's report (Muscle → Brain). 📄 DESIGN_Weapon_Layers §5d.
            yield return new EntityWeaponTaskEgressTranslator(participant, entityMap);
            yield return new EntityWeaponTaskIngressTranslator(participant, entityMap);
            yield return new EntityWeaponTaskStatusEgressTranslator(participant);
            yield return new EntityWeaponTaskStatusIngressTranslator(participant, entityMap);
            // ⭐ WL-6 (W-13d, R-272) — the weapon brain's posture of a unit's weapon, the dtWeaponPosture owner (the Brain) → every node.
            yield return new EntityWeaponPostureEgressTranslator(participant, entityMap);
            yield return new EntityWeaponPostureIngressTranslator(participant, entityMap);
            // ⭐ CE-3172 / CE-3173 S4e (docs/DESIGN_Entity_Control.md §8c) — possession: the operator's axes (Brain → the Muscle) and
            //   who holds the unit (Brain → every node).
            yield return new EntityManualDriveEgressTranslator(participant);
            yield return new EntityManualDriveIngressTranslator(participant, entityMap);
            yield return new EntityPossessedByEgressTranslator(participant);
            yield return new EntityPossessedByIngressTranslator(participant, entityMap);
            // ⭐ CE-3174 (C-7) — the ammunition per mount (Brain → every node), for the FPS HUD on any station.
            yield return new EntityWeaponAmmoEgressTranslator(participant);
            yield return new EntityWeaponAmmoIngressTranslator(participant, entityMap);
            // ⭐ CE-1153 (§8h.6) — the brain's verdict on the operator's last use press (Brain → every node), for the HUD.
            yield return new EntityOperatorUseStatusEgressTranslator(participant);
            yield return new EntityOperatorUseStatusIngressTranslator(participant, entityMap);
            // ⭐ CE-3219 (civilians B-1) — a person's stored non-lethal effects, beside Health (Brain → every node).
            yield return new EntityEffectStateEgressTranslator(participant);
            yield return new EntityEffectStateIngressTranslator(participant, entityMap);
            // ⭐⭐ CE-1148 S2 (docs/DESIGN_Node_Roles_And_Policies.md §3.5, R-3015) — the world events on EVERY node: a shot, a burst, a
            //   round's mark. The egress sends only this node's local events (IsRemote false) stamped with SourceNodeId; the ingress drops
            //   this node's own samples, so no role needs an exclusion and a node with two shooters' worth of events gets each once.
            yield return new Hrot.Network.NED.SimHost.WeaponFireNotificationEgressTranslator(participant, entityMap, localNodeId);
            yield return new Hrot.Network.NED.IG.WeaponFireIngressTranslator(participant, entityMap, localNodeId);
            yield return new Hrot.Network.NED.SimHost.MunitionDetonationEgressTranslator(participant, entityMap, localNodeId);
            yield return new Hrot.Network.NED.SimHost.MunitionDetonationIngressTranslator(participant, entityMap, localNodeId);
            yield return new Hrot.Network.NED.SimHost.RoundImpactEgressTranslator(participant, entityMap, localNodeId);
            yield return new Hrot.Network.NED.SimHost.RoundImpactIngressTranslator(participant, entityMap, localNodeId);
            // ⭐ CE-3224 (civilians B-6) — a person's gesture (Brain → every node).
            yield return new EntityGestureEgressTranslator(participant);
            yield return new EntityGestureIngressTranslator(participant, entityMap);
            // ⭐ CE-3256 (BR-4) — a man in custody (Brain → every node).
            yield return new Hrot.Map.Common.Replication.Egress.EntityCustodyEgressTranslator(participant);
            yield return new Hrot.Map.Common.Replication.Ingress.EntityCustodyIngressTranslator(participant, entityMap);
            // ⭐ CE-3257 (BR-5) — a man leaning round a corner (Brain → every node).
            yield return new Hrot.Map.Common.Replication.Egress.EntityLeanEgressTranslator(participant);
            yield return new Hrot.Map.Common.Replication.Ingress.EntityLeanIngressTranslator(participant, entityMap);
            // ⭐ CE-3259 (BR-6) — a soldier's combat proficiency (Brain → every node).
            yield return new Hrot.Map.Common.Replication.Egress.EntityCombatProficiencyEgressTranslator(participant);
            yield return new Hrot.Map.Common.Replication.Ingress.EntityCombatProficiencyIngressTranslator(participant, entityMap);
            // ⭐ CE-3261 — a man's or a commander's sector (Brain → every node).
            yield return new Hrot.Map.Common.Replication.Egress.EntityEngagementAreaEgressTranslator(participant);
            yield return new Hrot.Map.Common.Replication.Ingress.EntityEngagementAreaIngressTranslator(participant, entityMap);
            // ⭐ CE-3264..3266 (G1/G2/G3) — speech, mood (Brain → every node) and a blow struck (every node → every node).
            yield return new Hrot.Map.Common.Replication.Egress.EntityUtteranceEgressTranslator(participant, entityMap);
            yield return new Hrot.Map.Common.Replication.Ingress.EntityUtteranceIngressTranslator(participant, entityMap);
            yield return new Hrot.Map.Common.Replication.Egress.EntityDemeanourEgressTranslator(participant);
            yield return new Hrot.Map.Common.Replication.Ingress.EntityDemeanourIngressTranslator(participant, entityMap);
            yield return new Hrot.Network.NED.SimHost.MeleeStrikeEgressTranslator(participant, entityMap, localNodeId);
            yield return new Hrot.Network.NED.SimHost.MeleeStrikeIngressTranslator(participant, entityMap, localNodeId);
            // ⭐ CE-3252 (LD-1 / LD-2) — where a person looks (Muscle → every node: vision, the map's cone, HTTP).
            yield return new Hrot.Map.Common.Replication.Egress.EntityLookStateEgressTranslator(participant);
            yield return new Hrot.Map.Common.Replication.Ingress.EntityLookStateIngressTranslator(participant, entityMap);
            // ⭐ CE-3238 (civilians B-16) — a man's hostile act and its kind (Brain → every node).
            yield return new EntityHostileActEgressTranslator(participant);
            yield return new EntityHostileActIngressTranslator(participant, entityMap);
            // ⭐ CE-3219 part 3 (civilians B-1) — a gas / smoke cloud's description (creator → every node).
            yield return new EntityAreaEffectCloudEgressTranslator(participant);
            yield return new EntityAreaEffectCloudIngressTranslator(participant, entityMap);
            // ⭐ CE-3210 (hit location HL-6, HL-8) — the damage per body part (the Health owner → every node) and where rounds struck
            //   (the Muscle → every node).
            yield return new EntityPartDamageEgressTranslator(participant);
            yield return new EntityPartDamageIngressTranslator(participant, entityMap);
            yield return new EntityHitMarksEgressTranslator(participant);
            yield return new EntityHitMarksIngressTranslator(participant, entityMap);
            yield return new EntityDescentEgressTranslator(participant);                  // ⭐ airborne B-1 — a man coming down
            yield return new EntityDescentIngressTranslator(participant, entityMap);
            // ⭐ CE-3110 I-2b (R-224) — a vehicle's passenger list (its owner → every node; each derives the passenger's side).
            yield return new EntityPassengersEgressTranslator(participant);
            yield return new EntityPassengersIngressTranslator(participant, entityMap);
            // ⭐ Weapon systems WS-1 (W-C) — a weapon system's deploy state (its owner → every node).
            yield return new EntityWeaponSystemStateEgressTranslator(participant);
            yield return new EntityWeaponSystemStateIngressTranslator(participant, entityMap);
            // ⭐ CE-3216 S1-0 (R-3012) — the scenario clock on the World Environment, owner → every node.
            yield return new EntityScenarioClockEgressTranslator(participant);
            yield return new EntityTrafficDemandEgressTranslator(participant);                 // ⭐ AQ93 N4 — the traffic settings
            yield return new EntityTrafficDemandIngressTranslator(participant, entityMap);
            yield return new EntityScenarioClockIngressTranslator(participant, entityMap);
            // ⭐ CE-3216 S1 — authored weather (the global and every region), owner → every node; WeatherField derives the rest.
            yield return new EntityWeatherEgressTranslator(participant);
            yield return new EntityWeatherIngressTranslator(participant, entityMap);
            // ⭐ R-221 — every interaction kind (doors first) travels to the target's owner on ONE topic; the kind's handler applies it there.
            yield return new Hrot.Map.Common.Replication.Interactions.InteractionEgressTranslator(participant, entityMap);
            yield return new Hrot.Map.Common.Replication.Interactions.InteractionIngressTranslator(participant, entityMap);
            yield return new GeoSpatialEgressTranslator(participant, entityMap, geoTransform, localNodeId);
            yield return new GeoSpatialIngressTranslator(participant, entityMap, geoTransform, ghostCreationSystem, localNodeId);
            yield return new OwnershipUpdateTranslator(participant, (int)localNodeId);
            yield return new MapVisualOverlayEgressTranslator(participant, entityMap, geoTransform, localNodeId);
            yield return new MapVisualOverlayIngressTranslator(participant, entityMap, geoTransform, ghostCreationSystem, localNodeId);
            // ⭐ CE-2171 (Air Ops E2) — a route's plan, its owner → every node. Moved here from KinematicTranslatorPack (egress) and
            //   added (ingress) beside the overlay pair, for the same reason: a SCENARIO route is created and owned by CGF (Brain), and
            //   the egress existed only on the Muscle ⇒ SimHost, where the route is followed, never had the plan. 📄 ROUTES1 §6.1/6.2.
            //   ⭐ CE-1148 S1 — every node, the IG included, polls this pack's ingress (no second IG list).
            yield return new MapRouteEgressTranslator(participant, entityMap, geoTransform);
            yield return new MapRouteIngressTranslator(participant, entityMap, geoTransform);
            yield return new UpdateEntityAttributeCommandEgressTranslator(participant);
            // ⭐ CE-2247 — a destroy on a node that does not own the entity's master goes to the owner (DeleteEntityRequest), whose
            //   teardown disposes it everywhere. Was the IG's alone (CreateIgEgressTranslators); a charge set off on SimHost stayed live
            //   on CGF and the IG. 📄 DESIGN_Entity_Creation_Unification.md §3.4c, DESIGN_Node_Roles_And_Policies.md §3.5 (R-3015).
            yield return new DestroyEntityCommandEgressTranslator(participant, eventBus, entityMap, localNodeId);
            // ⭐ CE-484 — every node may host an operator window: a behaviour fault raised on any node becomes a row of
            //   the Message Log's "Behaviour faults" tab here. 📄 DESIGN_Behaviour_Fault_And_Teardown.md §4c
            yield return new Hrot.Network.Translators.BehaviorFaultIngressTranslator(participant, localNodeId);
            // ⭐ CE-3250 (behaviors' BR-3, R-2031) — a report up the chain of command: raised on any node (a staff on a brain node, a
            //   synthetic one over HTTP), every other node takes it in. 📄 DESIGN_Building_Clearing.md §3, §5b.
            yield return new Hrot.Network.Translators.SquadReportEgressTranslator(participant, entityMap, localNodeId);
            yield return new Hrot.Network.Translators.SquadReportIngressTranslator(participant, entityMap, localNodeId);
        }
    }
}
