using System.Numerics;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Services;
using Fdp.Modules.Geographic;
using ModuleHost.Core.Abstractions;

using EcsNavigationIntent = FDP.Toolkit.Navigation.NavigationIntent;
using EcsNavMode          = FDP.Toolkit.Navigation.NavigationMode;

namespace Hrot.SimHost.Network
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

        public string TopicName      => "NavigationIntent";
        public long   DescriptorOrdinal => 52;

        public NavigationIntentIngressTranslator(
            DdsParticipant      dds,
            NetworkEntityMap    entityMap,
            IGeographicTransform geoTransform)
        {
            _reader       = new DdsReader<Hrot.NED.Descriptors.NavigationIntent>(dds, "NavigationIntent");
            _entityMap    = entityMap    ?? throw new System.ArgumentNullException(nameof(entityMap));
            _geoTransform = geoTransform ?? throw new System.ArgumentNullException(nameof(geoTransform));
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
                        "[TRACE-SH] NavigationIntent ingress: unknown EntityId={0} — skipped", msg.EntityId);
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
                    "[TRACE-SH] NavigationIntent ingress: EntityId={0} IntentId={1} Mode={2}",
                    msg.EntityId, msg.IntentId, msg.Mode);
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
