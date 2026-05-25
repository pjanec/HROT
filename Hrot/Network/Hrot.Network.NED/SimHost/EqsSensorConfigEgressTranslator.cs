using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Utilities;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Spatial.Eqs.Topics;
using Hrot.NED.Descriptors;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Brain-side egress translator: publishes <see cref="EqsSensorConfigTopic"/> to the Muscle
    /// node whenever an <c>EqsSensor</c> component is added or mutated on an authority-owned entity.
    /// Uses <c>SmartEgressUtil</c> for dirty-tracking so the topic is only sent on actual changes.
    /// </summary>
    public sealed class EqsSensorConfigEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EqsSensorConfig";
        private readonly DdsWriter<EqsSensorConfigTopic>? _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtEqsSensorConfig;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EqsSensorConfigEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            if (entityMap   == null) throw new ArgumentNullException(nameof(entityMap));
            _entityMap = entityMap;
            _writer = new DdsWriter<EqsSensorConfigTopic>(participant, DdsTopicName);
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;

            // Query all entities with EqsSensor regardless of NetworkIdentity:
            // child-entity sensors identify themselves via PartMetadata.
            var query = view.Query()
                .With<EqsSensor>()
                .Build();

            foreach (var entity in query)
            {
                if (!view.HasAuthority(entity, DescriptorOrdinal)) continue;

                if (!SmartEgressUtil.ShouldPublish(view, entity, DescriptorOrdinal, isUnreliable: false))
                    continue;

                ref readonly var sensor = ref view.GetComponentRO<EqsSensor>(entity);

                // 3-branch compound identity resolution.
                long parentNetworkId;
                int  localChildIndex;
                if (view.HasComponent<PartMetadata>(entity))
                {
                    var meta   = view.GetComponentRO<PartMetadata>(entity);
                    var parent = meta.ParentEntity;
                    if (!view.IsAlive(parent) || !view.HasComponent<NetworkIdentity>(parent))
                        continue; // parent gone or local-only
                    parentNetworkId = view.GetComponentRO<NetworkIdentity>(parent).Value;
                    localChildIndex = meta.InstanceId;
                }
                else if (view.HasComponent<NetworkIdentity>(entity))
                {
                    parentNetworkId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                    localChildIndex = 0;
                }
                else
                {
                    // Local-only sensor: skip DDS publish.
                    continue;
                }

                _writer.Write(new EqsSensorConfigTopic
                {
                    ParentNetworkId       = parentNetworkId,
                    LocalChildIndex       = localChildIndex,
                    BlueprintId           = sensor.BlueprintId,
                    Epoch                 = sensor.Epoch,
                    SearchRadius          = sensor.SearchRadius,
                    FactionFilter         = sensor.FactionFilter,
                    ThreatThreshold       = sensor.ThreatThreshold,
                    PublishPolicy         = sensor.PublishPolicy,
                    Priority              = sensor.Priority,
                    ScoreDeltaThreshold   = sensor.ScoreDeltaThreshold,
                    ContextSlot0NetworkId = SlotNetId(sensor.ContextSlot0),
                    ContextSlot1NetworkId = SlotNetId(sensor.ContextSlot1),
                    ContextSlot2NetworkId = SlotNetId(sensor.ContextSlot2),
                });

                SentSampleCount++;
                SmartEgressUtil.MarkPublished(view, entity, DescriptorOrdinal);
            }

            // Removal detection: find entities with NetworkIdentity that no longer carry
            // EqsSensor. These are legacy single-sensor entities (LocalChildIndex == 0).
            var removalQuery = view.Query()
                .With<NetworkIdentity>()
                .Without<EqsSensor>()
                .Build();

            foreach (var entity in removalQuery)
            {
                if (!view.HasAuthority(entity, DescriptorOrdinal)) continue;
                if (!view.HasManagedComponent<EgressPublicationState>(entity)) continue;

                var state = view.GetManagedComponentRO<EgressPublicationState>(entity);
                if (!state.LastPublishedTickMap.ContainsKey(DescriptorOrdinal)) continue;

                // Entity lost EqsSensor after a prior publish -- send dispose.
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                _writer.DisposeInstance(new EqsSensorConfigTopic { ParentNetworkId = netId.Value, LocalChildIndex = 0 });
                state.LastPublishedTickMap.Remove(DescriptorOrdinal);
            }
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId)
        {
            _writer?.DisposeInstance(new EqsSensorConfigTopic { ParentNetworkId = networkEntityId, LocalChildIndex = 0 });
        }

        // Returns the network ID of a context-slot entity, or 0 if the entity is null/unregistered.
        private long SlotNetId(Entity slotEntity)
        {
            if (slotEntity.IsNull) return 0L;
            return _entityMap.TryGetNetworkId(slotEntity, out long netId) ? netId : 0L;
        }
    }
}

