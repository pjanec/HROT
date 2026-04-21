using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common;
using Hrot.Core.Network;
using Fdp.ModuleHost;
using Fdp.Network.Cyclone.Modules;
using Fdp.Network.Cyclone.Systems;

namespace Hrot.Network.NED.SimHost;

/// <summary>
/// NED implementation of <see cref="ISimHostAuxiliaryTranslators"/>.
/// Wraps <see cref="SimHostAuxiliaryTranslatorPack"/> and registers all
/// DDS translator systems on the given kernel.
/// </summary>
internal sealed class NedSimHostAuxiliaryTranslators : ISimHostAuxiliaryTranslators
{
    private readonly List<IDescriptorTranslator> _translators;

    public NedSimHostAuxiliaryTranslators(
        DdsParticipant   participant,
        NetworkEntityMap entityMap,
        FdpEventBus      eventBus,
        int              localNodeId,
        NodeRole         role)
    {
        _translators = SimHostAuxiliaryTranslatorPack.Create(
            participant, entityMap, eventBus, localNodeId, role);
    }

    public void RegisterOn(ModuleHostKernel kernel)
    {
        var ingress = new System.Collections.Generic.List<IDescriptorTranslator>(_translators.Count);
        var egress  = new System.Collections.Generic.List<IDescriptorTranslator>(_translators.Count);
        foreach (var t in _translators)
        {
            if ((t.Direction & TranslatorDirection.Ingress) != 0) ingress.Add(t);
            if ((t.Direction & TranslatorDirection.Egress)  != 0) egress.Add(t);
        }
        kernel.RegisterGlobalSystem(new CycloneNetworkIngressSystem(ingress.ToArray()));
        kernel.RegisterGlobalSystem(new CycloneEgressSystem(egress.ToArray()));
        kernel.RegisterGlobalSystem(new CycloneNetworkCleanupSystem(_translators));
    }

    public void Dispose() { }
}
