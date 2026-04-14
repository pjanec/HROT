using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Hrot.Core.Network;

namespace Hrot.Network.NED.IG;

/// <summary>
/// NED implementation of <see cref="IIgTranslators"/>.
/// Creates all NED IG ingress translators for the given session context.
/// </summary>
public sealed class NedIgTranslators : IIgTranslators
{
    public IReadOnlyList<IDescriptorTranslator> GetTranslators(
        DdsParticipant participant,
        NetworkEntityMap entityMap,
        FdpEventBus bus,
        GhostCreationSystem? ghostCreationSystem,
        long localNodeId,
        bool headless)
    {
        var translators = new List<IDescriptorTranslator>();

        if (ghostCreationSystem != null)
        {
            translators.Add(new IgMissionIngressTranslator(
                participant, entityMap, ghostCreationSystem, localNodeId));
            translators.Add(new GroundClampingOverrideTranslator(
                participant, entityMap));
            translators.Add(new AudioTargetDetectedIngressTranslator(
                participant, entityMap));
            translators.Add(new WeaponFireIngressTranslator(
                participant, entityMap));
            translators.Add(new ContextActionsUpdateTranslator(
                participant, entityMap, bus, ghostCreationSystem, localNodeId));
        }

        return translators;
    }
}
