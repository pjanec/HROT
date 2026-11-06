using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Squad.DangerArea.Topics;
using Hrot.NED.Descriptors;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// ⭐ <c>CE-3072</c> B3 — solver side: a danger-area sensor's answer (<see cref="DangerAreaResultEvent"/>, published by
    /// <c>EqsSolverSystem</c>) onto <see cref="DangerAreaResultTopic"/>, for the Brain. The twin of
    /// <see cref="EqsResultEventEgressTranslator"/>, with the same gate: only the node that OWNS the sensor's result part
    /// (<c>dtEqsResult</c>, part) publishes — the solver the Brain picked. 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7.
    /// </summary>
    public sealed class DangerAreaResultEgressTranslator : IDescriptorTranslator
    {
        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: one instance per (parent network id, child index), packed
        //   into the key; the instances it deletes when the parent is destroyed or the world is wiped.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<DangerAreaResultTopic> _instances;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<DangerAreaResultTopic>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtEqsResult;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public DangerAreaResultEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _instances = new(new DdsWriter<DangerAreaResultTopic>(participant),
                static key => new DangerAreaResultTopic { ParentNetworkId = (int)(key >> 32), LocalChildIndex = (int)(uint)key },
                static key => (int)(key >> 32));
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
            foreach (var evt in view.ReadManagedEvents<DangerAreaResultEvent>())
            {
                if (evt.ParentNetworkId == 0) continue;      // a local-only sensor is never on the wire
                if (!evt.Observer.IsNull) continue;          // an answer that ARRIVED from the wire (a Brain-role node)
                // ⭐ CE-4007 (Cluster Load Phase §8.3) — a parent gone or in TearDown: its results' deletes may be out already; never write them again (📐 live: a result one frame late re-published a destroyed unit's EQS result).
                if (!_entityMap.TryGetEntity(evt.ParentNetworkId, out var dying) || !view.IsAlive(dying) || Fdp.Network.Cyclone.Translators.PublishedInstances.IsTearingDown(view, dying)) continue;
                if (_entityMap.TryGetEntity(evt.ParentNetworkId, out var parent) && view.IsAlive(parent) &&
                    !Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(
                        view, parent, Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, evt.LocalChildIndex)))
                    continue;

                var areas = new List<DangerAreaWire>(evt.Count);
                for (int i = 0; i < evt.Count && i < evt.Areas.Length; i++) areas.Add(DangerAreaWireMap.ToWire(in evt.Areas[i]));
                _instances.Write(((long)evt.ParentNetworkId << 32) | (uint)evt.LocalChildIndex, new DangerAreaResultTopic
                {
                    ParentNetworkId = evt.ParentNetworkId,
                    LocalChildIndex = evt.LocalChildIndex,
                    Epoch           = evt.Epoch,
                    RefreshTick     = evt.RefreshTick,
                    Areas           = areas,
                });
                SentSampleCount++;
            }
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the parent is gone: delete its result instances on the wire
    }
}
