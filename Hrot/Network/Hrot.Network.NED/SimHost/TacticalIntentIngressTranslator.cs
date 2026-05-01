using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication.Services;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Subordinate Brain ingress translator: polls <see cref="TacticalIntentRequest"/>
    /// DDS samples and publishes each as an <see cref="AssignTacticalIntentEvent"/> on
    /// the local bus for <c>TacticalIntentResolutionSystem</c> to process.
    ///
    /// <para>Entity ID mapping: the <c>long</c> ID in the DDS message is resolved to a
    /// local <see cref="Entity"/> handle via <see cref="NetworkEntityMap"/>.
    /// If the entity is absent the sample is silently skipped.</para>
    ///
    /// <para>No authority check: authority verification is handled downstream by
    /// <c>TacticalIntentResolutionSystem</c>.</para>
    /// </summary>
    public sealed class TacticalIntentIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "TacticalIntentRequest";

        private readonly DdsReader<TacticalIntentRequest>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtTacticalIntentRequest;
        public long   ReceivedSampleCount { get; private set; }
        public long   SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        /// <summary>
        /// Production constructor. Pass <c>null</c> for <paramref name="participant"/>
        /// in unit tests; <see cref="PollIngress"/> becomes a no-op.
        /// </summary>
        public TacticalIntentIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader    = participant is not null
                ? new DdsReader<TacticalIntentRequest>(participant, DdsTopicName)
                : null;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;

            var repo = view as EntityRepository;
            if (repo is null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var data = sample.Data;
                ProcessSample(in data, repo);
            }
        }

        /// <summary>
        /// Processes a single <see cref="TacticalIntentRequest"/> sample.
        /// Exposed as <c>internal</c> so unit tests can inject samples without a live DDS stack.
        /// </summary>
        internal void ProcessSample(in TacticalIntentRequest request, EntityRepository repo)
        {
            if (!_entityMap.TryGetEntity(request.TargetEntityId, out var entity)) return;

            repo.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = request.IntentId   ?? string.Empty,
                JsonParams = request.JsonParams  ?? string.Empty,
            });
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
