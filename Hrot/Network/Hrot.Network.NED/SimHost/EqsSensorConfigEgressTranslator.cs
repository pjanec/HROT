using System;
using System.Collections.Generic;
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

        // What was last written per sensor entity. ⭐ A reliable topic that publishes ONCE never
        // carries a later parameter change (epoch bump, new area) to the Muscle — the old
        // SmartEgressUtil gate did exactly that, and the distributed rails worked around it by
        // removing and re-adding the sensor. Publishing on any change is what keeps the split right.
        private readonly Dictionary<Entity, EqsSensorConfigTopic> _published = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _gone = new();

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

            _seen.Clear();
            foreach (var entity in query)
            {
                if (!view.HasAuthority(entity, DescriptorOrdinal)) continue;

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

                _seen.Add(entity);

                // Hold while a context slot names an entity that has no network id yet: sending 0
                // would tell the Muscle "no entity", and nothing would re-send once the id existed.
                if (!TrySlotNetId(sensor.ContextSlot0, out long slot0)
                    || !TrySlotNetId(sensor.ContextSlot1, out long slot1)
                    || !TrySlotNetId(sensor.ContextSlot2, out long slot2))
                    continue;

                var topic = new EqsSensorConfigTopic
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
                    ContextSlot0NetworkId = slot0,
                    ContextSlot1NetworkId = slot1,
                    ContextSlot2NetworkId = slot2,
                };

                if (_published.TryGetValue(entity, out var last) && SameConfig(in last, in topic))
                    continue;

                _writer.Write(topic);
                _published[entity] = topic;
                SentSampleCount++;
                SmartEgressUtil.MarkPublished(view, entity, DescriptorOrdinal);
            }

            // A child sensor (LocalChildIndex != 0) that is destroyed or loses its EqsSensor must be
            // disposed, or the Muscle's carrier keeps solving it forever. Legacy single sensors are
            // disposed by the removal pass below and by Dispose(networkEntityId).
            _gone.Clear();
            foreach (var kv in _published)
            {
                if (_seen.Contains(kv.Key)) continue;
                if (view.IsAlive(kv.Key) && view.HasComponent<EqsSensor>(kv.Key)) continue; // e.g. authority moved
                _gone.Add(kv.Key);
            }
            foreach (var entity in _gone)
            {
                var last = _published[entity];
                _published.Remove(entity);
                if (last.LocalChildIndex != 0)
                    _writer.DisposeInstance(new EqsSensorConfigTopic
                    {
                        ParentNetworkId = last.ParentNetworkId,
                        LocalChildIndex = last.LocalChildIndex,
                    });
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

        // A null slot is 0 on the wire. A non-null slot must have a network id; false = not yet.
        private bool TrySlotNetId(Entity slotEntity, out long netId)
        {
            netId = 0L;
            if (slotEntity.IsNull) return true;
            return _entityMap.TryGetNetworkId(slotEntity, out netId);
        }

        private static bool SameConfig(in EqsSensorConfigTopic a, in EqsSensorConfigTopic b)
            => a.ParentNetworkId       == b.ParentNetworkId
            && a.LocalChildIndex       == b.LocalChildIndex
            && a.BlueprintId           == b.BlueprintId
            && a.Epoch                 == b.Epoch
            && a.SearchRadius.Equals(b.SearchRadius)
            && a.FactionFilter         == b.FactionFilter
            && a.ThreatThreshold.Equals(b.ThreatThreshold)
            && a.PublishPolicy         == b.PublishPolicy
            && a.Priority              == b.Priority
            && a.ScoreDeltaThreshold.Equals(b.ScoreDeltaThreshold)
            && a.ContextSlot0NetworkId == b.ContextSlot0NetworkId
            && a.ContextSlot1NetworkId == b.ContextSlot1NetworkId
            && a.ContextSlot2NetworkId == b.ContextSlot2NetworkId;
    }
}

