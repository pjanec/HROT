using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Squad.DangerArea.Topics;
using Hrot.NED.Descriptors;

namespace Hrot.Network.NED.CGF
{
    /// <summary>
    /// ⭐ <c>CE-3072</c> B3 — Brain side: <see cref="DangerAreaResultTopic"/> → a <see cref="DangerAreaResultEvent"/> with its
    /// <see cref="DangerAreaResultEvent.Observer"/> = the local child sensor, for <c>DangerAreaSensorSystem</c>. The twin of
    /// <see cref="EqsResultIngressTranslator"/>: the same re-checked child cache (part ids are reused, CE-487) and the same
    /// own-echo skip (the recorded owner of the result part SOLVED it). 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7.
    /// </summary>
    public sealed class DangerAreaResultIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "DangerAreaResult";
        private readonly DdsReader<DangerAreaResultTopic>? _reader;
        private readonly NetworkEntityMap _entityMap;
        internal readonly Dictionary<(long ParentNetId, int ChildIndex), Entity> _childEntityCache = new();

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtEqsResult;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public DangerAreaResultIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader = participant != null ? new DdsReader<DangerAreaResultTopic>(participant, DdsTopicName) : null;
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
                    var key = DdsTypeSupport.FromNative<DangerAreaResultTopic>(sample.NativePtr);
                    _childEntityCache.Remove((key.ParentNetworkId, key.LocalChildIndex));
                    continue;
                }
                ReceivedSampleCount++;
                var data = sample.Data;
                if (data.ParentNetworkId == 0 || data.LocalChildIndex == 0) continue;   // a danger sensor is always a child

                var cacheKey = (data.ParentNetworkId, data.LocalChildIndex);
                if (!_childEntityCache.TryGetValue(cacheKey, out var observer)
                    || !EqsSensorKey.IsChildSensor(repo, observer, data.ParentNetworkId, data.LocalChildIndex))
                {
                    _childEntityCache.Remove(cacheKey);
                    observer = Entity.Null;
                    foreach (var e in repo.Query().With<PartMetadata>().With<EqsSensor>().Build())
                    {
                        if (!EqsSensorKey.IsChildSensor(repo, e, data.ParentNetworkId, data.LocalChildIndex)) continue;
                        observer = e;
                        break;
                    }
                    if (observer.IsNull) continue;
                    _childEntityCache[cacheKey] = observer;
                }

                // The solver's own answer looping back: its DangerAreaSensorSystem already took the local event.
                if (Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.IsRecordedOwner(
                        repo, observer, Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, data.LocalChildIndex)))
                    continue;

                int n = data.Areas?.Count ?? 0;
                var areas = new DangerAreaDescriptor[n];
                for (int i = 0; i < n; i++) areas[i] = DangerAreaWireMap.FromWire(data.Areas![i]);
                repo.Bus.PublishManaged(new DangerAreaResultEvent
                {
                    ParentNetworkId = data.ParentNetworkId,
                    LocalChildIndex = data.LocalChildIndex,
                    Epoch           = data.Epoch,
                    RefreshTick     = data.RefreshTick,
                    Observer        = observer,
                    Areas           = areas,
                    Count           = n,
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
