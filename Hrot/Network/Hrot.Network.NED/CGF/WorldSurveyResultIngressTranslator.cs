using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Perception.Survey;
using Fdp.Toolkit.Perception.Survey.Topics;
using Hrot.NED.Descriptors;

namespace Hrot.Network.NED.CGF
{
    /// <summary>
    /// ⭐ <c>CE-3249</c> S4 — brain side: <see cref="WorldSurveyResultTopic"/> → a <see cref="WorldSurveyResultEvent"/> with its
    /// <see cref="WorldSurveyResultEvent.Observer"/> = the local child sensor, for <see cref="WorldSurveyApplySystem"/>. The twin of
    /// <see cref="DangerAreaResultIngressTranslator"/>: the same re-checked child cache (part ids are reused, CE-487) and the same own-echo
    /// skip (the recorded owner of the result part solved it). 📄 docs/DESIGN_World_Survey_Sensor.md §3.
    /// </summary>
    public sealed class WorldSurveyResultIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {

        private readonly DdsReader<WorldSurveyResultTopic>? _reader;
        private readonly NetworkEntityMap _entityMap;
        internal readonly Dictionary<(long ParentNetId, int ChildIndex), Entity> _childEntityCache = new();

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<WorldSurveyResultTopic>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtEqsResult;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public WorldSurveyResultIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader = participant != null ? new DdsReader<WorldSurveyResultTopic>(participant) : null;
        }

        /// <inheritdoc />
        public int DiscardIngress() => _reader.DiscardAll();   // ⭐ CE-3329 W5

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
                    var key = DdsTypeSupport.FromNative<WorldSurveyResultTopic>(sample.NativePtr);
                    _childEntityCache.Remove((key.ParentNetworkId, key.LocalChildIndex));
                    continue;
                }
                ReceivedSampleCount++;
                var data = sample.Data;
                ProcessSample(repo, in data);
            }
        }

        /// <summary>One received answer (exposed for tests: no DDS stack needed). Returns true when it raised the local event.</summary>
        internal bool ProcessSample(EntityRepository repo, in WorldSurveyResultTopic data)
        {
            if (data.ParentNetworkId == 0 || data.LocalChildIndex == 0) return false;   // a survey is always a child sensor
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
                if (observer.IsNull) return false;
                _childEntityCache[cacheKey] = observer;
            }
            // The solver's own answer looping back: its WorldSurveyApplySystem already took the local event.
            if (Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.IsRecordedOwner(
                    repo, observer, Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, data.LocalChildIndex)))
                return false;
            repo.Bus.PublishManaged(new WorldSurveyResultEvent
            {
                ParentNetworkId = data.ParentNetworkId,
                LocalChildIndex = data.LocalChildIndex,
                Epoch           = data.Epoch,
                RefreshTick     = data.RefreshTick,
                Observer        = observer,
                PayloadJson     = data.PayloadJson ?? string.Empty,
                ContentHash     = unchecked((ulong)data.ContentHash),
            });
            return true;
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
