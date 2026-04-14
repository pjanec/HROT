using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Extensions;
using FDP.Toolkit.Replication.Services;
using Fdp.ModuleHost_Core.Abstractions;

using EcsNavigationStatus = FDP.Toolkit.Navigation.NavigationStatus;
using EcsNavResult        = FDP.Toolkit.Navigation.NavigationResult;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress translator: reads the ECS <see cref="EcsNavigationStatus"/> component
    /// for all locally-owned entities and publishes a DDS <see cref="Hrot.NED.Descriptors.NavigationStatus"/>
    /// sample for each when the status changes.
    /// </summary>
    public sealed class NavigationStatusEgressTranslator : IDescriptorTranslator
    {
        // ── DDS writer ────────────────────────────────────────────────────────

        private readonly DdsWriter<Hrot.NED.Descriptors.NavigationStatus> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly long _localNodeId;

        public string TopicName      => "NavigationStatus";
        public long   DescriptorOrdinal => (long)EDescriptorType.dtNavigationStatus;

        // NavigationStatus ECS component ID = NavigationContractsComponentIds.NavigationStatus = 68
        private static readonly IReadOnlyList<int> _targetIds = new int[] { FDP.Toolkit.Navigation.NavigationContractsComponentIds.NavigationStatus };
        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        // ── Per-entity change-detection cache ────────────────────────────────
        // Avoids publishing on every tick when the status is unchanging.
        // Keyed by Entity; value is the last-published (IntentId, Result).
        // ProgressS is omitted from the key — it is included unconditionally
        // in the publish payload but does not trigger a new publish on its own.
        private readonly Dictionary<Entity, (uint IntentId, EcsNavResult Result)> _lastPublished = new();

        // Heartbeat: republish ProgressS every 5 s even when nothing has changed,
        // to recover from any UDP packet loss on unreliable topics.
        private const uint ProgressHeartbeatInterval = 300; // 5 s at 60 Hz

        public NavigationStatusEgressTranslator(
            DdsParticipant   dds,
            NetworkEntityMap entityMap,
            long localNodeId)
        {
            _writer    = new DdsWriter<Hrot.NED.Descriptors.NavigationStatus>(dds, "NavigationStatus");
            _entityMap = entityMap ?? throw new System.ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
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
            var query = view.Query()
                .With<EcsNavigationStatus>()
                .With<NetworkIdentity>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            long packedKey = Fdp.ModuleHost_Core.Network.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

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
                    || last.Result   != status.Result;

                // Salted heartbeat so not all entities flush at the same tick.
                uint salt      = (uint)(entity.Index % ProgressHeartbeatInterval);
                bool heartbeat = ((view.Tick + salt) % ProgressHeartbeatInterval) == 0;

                if (!hasChanged && !heartbeat)
                    continue;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

                _writer.Write(new Hrot.NED.Descriptors.NavigationStatus
                {
                    EntityId  = (int)netId.Value,
                    IntentId  = status.IntentId,
                    Result    = MapResult(status.Result),
                    ProgressS = status.ProgressS,
                });

                _lastPublished[entity] = (status.IntentId, status.Result);

                FdpLog<NavigationStatusEgressTranslator>.Debug(
                    "[Node-{0}] NavigationStatus egress: EntityId={1} IntentId={2} Result={3}",
                    _localNodeId, netId.Value, status.IntentId, status.Result);
            }
        }

        /// <summary>No ghost promotion needed.</summary>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <summary>Cleans up the per-entity change-detection cache on entity disposal.</summary>
        public void Dispose(long networkEntityId)
        {
            if (_entityMap.TryGetEntity(networkEntityId, out var entity))
                _lastPublished.Remove(entity);
        }

        // ── Enum mapping ──────────────────────────────────────────────────────

        private static ENavigationResult MapResult(EcsNavResult result) => result switch
        {
            EcsNavResult.Arrived           => ENavigationResult.RES_ARRIVED,
            EcsNavResult.FailedBlocked     => ENavigationResult.RES_FAILED_BLOCKED,
            EcsNavResult.FailedUnreachable => ENavigationResult.RES_FAILED_UNREACHABLE,
            _                              => ENavigationResult.RES_IN_PROGRESS,
        };
    }
}
