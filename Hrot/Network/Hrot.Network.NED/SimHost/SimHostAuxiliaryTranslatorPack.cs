using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common;
using Hrot.Network.NED.CGF;

namespace Hrot.Network.NED.SimHost;

/// <summary>
/// Factory for simulator-host-specific DDS translators that are outside the shared
/// NedReplicationModule packs due to domain or layer constraints.
///
/// <para><b>Included translators:</b></para>
/// <list type="bullet">
///   <item>Combat egress/ingress translators (WeaponFire*, MunitionDetonation*, combat events).</item>
///   <item>Mission-control CQRS translators (MissionControlIngress, MissionControlAckEgress).</item>
///   <item>EQS area-query translators (Brain and Muscle sides).</item>
/// </list>
///
/// <para>
/// <b>Note:</b> Time-sync and lockstep translators are registered by
/// <c>SharedApplicationBootstrapper.Phase6c</c> and are not included here
/// to avoid double-registration.
/// </para>
///
/// <para>⭐ <c>CE-1148</c> (R-3015) — EVERY NED host builds this pack (CGF, SimHost, Stride, and since S3 the IG); the role gates
/// below decide what each one takes. ⛔ The combat event pairs listed above moved to <c>SharedTranslatorPack</c> in S2.</para>
///
/// <para>
/// <see cref="SimHostApp.OnLoad"/> calls <see cref="Create"/> after building the
/// <c>HrotNodeContext</c> and registers the resulting translators via
/// <c>CycloneNetworkIngressSystem</c> + <c>CycloneEgressSystem</c> alongside
/// the packs bundled by <c>NedReplicationModule</c>.
/// </para>
/// </summary>
public static class SimHostAuxiliaryTranslatorPack
{
    /// <summary>
    /// Creates the role-filtered auxiliary translator set.
    /// </summary>
    /// <param name="participant">Live DDS participant.</param>
    /// <param name="entityMap">Shared network entity map.</param>
    /// <param name="eventBus">Application event bus (required for time-sync translators).</param>
    /// <param name="localNodeId">Local DDS node identifier (required for lockstep translator).</param>
    /// <param name="role">Node role; used to gate combat translators by simulation authority.</param>
    /// <param name="clusterCache">⭐ R-179: where the Brain picks each EQS sensor's solver (least-loaded Perception node).
    /// ⛔ A production host that has one must pass it — without it no solver is named and every Perception node
    /// solves.</param>
    public static List<IDescriptorTranslator> Create(
        DdsParticipant   participant,
        NetworkEntityMap entityMap,
        FdpEventBus      eventBus,
        int              localNodeId,
        NodeRole         role,
        Hrot.Network.Routing.IClusterStateCache? clusterCache = null)
    {
        var translators = new List<IDescriptorTranslator>();

        // ⭐⭐ CE-3167 (docs/DESIGN_Entity_Control.md §2.3, R-271 (backend)) — an ORDER may be issued on ANY node (an operator on a
        //   map host, the AI player over HTTP), so the tactical-intent egress runs on every node, not only a Brain: it forwards
        //   exactly the intents for units this node does not own (HasAuthority<BehaviorState> gate). 🔴 It was Brain-only, so an
        //   order published on a SimHost / map node reached nobody. A Brain node's egress still carries a commander's intents.
        translators.Add(new TacticalIntentEgressTranslator(participant, entityMap));

        // ── Mission control CQRS ───────────────────────────────────────────
        if (role.HasFlag(NodeRole.Brain))
        {
            // PACK-P001: mission control ingress polls DDS, egress writes ACKs.
            translators.Add(new MissionControlIngressTranslator(participant));
            translators.Add(new MissionControlAckEgressTranslator(participant));
            // Tactical intent: ingress on the brain that owns the unit (the egress is below, on every node).
            translators.Add(new TacticalIntentIngressTranslator(participant, entityMap));
            // EQS pipeline — Brain side.
            translators.Add(new EqsSensorConfigEgressTranslator(participant, entityMap, clusterCache, localNodeId));
            translators.Add(new EqsResultIngressTranslator(participant, entityMap));
            translators.Add(new DangerAreaResultIngressTranslator(participant, entityMap));   // ⭐ CE-3072 B3
        }

        // ── Combat egress — Brain / AllInOne emits WeaponFireIntent → DDS ──
        if (role.HasFlag(NodeRole.Brain))
        {
            translators.Add(new WeaponFireIntentEgressTranslator(participant, entityMap));
            // Brain (authority node): receives EntityHitDamage → applies health changes.
            translators.Add(new EntityHitDamageIngressTranslator(participant, entityMap));
            // ⭐ CE-3062 — what the unit HEARD (anonymous), into the Brain's memory (CE-3063).
            translators.Add(new AudioTargetDetectedIngressTranslator(participant, entityMap));
            // ⭐ CE-3064 — a round passed close to one of its units (R-206).
            translators.Add(new NearMissIngressTranslator(participant, entityMap));
            // ⭐ CE-2189 (DESIGN_Missiles_And_Ammo §3c) — a missile launched at one of its aircraft in. (⛔ CE-2188's flare / chaff ask is
            //   an ordinary WeaponFireRequest now — BS-1-DESIGN §5.4c.)
            translators.Add(new MissileWarningIngressTranslator(participant, entityMap));
        }

        // ⭐ CE-4024 (AQ93 §15 K7) — the TRAFFIC node takes the hits too: a hit on a SUMO car or pedestrian is addressed to the replica the
        //   traffic node owns, and its TrafficPanicSystem turns it into a conversion. 📌 Measured 2026-10-17 on the cluster: Brain-only, so
        //   a burst did 2 889 damage to a SUMO car on SimHost and the traffic node never heard of it. ⛔ Harmless for everything else:
        //   HealthApplicationSystem is authority-gated, and the traffic node owns no one's health but its replicas'.
        if (role.HasFlag(NodeRole.Traffic) && !role.HasFlag(NodeRole.Brain))
            translators.Add(new EntityHitDamageIngressTranslator(participant, entityMap));
        // ⭐ … and SENDS one: the hit it held for a converted agent is re-published to our new entity as its own, and must reach that
        //   entity's brain owner. 📌 Measured 2026-10-18: Muscle-only, so a converted car stood on CGF at 300/300 after an 863-damage hit.
        if (role.HasFlag(NodeRole.Traffic) && !role.HasFlag(NodeRole.MuscleGround))
            translators.Add(new DamageAssessedEgressTranslator(participant, entityMap));

        // ── Combat egress — Muscle / AllInOne emits notifications and receives requests ──
        if (role.HasFlag(NodeRole.MuscleGround))
        {
            // (⭐ CE-1148 S2 — the WeaponFire / MunitionDetonation / RoundImpact pairs are in SharedTranslatorPack, on every node.)
            translators.Add(new DamageAssessedEgressTranslator(participant, entityMap));
            translators.Add(new NearMissEgressTranslator(participant, entityMap));   // CE-3064
            translators.Add(new MissileWarningEgressTranslator(participant, entityMap));          // ⭐ CE-2189
            translators.Add(new WeaponFireRequestIngressTranslator(participant, entityMap));
        }

        // ── EQS pipeline — solver side. ⭐ R-179: keyed on PERCEPTION, the role whose capability registers the solver
        //    (EQS design §17.3) and the role the Brain picks from; MuscleGround kept so no host that solves today stops.
        if (role.HasFlag(NodeRole.Perception) || role.HasFlag(NodeRole.MuscleGround))
        {
            translators.Add(new EqsSensorConfigIngressTranslator(participant, entityMap, localNodeId));
            translators.Add(new EqsResultEventEgressTranslator(participant, entityMap));
            translators.Add(new DangerAreaResultEgressTranslator(participant, entityMap));    // ⭐ CE-3072 B3
            // ⭐ CE-3065 — what a unit HEARD leaves from the node that SOLVED it (the memory stage runs with the solver),
            //   so it is keyed like the EQS result above, not on MuscleGround.
            translators.Add(new AudioTargetDetectedEgressTranslator(participant, entityMap));
        }

        // ⭐⭐ CE-1148 S2 (Node Roles §3.5, R-3015) — the two branches that were here ("Perception && !MuscleGround" for heat and sound,
        //   CE-3065; "Brain && !MuscleGround && !Perception" for a fire-mission observer's burst memory, IF-3) existed only because a
        //   node would re-ingest its own shots and bursts. The pairs now sit in SharedTranslatorPack on every node, and the ingress drops
        //   this node's own samples by SourceNodeId, so neither exclusion is needed.

        return translators;
    }
}
