using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Services;

namespace Bagira.SimHost.Network
{
    /// <summary>
    /// Factory for the NavigationSolver-side pathfinding translator set.
    ///
    /// <para>Translators created:</para>
    /// <list type="bullet">
    ///   <item><see cref="PathRequestSolverIngressTranslator"/>   — Brain → Solver: receives path requests for resolution.</item>
    ///   <item><see cref="PathResponseSolverEgressTranslator"/>   — Solver → Brain: publishes computed path results.</item>
    /// </list>
    ///
    /// <para>Install on <see cref="NodeRole.NavigationSolver"/> and <see cref="NodeRole.AllInOne"/> nodes.</para>
    /// </summary>
    public static class SimPathfindingTranslatorPack
    {
        /// <summary>Creates the NavigationSolver-side pathfinding translator instances.</summary>
        public static IEnumerable<IDescriptorTranslator> Create(
            DdsParticipant       participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform)
        {
            yield return new PathRequestSolverIngressTranslator(participant, entityMap, geoTransform);
            yield return new PathResponseSolverEgressTranslator(participant, entityMap);
        }
    }
}
