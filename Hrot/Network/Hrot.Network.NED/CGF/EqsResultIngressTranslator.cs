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
        internal readonly Dictionary<(long ParentNetId, int ChildIndex), Entity> _childEntityCache = new();

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
                if (!sample.IsValid)
                {
                    // NotAliveDisposed: evict the stale cache entry so the next live sample
                    // triggers a fresh entity scan rather than resolving to a dead entity.
                    var keyData         = DdsTypeSupport.FromNative<EqsResultTopic>(sample.NativePtr);
                    RemoveCacheEntry(keyData.ParentNetworkId, keyData.LocalChildIndex);
                    continue;
                }

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
                    // ⭐⭐ CE-487 — a hit is RE-CHECKED every time. 📄 DESIGN_Behaviour_Fault_And_Teardown.md §1 D5, §2 ③.
                    //    Part ids are allocated and REUSED (D5 ①): when a behaviour run ends, its sensor is destroyed
                    //    and the next run's sensor takes the same (parent, part id) — a NEW local entity. The instance is
                    //    never disposed (CE-486), so no dispose sample evicts the entry; and on a KeepLast-1 topic one
                    //    could collapse anyway. ⇒ without this check every answer for the new sensor went to the dead one.
                    if (!_childEntityCache.TryGetValue(cacheKey, out observer)
                        || !EqsSensorKey.IsChildSensor(repo, observer, data.ParentNetworkId, data.LocalChildIndex))
                    {
                        _childEntityCache.Remove(cacheKey);

                        // Cache miss (or a stale hit): one-shot scan for the child sensor.
                        Entity? found = null;
                        foreach (var e in repo.Query().With<PartMetadata>().With<EqsSensor>().Build())
                        {
                            if (!EqsSensorKey.IsChildSensor(repo, e, data.ParentNetworkId, data.LocalChildIndex)) continue;
                            found = e;
                            break;
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
                    Results     = MapToLocal(data.Results, _entityMap),
                });
            }
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }

        // ── Internal helpers (exposed for unit testing via InternalsVisibleTo) ─

        /// <summary>
        /// Rewrites every entity-shaped entry's <c>EntityId</c> from the wire's NETWORK id to this node's
        /// packed local entity, and drops entries whose entity is not (yet) known here. Positional
        /// entries (<c>EntityId == 0</c>) pass through.
        /// </summary>
        /// <remarks>
        /// ⭐ Without this the Brain's <c>EqsCognitiveBuffer</c> held network ids across nodes but packed
        /// entities on one node — one field, two meanings — so <c>new Entity((ulong)EntityId)</c> (every
        /// reader, including the blueprint <c>ReadEqsResult</c> node) was wrong exactly in the split. Design
        /// §4.1 says the id "resolves to local entity on Brain"; the area-query ingress always did.
        /// </remarks>
        internal static List<EqsResultEntry> MapToLocal(List<EqsResultEntry>? results, NetworkEntityMap entityMap)
        {
            var mapped = new List<EqsResultEntry>(results?.Count ?? 0);
            if (results is null) return mapped;
            foreach (var entry in results)
            {
                if (entry.EntityId == 0L) { mapped.Add(entry); continue; }
                if (!entityMap.TryGetEntity(entry.EntityId, out var local)) continue;
                var copy = entry;
                copy.EntityId = (long)local.PackedValue;
                mapped.Add(copy);
            }
            return mapped;
        }

        /// <summary>
        /// Removes a cache entry for the given composite key.
        /// Called when a NotAliveDisposed DDS sample is received.
        /// </summary>
        internal void RemoveCacheEntry(long parentNetworkId, int localChildIndex)
            => _childEntityCache.Remove((parentNetworkId, localChildIndex));
    }
}

