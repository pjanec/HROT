using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Factory for the Perception-Solver-side translator set.
    ///
    /// <para>Translators created:</para>
    /// <list type="bullet">
    ///   <item><see cref="SensorConfigIngressTranslator"/>         — Brain → Solver: populates sensor component from DDS.</item>
    ///   <item><see cref="RaycastBatchSolverIngressTranslator"/>   — Brain → Solver: receives raycast batch for resolution.</item>
    ///   <item><see cref="SensorTrackStateEgressTranslator"/>      — Solver → Brain: discrete contact acquired/lost events.</item>
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
            IGeographicTransform geoTransform,
            GhostCreationSystem  ghostCreationSystem)
        {
            yield return new SensorConfigIngressTranslator(participant, entityMap, ghostCreationSystem);
            yield return new RaycastBatchSolverIngressTranslator(participant, entityMap, geoTransform);
            yield return new SensorTrackStateEgressTranslator(participant, entityMap);
        }
    }
}
