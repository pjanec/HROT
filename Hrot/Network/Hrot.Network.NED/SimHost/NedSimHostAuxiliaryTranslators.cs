using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Patching;
using FDP.Toolkit.Replication.Services;
using Hrot.Common;
using Hrot.Core.Network;
using Fdp.ModuleHost_Core;
using Fdp.ModuleHost.Network.Cyclone.Modules;
using Fdp.ModuleHost.Network.Cyclone.Systems;

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
        kernel.RegisterGlobalSystem(new CycloneNetworkIngressSystem(_translators.ToArray()));
        kernel.RegisterGlobalSystem(new CycloneEgressSystem(_translators.ToArray()));
        kernel.RegisterGlobalSystem(new CycloneNetworkCleanupSystem(_translators));
    }

    public void Dispose() { }
}
