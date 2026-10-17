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
        // ⭐ CE-1148 S3 (docs/DESIGN_Node_Roles_And_Policies.md §3.5) — the order egress (CE-3169) is no longer built here: the IG
        //   builds the auxiliary pack like every node (IgNodeBootstrapper), and the pack gives every role that egress.
        var translators = new List<INetworkTranslator>();

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
