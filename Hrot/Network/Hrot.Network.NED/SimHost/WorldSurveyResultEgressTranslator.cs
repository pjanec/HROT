using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Survey;
using Fdp.Toolkit.Perception.Survey.Topics;
using Fdp.Toolkit.Replication.Services;
using Hrot.NED.Descriptors;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// ⭐ <c>CE-3249</c> S4 — solver side: a WorldSurvey sensor's answer (<see cref="WorldSurveyResultEvent"/>, published by
    /// <c>EqsSolverSystem</c>) onto <see cref="WorldSurveyResultTopic"/>, for the brain. The twin of <see cref="DangerAreaResultEgressTranslator"/>,
    /// with the same gate: only the node that OWNS the sensor's result part (<c>dtEqsResult</c>, part) — the solver the brain picked —
    /// publishes. 📄 docs/DESIGN_World_Survey_Sensor.md §3.
    /// </summary>
    public sealed class WorldSurveyResultEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "WorldSurveyResult";

        private readonly DdsWriter<WorldSurveyResultTopic>? _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtEqsResult;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public WorldSurveyResultEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _writer    = new DdsWriter<WorldSurveyResultTopic>(participant, DdsTopicName);
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;
            foreach (var evt in view.ReadManagedEvents<WorldSurveyResultEvent>())
            {
                if (evt is null || evt.ParentNetworkId == 0) continue;   // a local-only sensor is never on the wire
                if (!evt.Observer.IsNull) continue;                       // an answer that ARRIVED from the wire
                if (_entityMap.TryGetEntity(evt.ParentNetworkId, out var parent) && view.IsAlive(parent) &&
                    !Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(
                        view, parent, Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, evt.LocalChildIndex)))
                    continue;
                _writer.Write(new WorldSurveyResultTopic
                {
                    ParentNetworkId = evt.ParentNetworkId,
                    LocalChildIndex = evt.LocalChildIndex,
                    Epoch           = evt.Epoch,
                    RefreshTick     = evt.RefreshTick,
                    ContentHash     = unchecked((long)evt.ContentHash),
                    PayloadJson     = evt.PayloadJson ?? string.Empty,
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
