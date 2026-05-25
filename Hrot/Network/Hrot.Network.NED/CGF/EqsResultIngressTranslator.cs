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

namespace Hrot.Network.NED.CGF
{
    /// <summary>
    /// Brain-side ingress translator: receives <see cref="EqsResultTopic"/> samples from the
    /// Muscle node and publishes a managed <see cref="EqsResultUpdateEvent"/> on the Brain-tier
    /// event bus so that <c>EqsResultUpdateSystem</c> can write the results into the entity's
    /// <c>EqsCognitiveBuffer</c> component.
    /// </summary>
    public sealed class EqsResultIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EqsResult";
        private readonly DdsReader<EqsResultTopic>? _reader;
        private readonly NetworkEntityMap _entityMap;
        // Dictionary-cached reverse lookup: (ParentNetworkId, LocalChildIndex) -> Brain-side entity.
        // Populated lazily on first miss by a one-shot scan of PartMetadata entities.
        private readonly Dictionary<(long ParentNetId, int ChildIndex), Entity> _childEntityCache = new();

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtEqsResult;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EqsResultIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            if (entityMap == null) throw new ArgumentNullException(nameof(entityMap));
            _entityMap = entityMap;
            _reader = participant != null
                ? new DdsReader<EqsResultTopic>(participant, DdsTopicName)
                : null;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            if (view is not EntityRepository repo) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;

                ReceivedSampleCount++;
                var data = sample.Data;

                // Skip offline results (ParentNetworkId == 0): they are never sent via DDS.
                if (data.ParentNetworkId == 0) continue;

                Entity observer;
                if (data.LocalChildIndex == 0)
                {
                    // Legacy single-sensor: sensor lives on the parent entity itself.
                    if (!_entityMap.TryGetEntity(data.ParentNetworkId, out observer)) continue;
                }
                else
                {
                    // Child-entity sensor: look up via dictionary cache.
                    var cacheKey = (data.ParentNetworkId, data.LocalChildIndex);
                    if (!_childEntityCache.TryGetValue(cacheKey, out observer))
                    {
                        // Cache miss: one-shot scan for the child entity.
                        Entity? found = null;
                        foreach (var e in repo.Query().With<PartMetadata>().Build())
                        {
                            var meta = repo.GetComponentRO<PartMetadata>(e);
                            if (meta.InstanceId == data.LocalChildIndex &&
                                repo.HasComponent<NetworkIdentity>(meta.ParentEntity) &&
                                repo.GetComponentRO<NetworkIdentity>(meta.ParentEntity).Value == data.ParentNetworkId)
                            {
                                found = e;
                                break;
                            }
                        }
                        if (!found.HasValue) continue;
                        observer = found.Value;
                        _childEntityCache[cacheKey] = observer;
                    }
                }

                // Bridge to the managed event bus so EqsResultUpdateSystem can consume it.
                // EqsResultTopic.Results is List<EqsResultEntry> -- direct assignment works.
                repo.Bus.PublishManaged(new EqsResultUpdateEvent
                {
                    Observer    = observer,
                    Epoch       = data.Epoch,
                    RefreshTick = data.RefreshTick,
                    Results     = data.Results,
                });
            }
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}

