using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common;
using Hrot.Core.Network;
using Fdp.ModuleHost;
using Fdp.Network.Cyclone.Modules;
using Fdp.Network.Cyclone.Systems;

namespace Hrot.Network.NED.SimHost;

/// <summary>
/// NED implementation of <see cref="ISimHostPerceptionTranslators"/>.
/// Wraps <see cref="BrainPerceptionTranslatorPack"/> and <see cref="SimPerceptionTranslatorPack"/>
/// and registers all DDS translator systems on the given kernel.
/// </summary>
internal sealed class NedSimHostPerceptionTranslators : ISimHostPerceptionTranslators
{
    private readonly List<IDescriptorTranslator> _translators = new();

    public NedSimHostPerceptionTranslators(
        DdsParticipant       participant,
        NetworkEntityMap     entityMap,
        IGeographicTransform geoTransform,
        NodeRole             role,
        int                  localNodeId = 0)
    {
        if (role.HasFlag(NodeRole.Brain))
            _translators.AddRange(BrainPerceptionTranslatorPack.Create(participant, entityMap, geoTransform, localNodeId));
        if (role.HasFlag(NodeRole.Perception))
            _translators.AddRange(SimPerceptionTranslatorPack.Create(participant, entityMap, geoTransform));
    }

    public void RegisterOn(ModuleHostKernel kernel)
    {
        if (_translators.Count == 0) return;
        kernel.RegisterGlobalSystem(new CycloneNetworkIngressSystem(_translators.ToArray()));
        kernel.RegisterGlobalSystem(new CycloneEgressSystem(_translators.ToArray()));
        kernel.RegisterGlobalSystem(new CycloneNetworkCleanupSystem(_translators));
    }

    public void Dispose() { }
}

