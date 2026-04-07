using System.Collections.Generic;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;

namespace Hrot.Network.Translators
{
    /// <summary>
    /// Factory for the cognitive (Brain-side) translator set.
    ///
    /// <para><b>Translators created (in DDS ordinal order):</b></para>
    /// <list type="number">
    ///   <item><see cref="NavigationIntentEgressTranslator"/>    — publishes <c>NavigationIntent</c> commands for owned entities (ordinal 52).</item>
    ///   <item><see cref="EntityMissionEgressTranslator"/>       — publishes mission plans for owned entities (ordinal 51).</item>
    ///   <item><see cref="GeoSpatialIngressTranslator"/>         — receives position updates for ghost entities.</item>
    ///   <item><see cref="NavigationStatusIngressTranslator"/>   — receives nav status feedback from Muscle nodes (ordinal 53).</item>
    /// </list>
    ///
    /// <para>Install on <see cref="NodeRole.Brain"/> and <see cref="NodeRole.AllInOne"/> nodes.</para>
    /// </summary>
    public static class CognitiveTranslatorPack
    {
        /// <summary>
        /// Creates the cognitive translator instances.
        /// </summary>
        /// <param name="participant">Live DDS participant; passed to all translator constructors.</param>
        /// <param name="entityMap">Shared <see cref="NetworkEntityMap"/> for entity lookups.</param>
        /// <param name="geoTransform">
        ///   Geodetic transform used by <see cref="NavigationIntentEgressTranslator"/> to convert
        ///   Cartesian waypoints to WGS-84 <c>GeoPosition</c>, and by <see cref="GeoSpatialIngressTranslator"/>
        ///   to convert received coordinates back to Cartesian.
        /// </param>
        /// <param name="doctrineRegistry">
        ///   Doctrine registry forwarded to <see cref="EntityMissionEgressTranslator"/>
        ///   for mission-plan serialisation. May be <c>null</c> when running without a doctrine layer.
        /// </param>
        /// <param name="ghostCreationSystem">
        ///   Ghost-creation helper injected into <see cref="GeoSpatialIngressTranslator"/>
        ///   so it can lazily create Cartesian ghost entities for remote vehicles.
        /// </param>
        public static IEnumerable<IDescriptorTranslator> Create(
            DdsParticipant       participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform,
            DoctrineRegistry?    doctrineRegistry,
            GhostCreationSystem  ghostCreationSystem)
        {
            yield return new NavigationIntentEgressTranslator(participant, entityMap, geoTransform);
            yield return new EntityMissionEgressTranslator(participant, entityMap, doctrineRegistry);
            yield return new GeoSpatialIngressTranslator(participant, entityMap, geoTransform, ghostCreationSystem);
            yield return new NavigationStatusIngressTranslator(participant, entityMap);
        }
    }
}
