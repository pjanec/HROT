using System.Collections.Generic;
using CarKinem.Trajectory;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Factory for the Brain-side pathfinding translator set.
    ///
    /// <para>Translators created:</para>
    /// <list type="bullet">
    ///   <item><see cref="PathRequestBrainEgressTranslator"/>    — Brain -> NavigationSolver: path request batch.</item>
    ///   <item><see cref="PathResponseBrainIngressTranslator"/>  — NavigationSolver -> Brain: computed path results.</item>
    /// </list>
    ///
    /// <para>Install on <see cref="NodeRole.Brain"/> and <see cref="NodeRole.AllInOne"/> nodes.</para>
    /// </summary>
    public static class BrainPathfindingTranslatorPack
    {
        /// <summary>Creates the Brain-side pathfinding translator instances.</summary>
        public static IEnumerable<IDescriptorTranslator> Create(
            DdsParticipant       participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform,
            TrajectoryPoolManager trajectoryPool,
            int                  localNodeId = 0)
        {
            yield return new PathRequestBrainEgressTranslator(participant, entityMap, geoTransform, localNodeId);
            yield return new PathResponseBrainIngressTranslator(participant, entityMap, geoTransform, trajectoryPool, localNodeId);
        }
    }
}
