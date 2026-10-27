using System.Collections.Generic;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;

namespace Hrot.Map.Common.Translators
{
    /// <summary>
    /// Factory for the kinematic (Muscle-side) translator set.
    ///
    /// <para><b>Translators created (in DDS ordinal order):</b></para>
    /// <list type="number">
    ///   <item><see cref="NavigationStatusEgressTranslator"/>    — publishes nav completion status for owned entities (ordinal 53).</item>
    ///   <item><see cref="NavigationIntentIngressTranslator"/>   — receives nav commands from a Brain node (ordinal 52).</item>
    /// </list>
    ///
    /// <para>
    /// <b>Note:</b> <see cref="GeoSpatialEgressTranslator"/> (WorldPos, ordinal 2) was moved to
    /// <see cref="SharedTranslatorPack"/> so that Brain nodes can also publish the initial entity
    /// position before delegating physics authority to the Muscle.
    /// <see cref="MapVisualOverlayEgressTranslator"/> was likewise moved to
    /// <see cref="SharedTranslatorPack"/> so Brain nodes (CGF) can publish overlays for area
    /// entities they own. <see cref="MapRouteEgressTranslator"/> moved there too (CE-2171): CGF owns scenario routes.
    /// </para>
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
        /// <param name="ghostCreationSystem">
        ///   Ghost-creation helper injected into <see cref="NavigationIntentIngressTranslator"/>.
        /// </param>
        /// <param name="localNodeId">Local node identifier; embedded in FdpLog messages.</param>
        public static IEnumerable<IDescriptorTranslator> Create(
            DdsParticipant       participant,
            NetworkEntityMap     entityMap,
            GhostCreationSystem? ghostCreationSystem = null,
            long                 localNodeId = 0)
        {
            yield return new NavigationStatusEgressTranslator(participant, entityMap, localNodeId);
            yield return new NavigationIntentIngressTranslator(participant, entityMap, localNodeId);
        }
    }
}
