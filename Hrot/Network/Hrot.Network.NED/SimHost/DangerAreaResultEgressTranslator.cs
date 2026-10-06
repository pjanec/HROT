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
        private const string DdsTopicName = "DangerAreaResult";
        private readonly DdsWriter<DangerAreaResultTopic>? _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtEqsResult;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public DangerAreaResultEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _writer    = new DdsWriter<DangerAreaResultTopic>(participant, DdsTopicName);
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;
            foreach (var evt in view.ReadManagedEvents<DangerAreaResultEvent>())
            {
                if (evt.ParentNetworkId == 0) continue;      // a local-only sensor is never on the wire
                if (!evt.Observer.IsNull) continue;          // an answer that ARRIVED from the wire (a Brain-role node)
                if (_entityMap.TryGetEntity(evt.ParentNetworkId, out var parent) && view.IsAlive(parent) &&
                    !Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(
                        view, parent, Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, evt.LocalChildIndex)))
                    continue;

                var areas = new List<DangerAreaWire>(evt.Count);
                for (int i = 0; i < evt.Count && i < evt.Areas.Length; i++) areas.Add(DangerAreaWireMap.ToWire(in evt.Areas[i]));
                _writer.Write(new DangerAreaResultTopic
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
        public void Dispose(long networkEntityId) { }
    }
}
