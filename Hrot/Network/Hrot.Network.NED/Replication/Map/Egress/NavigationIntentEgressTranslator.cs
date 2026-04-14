using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Extensions;
using FDP.Toolkit.Replication.Services;
using Fdp.Modules.Geographic;
using Fdp.ModuleHost.Core.Abstractions;

using EcsNavigationIntent = FDP.Toolkit.Navigation.NavigationIntent;
using EcsNavMode          = FDP.Toolkit.Navigation.NavigationMode;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress translator: reads the ECS <see cref="EcsNavigationIntent"/> component
    /// for all locally-owned entities and publishes a DDS <see cref="Hrot.NED.Descriptors.NavigationIntent"/>
    /// sample for each.
    /// </summary>
    public sealed class NavigationIntentEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<Hrot.NED.Descriptors.NavigationIntent> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly IGeographicTransform _geoTransform;
        private readonly long _localNodeId;

        public string TopicName      => "NavigationIntent";
        public long   DescriptorOrdinal => (long)EDescriptorType.dtNavigationIntent;

        // NavigationIntent ECS component ID = NavigationContractsComponentIds.NavigationIntent = 67
        private static readonly IReadOnlyList<int> _targetIds = new int[] { FDP.Toolkit.Navigation.NavigationContractsComponentIds.NavigationIntent };
        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        public NavigationIntentEgressTranslator(
            DdsParticipant      dds,
            NetworkEntityMap    entityMap,
            IGeographicTransform geoTransform,
            long localNodeId)
        {
            _writer       = new DdsWriter<Hrot.NED.Descriptors.NavigationIntent>(dds, "NavigationIntent");
            _entityMap    = entityMap   ?? throw new System.ArgumentNullException(nameof(entityMap));
            _geoTransform = geoTransform ?? throw new System.ArgumentNullException(nameof(geoTransform));
            _localNodeId  = localNodeId;
        }

        /// <summary>No ingress for this translator.</summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Publishes <see cref="Hrot.NED.Descriptors.NavigationIntent"/> to DDS for every
        /// authority-owned entity that has an active <see cref="EcsNavigationIntent"/>.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<EcsNavigationIntent>()
                .With<NetworkIdentity>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            long packedKey = Fdp.ModuleHost.Core.Network.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            foreach (var entity in query)
            {
                // Only publish navigation intent for locally-owned entities.
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                var intent = view.GetComponentRO<EcsNavigationIntent>(entity);

                // Skip inactive intents — no command to broadcast.
                if (intent.Mode == EcsNavMode.None)
                    continue;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

                // Convert Cartesian destination to geodetic (WGS-84) for the wire format.
                var (lat, lon, alt) = _geoTransform.ToGeodetic(
                    new Vector3(intent.FinalDestination.X, intent.FinalDestination.Y, 0f));

                _writer.Write(new Hrot.NED.Descriptors.NavigationIntent
                {
                    EntityId         = (int)netId.Value,
                    IntentId         = intent.IntentId,
                    Mode             = MapMode(intent.Mode),
                    FinalDestination = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt },
                    TargetSpeed      = intent.TargetSpeed,
                    ArrivalRadius    = intent.ArrivalRadius
                });

                FdpLog<NavigationIntentEgressTranslator>.Debug(
                    "[Node-{0}] NavigationIntent egress: EntityId={1} IntentId={2} Mode={3}",
                    _localNodeId, netId.Value, intent.IntentId, intent.Mode);
            }
        }

        /// <summary>
        /// Ghost promotion is not needed for NavigationIntent egress.
        /// </summary>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <summary>No DDS dispose needed for navigation intent.</summary>
        public void Dispose(long networkEntityId) { }

        // ── Enum mapping ──────────────────────────────────────────────────────

        private static ENavigationMode MapMode(EcsNavMode mode) => mode switch
        {
            EcsNavMode.DirectPoint   => ENavigationMode.NAV_DIRECT_POINT,
            EcsNavMode.FollowRoute   => ENavigationMode.NAV_FOLLOW_ROUTE,
            EcsNavMode.JoinFormation => ENavigationMode.NAV_JOIN_FORMATION,
            EcsNavMode.RoadGraph     => ENavigationMode.NAV_ROAD_GRAPH,
            _                        => ENavigationMode.NAV_NONE,
        };
    }
}
