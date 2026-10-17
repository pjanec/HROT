using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Hrot.Core.Network;
using Hrot.Network.NED.SimHost;

namespace Hrot.Network.NED.IG;

/// <summary>
/// NED implementation of <see cref="IIgTranslators"/>.
/// Creates all NED IG ingress translators for the given session context.
/// </summary>
public sealed class NedIgTranslators : IIgTranslators
{
    public IReadOnlyList<INetworkTranslator> GetTranslators(
        DdsParticipant participant,
        NetworkEntityMap entityMap,
        FdpEventBus bus,
        GhostCreationSystem? ghostCreationSystem,
        long localNodeId,
        bool headless)
    {
        var translators = new List<INetworkTranslator>
        {
            // ⭐ CE-3169 (DESIGN_Entity_Control §2.3, §9) — an order given on THIS station (the map menu, the RTS tool, HTTP) is an
            //   AssignTacticalIntentEvent on the IG's bus; the brain owner is elsewhere, so it must leave as TacticalIntentRequest.
            //   📐 Every other node gets this egress from SimHostAuxiliaryTranslatorPack, which the IG never builds (no host builds
            //   both packs, so it is never doubled).
            new TacticalIntentEgressTranslator(participant, entityMap),
        };

        if (ghostCreationSystem != null)
        {
            translators.Add(new IgMissionIngressTranslator(
                participant, entityMap, ghostCreationSystem, localNodeId));
            translators.Add(new GroundClampingOverrideTranslator(
                participant, entityMap));
            // ⛔ CE-3062 — the IG's AudioTargetDetected ingress is retired: nothing on the IG read what it published. The topic
            //   is the Brain's now (SimHostAuxiliaryTranslatorPack, AudioTargetDetectedIngressTranslator).
            // (⭐ CE-1148 S2 — the WeaponFire / MunitionDetonation / RoundImpact ingress is in SharedTranslatorPack, on every node.)
            translators.Add(new ContextActionsUpdateTranslator(
                participant, entityMap, bus, ghostCreationSystem, localNodeId));
        }

        return translators;
    }
}
