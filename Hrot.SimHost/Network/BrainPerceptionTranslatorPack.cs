using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Services;

namespace Hrot.SimHost.Network
{
    /// <summary>
    /// Factory for the Brain-side perception translator set.
    ///
    /// <para>Translators created:</para>
    /// <list type="bullet">
    ///   <item><see cref="SensorConfigEgressTranslator"/>      — Brain → Perception Solver: sensor configuration.</item>
    ///   <item><see cref="RaycastBatchEgressTranslator"/>      — Brain → Perception Solver: raycast request batch.</item>
    ///   <item><see cref="SensorTargetsIngressTranslator"/>    — Perception Solver → Brain: perceived targets.</item>
    ///   <item><see cref="RaycastBatchIngressTranslator"/>     — Perception Solver → Brain: raycast results.</item>
    /// </list>
    ///
    /// <para>Install on <see cref="NodeRole.Brain"/> and <see cref="NodeRole.AllInOne"/> nodes.</para>
    /// </summary>
    public static class BrainPerceptionTranslatorPack
    {
        /// <summary>Creates the Brain-side perception translator instances.</summary>
        public static IEnumerable<IDescriptorTranslator> Create(
            DdsParticipant       participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform)
        {
            yield return new SensorConfigEgressTranslator(participant, entityMap, geoTransform);
            yield return new RaycastBatchEgressTranslator(participant, entityMap, geoTransform);
            yield return new SensorTargetsIngressTranslator(participant, entityMap);
            yield return new RaycastBatchIngressTranslator(participant, entityMap);
        }
    }
}
