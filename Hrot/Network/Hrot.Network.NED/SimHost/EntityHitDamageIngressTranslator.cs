using System;
using Hrot.NED.Messages;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Authority-node ingress translator: polls the <c>EntityHitDamage</c> DDS topic and
    /// re-publishes each decoded sample as a local <see cref="DamageAssessedEvent"/> ECS
    /// event for <c>HealthApplicationSystem</c> to apply.
    ///
    /// <para>
    /// Entity ID mapping: the <c>HitEntityId</c> long is validated against
    /// <see cref="NetworkEntityMap"/> to confirm the target is known on this node.
    /// If unknown, the sample is silently skipped.
    /// </para>
    /// </summary>
    public sealed class EntityHitDamageIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityHitDamage";

        private readonly DdsReader<EntityHitDamage>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => 83;

        /// <summary>
        /// Production constructor â€” creates a live DDS reader.
        /// Pass <c>null</c> for <paramref name="participant"/> in unit tests.
        /// </summary>
        public EntityHitDamageIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader    = participant is not null
                ? new DdsReader<EntityHitDamage>(participant, DdsTopicName)
                : null;
        }

        /// <summary>
        /// Polls the DDS reader for incoming <see cref="EntityHitDamage"/> samples and
        /// publishes each valid sample as a <see cref="DamageAssessedEvent"/> on the local
        /// event bus via the command buffer.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                var data = sample.Data;
                ProcessSample(in data, cmd, view);
            }
        }

        /// <summary>
        /// Processes a single <see cref="EntityHitDamage"/> sample.
        /// Exposed as <c>internal</c> so unit tests can inject samples without a live DDS stack.
        /// </summary>
        internal void ProcessSample(
            in EntityHitDamage msg,
            IEntityCommandBuffer cmd,
            ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(msg.HitEntityId, out var hitEntity)) return;

            cmd.PublishEvent(new DamageAssessedEvent
            {
                HitEntity   = hitEntity,
                TotalDamage = msg.TotalDamage,
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
