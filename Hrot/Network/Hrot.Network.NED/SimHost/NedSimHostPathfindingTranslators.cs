using System;
using System.Collections.Generic;
using CarKinem.Trajectory;
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
/// NED implementation of <see cref="ISimHostPathfindingTranslators"/>.
/// Wraps <see cref="BrainPathfindingTranslatorPack"/> and <see cref="SimPathfindingTranslatorPack"/>
/// and registers all DDS translator systems on the given kernel.
/// </summary>
internal sealed class NedSimHostPathfindingTranslators : ISimHostPathfindingTranslators
{
    private readonly List<IDescriptorTranslator> _translators = new();

    public NedSimHostPathfindingTranslators(
        DdsParticipant       participant,
        NetworkEntityMap     entityMap,
        IGeographicTransform geoTransform,
        NodeRole             role,
        TrajectoryPoolManager? trajectoryPool = null,
        int                  localNodeId = 0)
    {
        if (role.HasFlag(NodeRole.Brain) && trajectoryPool != null)
            _translators.AddRange(BrainPathfindingTranslatorPack.Create(participant, entityMap, geoTransform, trajectoryPool, localNodeId));
        if (role.HasFlag(NodeRole.NavigationSolver) && trajectoryPool != null)
            _translators.AddRange(SimPathfindingTranslatorPack.Create(participant, entityMap, geoTransform, trajectoryPool));
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
