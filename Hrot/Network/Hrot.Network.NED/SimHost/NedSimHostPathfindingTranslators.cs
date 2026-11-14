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
using Fdp.Core;

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

        // ⭐ Macros S3a ⑤ (SR-180) — the placement query needs NO trajectory pool, so it is gated by ROLE alone. 📌 Measured live
        //   2026-11-02 (--mode all): CGF composes this set WITHOUT a pool, so with the placement pair inside the pool-gated packs the
        //   Brain never sent a request and every expansion fell back to its own terrain (E-6) after 300 frames.
        if (role.HasFlag(NodeRole.Brain))
        {
            _translators.Add(new PlacementRequestBrainEgressTranslator(participant, localNodeId));
            _translators.Add(new PlacementResponseBrainIngressTranslator(participant, localNodeId));
        }
        if (role.HasFlag(NodeRole.NavigationSolver))
        {
            _translators.Add(new PlacementRequestSolverIngressTranslator(participant));
            _translators.Add(new PlacementResponseSolverEgressTranslator(participant));
        }
    }

    public void RegisterOn(ModuleHostKernel kernel)
    {
        if (_translators.Count == 0) return;
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
