using System.Numerics;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Services;
using Fdp.Modules.Geographic;
using Fdp.ModuleHost.Abstractions;

using EcsNavigationIntent = Fdp.Toolkit.Navigation.NavigationIntent;
using EcsNavMode          = Fdp.Toolkit.Navigation.NavigationMode;

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// Ingress translator: polls the DDS <c>NavigationIntent</c> topic and
    /// updates the ECS <see cref="EcsNavigationIntent"/> component for the
    /// matching entity.
    ///
    /// <para>
    /// The wire <see cref="Hrot.NED.Common.GeoPoint"/> is converted back to a
    /// Cartesian <c>Vector2</c> via <see cref="IGeographicTransform"/>.
    /// Entities not yet registered in the <see cref="NetworkEntityMap"/> are silently skipped.
    /// </para>
    /// </summary>
    public sealed class NavigationIntentIngressTranslator : IDescriptorTranslator
    {
        private readonly DdsReader<Hrot.NED.Descriptors.NavigationIntent> _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly IGeographicTransform _geoTransform;
        private readonly long _localNodeId;

        public string TopicName      => "NavigationIntent";
        public long   DescriptorOrdinal => 52;

        public NavigationIntentIngressTranslator(
            DdsParticipant      dds,
            NetworkEntityMap    entityMap,
            IGeographicTransform geoTransform,
            long localNodeId)
        {
            _reader       = new DdsReader<Hrot.NED.Descriptors.NavigationIntent>(dds, "NavigationIntent");
            _entityMap    = entityMap    ?? throw new System.ArgumentNullException(nameof(entityMap));
            _geoTransform = geoTransform ?? throw new System.ArgumentNullException(nameof(geoTransform));
            _localNodeId  = localNodeId;
        }

        /// <summary>
        /// Polls DDS reader and updates <see cref="EcsNavigationIntent"/> on matching entities.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            using var loan = _reader.Take();

            foreach (var sample in loan)
            {
                if (!sample.IsValid)
                    continue;

                var msg = sample.Data;

                if (!_entityMap.TryGetEntity(msg.EntityId, out var entity))
                {
                    // Entity not yet known — skip silently (no ghost creation for intent).
                    FdpLog<NavigationIntentIngressTranslator>.Debug(
                        "[Node-{0}] NavigationIntent ingress: unknown EntityId={1} — skipped", _localNodeId, msg.EntityId);
                    continue;
                }

                // Convert wire GeoPosition back to Cartesian Vector2.
                var cartesian = _geoTransform.ToCartesian(
                    msg.FinalDestination.Latitude,
                    msg.FinalDestination.Longitude,
                    msg.FinalDestination.Altitude);

                cmd.SetComponent(entity, new EcsNavigationIntent
                {
                    IntentId         = msg.IntentId,
                    Mode             = MapMode(msg.Mode),
                    FinalDestination = new Vector2(cartesian.X, cartesian.Y),
                    TargetSpeed      = msg.TargetSpeed,
                    ArrivalRadius    = msg.ArrivalRadius
                });

                FdpLog<NavigationIntentIngressTranslator>.Debug(
                    "[Node-{0}] NavigationIntent ingress: EntityId={1} IntentId={2} Mode={3}",
                    _localNodeId, msg.EntityId, msg.IntentId, msg.Mode);
            }
        }

        /// <summary>No egress for this translator.</summary>
        public void ScanAndPublish(ISimulationView view) { }

        /// <summary>No DDS dispose needed.</summary>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <summary>No DDS dispose needed.</summary>
        public void Dispose(long networkEntityId) { }

        // ── Enum mapping ──────────────────────────────────────────────────────

        private static EcsNavMode MapMode(ENavigationMode mode) => mode switch
        {
            ENavigationMode.NAV_DIRECT_POINT   => EcsNavMode.DirectPoint,
            ENavigationMode.NAV_FOLLOW_ROUTE   => EcsNavMode.FollowRoute,
            ENavigationMode.NAV_JOIN_FORMATION => EcsNavMode.JoinFormation,
            _                                  => EcsNavMode.None,
        };
    }
}
