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

        // ── Mission control CQRS ───────────────────────────────────────────
        if (role.HasFlag(NodeRole.Brain))
        {
            // PACK-P001: mission control ingress polls DDS, egress writes ACKs.
            translators.Add(new MissionControlIngressTranslator(participant));
            translators.Add(new MissionControlAckEgressTranslator(participant));
            // Tactical intent: egress from Commander Brain, ingress on subordinate Brain.
            translators.Add(new TacticalIntentEgressTranslator(participant, entityMap));
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
        }

        // ── Combat egress — Muscle / AllInOne emits notifications and receives requests ──
        if (role.HasFlag(NodeRole.MuscleGround))
        {
            translators.Add(new WeaponFireNotificationEgressTranslator(participant, entityMap));
            translators.Add(new MunitionDetonationEgressTranslator(participant, entityMap));
            translators.Add(new DamageAssessedEgressTranslator(participant, entityMap));
            translators.Add(new NearMissEgressTranslator(participant, entityMap));   // CE-3064
            translators.Add(new WeaponFireRequestIngressTranslator(participant, entityMap));
            translators.Add(new MunitionDetonationIngressTranslator(participant, entityMap));
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

        // ── Sensor inputs for a Perception node WITHOUT MuscleGround (CE-3065, S7 design §3a) ──
        //    Heat and sound are derived where the sensor solves (EqsSolverStartup.PopulateSystems). Positions replicate
        //    everywhere; shots and detonations are events, so a node that does not fire them itself must read them back.
        //    ⛔ Not with MuscleGround: there the shots are already local events, and its own samples would loop back
        //    through the reader and heat each shooter twice. Both arrive IsRemote = true; heat and sound accept them.
        if (role.HasFlag(NodeRole.Perception) && !role.HasFlag(NodeRole.MuscleGround))
        {
            translators.Add(new Hrot.Network.NED.IG.WeaponFireIngressTranslator(participant, entityMap));
            translators.Add(new MunitionDetonationIngressTranslator(participant, entityMap));
        }

        return translators;
    }
}
