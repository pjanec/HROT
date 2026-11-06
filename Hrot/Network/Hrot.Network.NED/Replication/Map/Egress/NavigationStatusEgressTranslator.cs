using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

using EcsNavigationStatus = Fdp.Toolkit.Navigation.NavigationStatus;
using EcsNavResult        = Fdp.Toolkit.Navigation.NavigationResult;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress translator: reads the ECS <see cref="EcsNavigationStatus"/> component
    /// for all locally-owned entities and publishes a DDS <see cref="Hrot.NED.Descriptors.NavigationStatus"/>
    /// sample for each when the status changes.
    /// </summary>
    public sealed class NavigationStatusEgressTranslator : IDescriptorTranslator
    {
        // â”€â”€ DDS writer â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the instances it deletes on a destruction, a wipe or (as an
        //   unregister) a hand-over. The change cache below stays: it compares (IntentId, Result, Phase), finer than the wire sample.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<Hrot.NED.Descriptors.NavigationStatus> _instances;
        private readonly NetworkEntityMap _entityMap;
        private readonly NodeId _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<Hrot.NED.Descriptors.NavigationStatus>();
        public long   DescriptorOrdinal => (long)EDescriptorType.dtNavigationStatus;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        // NavigationStatus ECS component ID = NavigationContractsComponentIds.NavigationStatus = 68
        private static readonly IReadOnlyList<int> _targetIds = new int[] { Fdp.Toolkit.Navigation.NavigationContractsComponentIds.NavigationStatus };
        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        // â”€â”€ Per-entity change-detection cache â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Avoids publishing on every tick when the status is unchanging.
        // Keyed by Entity; value is the last-published (IntentId, Result, Phase).
        // ProgressS is omitted from the key — it is included unconditionally
        // in the publish payload but does not trigger a new publish on its own.
        private readonly Dictionary<Entity, (uint IntentId, EcsNavResult Result, NavigationPhase Phase)> _lastPublished = new();

        // Heartbeat: republish ProgressS every 5 s even when nothing has changed,
        // to recover from any UDP packet loss on unreliable topics.
        private const uint ProgressHeartbeatInterval = 300; // 5 s at 60 Hz

        public NavigationStatusEgressTranslator(
            DdsParticipant   dds,
            NetworkEntityMap entityMap,
            int localNodeId)
        {
            _instances = new(new DdsWriter<Hrot.NED.Descriptors.NavigationStatus>(dds), static key => new Hrot.NED.Descriptors.NavigationStatus { EntityId = (int)key });
            _entityMap = entityMap ?? throw new System.ArgumentNullException(nameof(entityMap));
            _localNodeId = (Fdp.Core.NodeId)(localNodeId);
        }

        /// <summary>No ingress for this translator.</summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Publishes <see cref="Hrot.NED.Descriptors.NavigationStatus"/> to DDS only when
        /// <see cref="EcsNavigationStatus.IntentId"/> or <see cref="EcsNavigationStatus.Result"/>
        /// has changed since the last publish, plus a periodic heartbeat for ProgressS.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            if (_instances.OnFrame(view)) _lastPublished.Clear();   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3); the change cache restarts with it
            var query = view.Query()
                .With<EcsNavigationStatus>()
                .DefaultRoutePublishable()
                .WithLifecycle(EntityLifecycle.AllButTearDown)
                .Build();

            long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            foreach (var entity in query)
            {
                // Only publish navigation status for locally-owned (Muscle) entities.
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                var status = view.GetComponentRO<EcsNavigationStatus>(entity);

                // Change-detection: publish only when IntentId or Result changes,
                // or on first publish, or on the ProgressS heartbeat.
                bool isFirstPublish = !_lastPublished.TryGetValue(entity, out var last);
                bool hasChanged     = isFirstPublish
                    || last.IntentId != status.IntentId
                    || last.Result   != status.Result
                    || last.Phase    != status.Phase;

                // Salted heartbeat so not all entities flush at the same tick.
                uint salt      = (uint)(entity.Index % ProgressHeartbeatInterval);
                bool heartbeat = ((view.Tick + salt) % ProgressHeartbeatInterval) == 0;

                if (!hasChanged && !heartbeat)
                    continue;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

                _instances.Write(netId.Value, new Hrot.NED.Descriptors.NavigationStatus
                {
                    EntityId              = netId.Value,
                    IntentId              = status.IntentId,
                    Result                = MapResult(status.Result),
                    ProgressS             = status.ProgressS,
                    Phase                 = (byte)status.Phase,
                    ReplanCount           = status.ReplanCount,
                    RouteHandle           = status.RouteHandle,
                    NavmeshVersionObserved = status.NavmeshVersionObserved,
                });

                SentSampleCount++;
                _lastPublished[entity] = (status.IntentId, status.Result, status.Phase);

                FdpLog<NavigationStatusEgressTranslator>.Trace(
                    "[Node-{0}] NavigationStatus egress: EntityId={1} IntentId={2} Result={3}",
                    _localNodeId, netId.Value, status.IntentId, status.Result);
            }
        }

        /// <summary>No ghost promotion needed.</summary>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <summary>Cleans up the per-entity change-detection cache on entity disposal.</summary>
        public void Dispose(int networkEntityId)
        {
            if (_entityMap.TryGetEntity(networkEntityId, out var entity))
                _lastPublished.Remove(entity);
            _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire
        }

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget what was published so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId)
        {
            if (_entityMap.TryGetEntity(networkEntityId, out var entity)) _lastPublished.Remove(entity);
            _instances.Release(networkEntityId);
        }

        // â”€â”€ Enum mapping â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static ENavigationResult MapResult(EcsNavResult result) => result switch
        {
            EcsNavResult.Arrived              => ENavigationResult.RES_ARRIVED,
            EcsNavResult.FailedBlocked        => ENavigationResult.RES_FAILED_BLOCKED,
            EcsNavResult.FailedUnreachable    => ENavigationResult.RES_FAILED_UNREACHABLE,
            EcsNavResult.PathFound            => ENavigationResult.RES_PATH_FOUND,
            EcsNavResult.NoPath               => ENavigationResult.RES_NO_PATH,
            EcsNavResult.FailedNoLayer        => ENavigationResult.RES_FAILED_NO_LAYER,
            EcsNavResult.FailedInvalidHandle  => ENavigationResult.RES_FAILED_INVALID_HANDLE,
            _                                 => ENavigationResult.RES_IN_PROGRESS,
        };
    }
}
