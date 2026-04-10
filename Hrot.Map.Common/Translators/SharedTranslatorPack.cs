using System.Collections.Generic;
using Hrot.Map.Common.Replication;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;

namespace Hrot.Map.Common.Translators
{
    /// <summary>
    /// Factory for the shared translator set that all <see cref="NodeRole"/> values
    /// install regardless of specialisation.
    ///
    /// <para><b>Translators created (in DDS ordinal order):</b></para>
    /// <list type="number">
    ///   <item><see cref="EntityMasterEgressTranslator"/>  — publishes entity births/deaths (ordinal 0).</item>
    ///   <item><see cref="EntityMasterIngressTranslator"/> — ghost-creates remote entities (ordinal -2).</item>
    ///   <item><see cref="EntityInfoEgressTranslator"/>    — publishes entity metadata such as affiliation (ordinal 21).</item>
    ///   <item><see cref="FireInteractionEventTranslator"/>— bidirectional fire-interaction events (Muscle egress / IG ingress).</item>
    ///   <item><see cref="EntityDamageEgressTranslator"/>  — publishes health changes to IG/ExCon (ordinal 30).</item>
    ///   <item><see cref="GeoSpatialEgressTranslator"/>    — publishes position/velocity for owned entities (ordinal 2); moved here
    ///     from <see cref="KinematicTranslatorPack"/> so Brain nodes can broadcast the initial WorldPos before
    ///     handing physics authority to the Muscle.</item>
    ///   <item><see cref="OwnershipUpdateTranslator"/>     — bidirectional authority-handover notification (Muscle egress / Brain ingress).</item>
    /// </list>
    /// </summary>
    public static class SharedTranslatorPack
    {
        /// <summary>
        /// Creates the shared translator instances.
        /// </summary>
        /// <param name="participant">Live DDS participant; passed to all translator constructors.</param>
        /// <param name="entityMap">Shared <see cref="NetworkEntityMap"/> for egress/ingress lookups.</param>
        /// <param name="localNodeId">Local node identifier; needed by <see cref="EntityMasterEgressTranslator"/>.</param>
        /// <param name="eventBus">
        ///   Application event bus; forwarded to <see cref="EntityMasterIngressTranslator"/>
        ///   so it can publish <c>DestroyEntityCommand</c> on entity teardown.
        /// </param>
        /// <param name="ghostCreationSystem">
        ///   Ghost-creation helper; used by <see cref="EntityMasterIngressTranslator"/>
        ///   to materialise replicas of remote entities.
        /// </param>
        /// <param name="geoTransform">
        ///   Geodetic coordinate transform; passed to <see cref="GeoSpatialEgressTranslator"/>
        ///   so Brain nodes can broadcast the initial WorldPos before delegating physics authority.
        /// </param>
        public static IEnumerable<IDescriptorTranslator> Create(
            DdsParticipant       participant,
            NetworkEntityMap     entityMap,
            long                 localNodeId,
            FdpEventBus          eventBus,
            GhostCreationSystem  ghostCreationSystem,
            IGeographicTransform geoTransform)
        {
            yield return new EntityMasterEgressTranslator(participant, entityMap, localNodeId);
            yield return new EntityMasterIngressTranslator(participant, entityMap, localNodeId, eventBus, ghostCreationSystem);
            yield return new EntityInfoEgressTranslator(participant, entityMap);
            yield return new FireInteractionEventTranslator(participant, entityMap);
            yield return new EntityDamageEgressTranslator(participant, entityMap);
            yield return new GeoSpatialEgressTranslator(participant, entityMap, geoTransform);
            yield return new OwnershipUpdateTranslator(participant, (int)localNodeId);
        }
    }
}
