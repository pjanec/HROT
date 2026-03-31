using System.Collections.Generic;
using Hrot.Map.Common.Replication.Egress;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Services;

namespace Hrot.SimHost.Network
{
    /// <summary>
    /// Factory for the kinematic (Muscle-side) translator set.
    ///
    /// <para><b>Translators created (in DDS ordinal order):</b></para>
    /// <list type="number">
    ///   <item><see cref="GeoSpatialEgressTranslator"/>          — publishes position/velocity for owned entities.</item>
    ///   <item><see cref="NavigationStatusEgressTranslator"/>    — publishes nav completion status for owned entities (ordinal 53).</item>
    ///   <item><see cref="NavigationIntentIngressTranslator"/>   — receives nav commands from a Brain node (ordinal 52).</item>
    /// </list>
    ///
    /// <para>Install on <see cref="NodeRole.MuscleGround"/> and <see cref="NodeRole.AllInOne"/> nodes.</para>
    /// </summary>
    public static class KinematicTranslatorPack
    {
        /// <summary>
        /// Creates the kinematic translator instances.
        /// </summary>
        /// <param name="participant">Live DDS participant; passed to all translator constructors.</param>
        /// <param name="entityMap">Shared <see cref="NetworkEntityMap"/> for entity lookups.</param>
        /// <param name="geoTransform">
        ///   Geodetic transform used by <see cref="NavigationIntentIngressTranslator"/>
        ///   to convert incoming WGS-84 waypoints back to Cartesian <c>Vector2</c>.
        /// </param>
        public static IEnumerable<IDescriptorTranslator> Create(
            DdsParticipant       participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform)
        {
            yield return new GeoSpatialEgressTranslator(participant, entityMap, geoTransform);
            yield return new NavigationStatusEgressTranslator(participant, entityMap);
            yield return new NavigationIntentIngressTranslator(participant, entityMap, geoTransform);
        }
    }
}
