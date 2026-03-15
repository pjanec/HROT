using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Services;

namespace Bagira.SimHost.Network
{
    /// <summary>
    /// Factory for the Perception-Solver-side translator set.
    ///
    /// <para>Translators created:</para>
    /// <list type="bullet">
    ///   <item><see cref="SensorConfigIngressTranslator"/>         — Brain → Solver: populates sensor component from DDS.</item>
    ///   <item><see cref="RaycastBatchSolverIngressTranslator"/>   — Brain → Solver: receives raycast batch for resolution.</item>
    ///   <item><see cref="SensorTargetsEgressTranslator"/>         — Solver → Brain: publishes computed sensor targets.</item>
    ///   <item><see cref="RaycastBatchSolverEgressTranslator"/>    — Solver → Brain: publishes resolved raycast hits.</item>
    /// </list>
    ///
    /// <para>Install on <see cref="NodeRole.Perception"/> and <see cref="NodeRole.AllInOne"/> nodes.</para>
    /// </summary>
    public static class SimPerceptionTranslatorPack
    {
        /// <summary>Creates the Perception-Solver-side translator instances.</summary>
        public static IEnumerable<IDescriptorTranslator> Create(
            DdsParticipant       participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform)
        {
            yield return new SensorConfigIngressTranslator(participant, entityMap);
            yield return new RaycastBatchSolverIngressTranslator(participant, entityMap);
            yield return new SensorTargetsEgressTranslator(participant, entityMap);
            yield return new RaycastBatchSolverEgressTranslator(participant, entityMap);
        }
    }
}
