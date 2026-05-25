using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Spatial.Eqs.Topics;
using Hrot.NED.Descriptors;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Muscle-side ingress translator: receives <see cref="EqsSensorConfigTopic"/> samples
    /// and applies the <c>EqsSensor</c> component to the corresponding ghost entity so the
    /// solver picks it up on the next tick.
    /// On <c>NOT_ALIVE_DISPOSED</c>, removes <c>EqsSensor</c> from the ghost entity,
    /// signalling the solver to drop the query.
    /// </summary>
    public sealed class EqsSensorConfigIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EqsSensorConfig";
        private readonly DdsReader<EqsSensorConfigTopic>? _reader;
        private readonly NetworkEntityMap _entityMap;
        // Dictionary-cached entity lookup: (ParentNetworkId, LocalChildIndex) -> child ghost entity.
        // Avoids ECS query scans inside the polling loop (O(1) steady-state lookup).
        private readonly Dictionary<(long ParentNetId, int ChildIndex), Entity> _childGhostCache = new();

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtEqsSensorConfig;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EqsSensorConfigIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            if (entityMap == null) throw new ArgumentNullException(nameof(entityMap));
            _entityMap = entityMap;
            _reader = participant != null
                ? new DdsReader<EqsSensorConfigTopic>(participant, DdsTopicName)
                : null;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                long parentNetId;
                int  localChildIndex;
                if (sample.IsValid)
                {
                    parentNetId     = sample.Data.ParentNetworkId;
                    localChildIndex = sample.Data.LocalChildIndex;
                    ReceivedSampleCount++;
                }
                else
                {
                    // For NOT_ALIVE samples the managed .Data property throws.
                    // Read key fields directly from the native serialised buffer.
                    var keyData     = DdsTypeSupport.FromNative<EqsSensorConfigTopic>(sample.NativePtr);
                    parentNetId     = keyData.ParentNetworkId;
                    localChildIndex = keyData.LocalChildIndex;
                }

                if (localChildIndex == 0)
                {
                    // Legacy single-sensor path: sensor lives directly on the parent ghost entity.
                    if (!_entityMap.TryGetEntity(parentNetId, out var parentGhost)) continue;

                    if (sample.IsValid)
                    {
                        cmd.SetComponent(parentGhost, BuildSensor(sample.Data));
                        _childGhostCache[(parentNetId, 0)] = parentGhost;
                    }
                    else if (sample.Info.InstanceState == DdsInstanceState.NotAliveDisposed)
                    {
                        cmd.RemoveComponent<EqsSensor>(parentGhost);
                        _childGhostCache.Remove((parentNetId, 0));
                    }
                }
                else
                {
                    // Child-entity sensor path: carrier ghost is spawned/reused from cache.
                    if (!_entityMap.TryGetEntity(parentNetId, out var parentGhost)) continue;

                    var cacheKey = (parentNetId, localChildIndex);

                    if (sample.IsValid)
                    {
                        if (!_childGhostCache.TryGetValue(cacheKey, out var child))
                        {
                            // Cache miss: spawn carrier ghost entity.
                            // No NetworkIdentity, TkbIdentity, or GhostStateTracker on the carrier.
                            child = cmd.CreateEntity();
                            cmd.AddComponent(child, new PartMetadata
                            {
                                ParentEntity      = parentGhost,
                                InstanceId        = localChildIndex,
                                DescriptorOrdinal = 0,
                            });
                            cmd.AddComponent(child, BuildSensor(sample.Data));
                            cmd.AddComponent(child, default(EqsCognitiveBuffer));
                            _childGhostCache[cacheKey] = child;
                        }
                        else
                        {
                            // Cache hit: update sensor parameters on existing carrier.
                            cmd.SetComponent(child, BuildSensor(sample.Data));
                        }
                    }
                    else if (sample.Info.InstanceState == DdsInstanceState.NotAliveDisposed)
                    {
                        if (_childGhostCache.Remove(cacheKey, out var dead))
                            cmd.DestroyEntity(dead);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }

        // Resolves a wire network ID to the local Muscle-side Entity. Returns Entity.Null if
        // networkId == 0 or the entity is not yet in the ghost map.
        internal Entity ResolveSlot(long networkId)
        {
            if (networkId == 0L) return Entity.Null;
            return _entityMap.TryGetEntity(networkId, out Entity e) ? e : Entity.Null;
        }

        // Builds an EqsSensor struct from a received DDS topic sample.
        private EqsSensor BuildSensor(EqsSensorConfigTopic data) => new EqsSensor
        {
            BlueprintId         = data.BlueprintId,
            Epoch               = data.Epoch,
            SearchRadius        = data.SearchRadius,
            FactionFilter       = data.FactionFilter,
            ThreatThreshold     = data.ThreatThreshold,
            PublishPolicy       = data.PublishPolicy,
            Priority            = data.Priority,
            ScoreDeltaThreshold = data.ScoreDeltaThreshold,
            ContextSlot0        = ResolveSlot(data.ContextSlot0NetworkId),
            ContextSlot1        = ResolveSlot(data.ContextSlot1NetworkId),
            ContextSlot2        = ResolveSlot(data.ContextSlot2NetworkId),
        };
    }
}

