using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Modules.Geographic;
using Fdp.ModuleHost.Abstractions;

using EcsNavigationIntent = Fdp.Toolkit.Navigation.NavigationIntent;
using EcsNavMode          = Fdp.Toolkit.Navigation.NavigationMode;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress translator: reads the ECS <see cref="EcsNavigationIntent"/> component
    /// for all locally-owned entities and publishes a DDS <see cref="Hrot.NED.Descriptors.NavigationIntent"/>
    /// sample for each.
    /// </summary>
    public sealed class NavigationIntentEgressTranslator : IDescriptorTranslator
    {
        private readonly IDdsWriter<Hrot.NED.Descriptors.NavigationIntent> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly IGeographicTransform _geoTransform;
        private readonly long _localNodeId;
        // Tracks the tick of the last scan so QueryDelta can skip unchanged chunks.
        private uint _lastScanTick;
        // Fine-grained per-entity change filter: stores the last-published IntentId so that
        // chunk-level false positives (multiple entities in a dirty 64KB block) are dropped
        // without any executor coupling or shadow ECS component.
        // Key = full Entity handle (Index + Generation), Value = last IntentId that was
        // published to DDS for that exact entity instance.
        private readonly System.Collections.Generic.Dictionary<Entity, uint> _lastPublishedIntentId
            = new System.Collections.Generic.Dictionary<Entity, uint>();

        public string TopicName      => "NavigationIntent";
        public long   DescriptorOrdinal => (long)EDescriptorType.dtNavigationIntent;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        // NavigationIntent ECS component ID = NavigationContractsComponentIds.NavigationIntent = 67
        private static readonly IReadOnlyList<int> _targetIds = new int[] { Fdp.Toolkit.Navigation.NavigationContractsComponentIds.NavigationIntent };
        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        public NavigationIntentEgressTranslator(
            DdsParticipant      dds,
            NetworkEntityMap    entityMap,
            IGeographicTransform geoTransform,
            long localNodeId)
            : this(new DdsWriterAdapter<Hrot.NED.Descriptors.NavigationIntent>(dds, "NavigationIntent"),
                   entityMap, geoTransform, localNodeId)
        {
        }

        /// <summary>
        /// Testable constructor. Accepts a pre-built writer so unit tests can
        /// capture published samples without a live DDS participant.
        /// </summary>
        internal NavigationIntentEgressTranslator(
            IDdsWriter<Hrot.NED.Descriptors.NavigationIntent> writer,
            NetworkEntityMap    entityMap,
            IGeographicTransform geoTransform,
            long localNodeId)
        {
            _writer       = writer       ?? throw new System.ArgumentNullException(nameof(writer));
            _entityMap    = entityMap    ?? throw new System.ArgumentNullException(nameof(entityMap));
            _geoTransform = geoTransform ?? throw new System.ArgumentNullException(nameof(geoTransform));
            _localNodeId  = localNodeId;
        }

        /// <summary>No ingress for this translator.</summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Publishes <see cref="Hrot.NED.Descriptors.NavigationIntent"/> to DDS for every
        /// authority-owned entity whose <see cref="EcsNavigationIntent"/> has changed since
        /// the last scan.
        ///
        /// Two-tier filtering:
        ///   1. Coarse (unmanaged, zero-alloc): QueryDelta skips entire 64KB EntityHeader
        ///      chunks that have not been written since _lastScanTick.
        ///   2. Fine (per-entity, O(1)): IntentId comparison in a local dictionary drops the
        ///      remaining chunk-level false positives — entities adjacent in memory to the one
        ///      that actually changed — without requiring any executor coupling or shadow ECS
        ///      component.  Because every executor (MoveToExecutor, FollowRouteExecutor, etc.)
        ///      increments IntentId on each new command, a change in IntentId is a strict
        ///      proxy for "this entity's navigation order actually changed".
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            // QueryDelta is a concrete-repository API; bail out gracefully when view
            // is a snapshot or test double that does not implement it.
            var repo = view as EntityRepository;
            if (repo == null)
                return;

            var query = view.Query()
                .With<EcsNavigationIntent>()
                .With<NetworkIdentity>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            // 1. Coarse unmanaged filter: skips all chunks unchanged since the last scan.
            foreach (var entity in repo.QueryDelta(query, _lastScanTick))
            {
                // Only publish navigation intent for locally-owned entities.
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                ref readonly var intent = ref view.GetComponentRO<EcsNavigationIntent>(entity);


                // 2. Fine-grained per-entity filter: only publish when IntentId changed.
                //    IntentId is incremented by every executor (MoveToExecutor, FollowRouteExecutor,
                //    etc.) on each new navigation command, making it a zero-coupling change
                //    fingerprint that needs no MarkDirty calls anywhere in the AI layer.
                if (_lastPublishedIntentId.TryGetValue(entity, out uint lastId)
                    && lastId == intent.IntentId)
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

                SentSampleCount++;
                // Record the published IntentId so the same command is not resent next frame.
                _lastPublishedIntentId[entity] = intent.IntentId;

                FdpLog<NavigationIntentEgressTranslator>.Trace(
                    "[Node-{0}] NavigationIntent egress: EntityId={1} IntentId={2} Mode={3}",
                    _localNodeId, netId.Value, intent.IntentId, intent.Mode);
            }

            _lastScanTick = repo.GlobalVersion;
        }

        /// <summary>
        /// Ghost promotion is not needed for NavigationIntent egress.
        /// </summary>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <summary>No DDS dispose needed for navigation intent.</summary>
        public void Dispose(long networkEntityId) { }

        // â”€â”€ Enum mapping â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
