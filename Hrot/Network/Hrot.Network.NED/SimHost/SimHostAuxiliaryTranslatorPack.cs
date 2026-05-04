using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common;

namespace Hrot.Network.NED.SimHost;

/// <summary>
/// Factory for simulator-host-specific DDS translators that are outside the shared
/// NedReplicationModule packs due to domain or layer constraints.
///
/// <para><b>Included translators:</b></para>
/// <list type="bullet">
///   <item>Time-sync / lockstep translators (FDP.Toolkit.Time).</item>
///   <item>Combat egress/ingress translators (WeaponFire*, MunitionDetonation*, combat events).</item>
///   <item>Mission-control CQRS translators (MissionControlIngress, MissionControlAckEgress).</item>
/// </list>
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
    public static List<IDescriptorTranslator> Create(
        DdsParticipant   participant,
        NetworkEntityMap entityMap,
        FdpEventBus      eventBus,
        int              localNodeId,
        NodeRole         role)
    {
        var translators = new List<IDescriptorTranslator>();

        // ── Time sync ──────────────────────────────────────────────────────
        // CGF1-A.1: Bridge SwitchTimeModeEvent between FdpEventBus and DDS for
        // distributed time-mode switching (SlaveSyncController ingress).
        translators.Add(Fdp.Toolkit.Time.TimeNetworkModule.CreateDescriptorTranslator(
            participant, eventBus));

        // Bridge FrameOrder/FrameAck for distributed lockstep stepping (slave side).
        translators.Add(Fdp.Toolkit.Time.TimeNetworkModule.CreateSlaveLockstepTranslator(
            participant, eventBus, localNodeId));

        // NTP slave sync: receive TimeSyncRequest/Response from master, publish into bus.
        translators.Add(Fdp.Toolkit.Time.TimeNetworkModule.CreateSlaveTimeSyncTranslator(
            participant, eventBus, localNodeId));

        // ── Mission control CQRS ───────────────────────────────────────────
        if (role.HasFlag(NodeRole.Brain))
        {
            // PACK-P001: mission control ingress polls DDS, egress writes ACKs.
            translators.Add(new MissionControlIngressTranslator(participant));
            translators.Add(new MissionControlAckEgressTranslator(participant));
            // Tactical intent: egress from Commander Brain, ingress on subordinate Brain.
            translators.Add(new TacticalIntentEgressTranslator(participant, entityMap));
            translators.Add(new TacticalIntentIngressTranslator(participant, entityMap));
            // EQS area-query pipeline (Brain side).
            translators.Add(new AreaQueryBrainEgressTranslator(participant, entityMap, localNodeId));
            translators.Add(new AreaQueryBrainIngressTranslator(participant, entityMap, localNodeId));
        }

        // ── Combat egress — Brain / AllInOne emits WeaponFireIntent → DDS ──
        if (role.HasFlag(NodeRole.Brain))
        {
            translators.Add(new WeaponFireIntentEgressTranslator(participant, entityMap));
            // Brain (authority node): receives EntityHitDamage → applies health changes.
            translators.Add(new EntityHitDamageIngressTranslator(participant, entityMap));
        }

        // ── Combat egress — Muscle / AllInOne emits notifications and receives requests ──
        if (role.HasFlag(NodeRole.MuscleGround))
        {
            translators.Add(new WeaponFireNotificationEgressTranslator(participant, entityMap));
            translators.Add(new MunitionDetonationEgressTranslator(participant, entityMap));
            translators.Add(new DamageAssessedEgressTranslator(participant, entityMap));
            translators.Add(new AudioTargetDetectedEgressTranslator(participant, entityMap));
            translators.Add(new WeaponFireRequestIngressTranslator(participant, entityMap));
            translators.Add(new MunitionDetonationIngressTranslator(participant, entityMap));
            // EQS area-query pipeline (Muscle side).
            translators.Add(new AreaQueryMuscleIngressTranslator(participant, entityMap));
            translators.Add(new AreaQueryMuscleEgressTranslator(participant, entityMap));
        }

        return translators;
    }
}
