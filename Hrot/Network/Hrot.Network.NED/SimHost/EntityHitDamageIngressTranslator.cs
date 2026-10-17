using System;
using Hrot.NED.Descriptors;
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
    public sealed class EntityHitDamageIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const string DdsTopicName = "EntityHitDamage";

        private readonly DdsReader<EntityHitDamage>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtEntityHitDamage;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

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
        /// <summary>⭐ R-4007 (Q91 §4-M) — a one-shot world event: discarded while the node is frozen for a join.</summary>
        public TranslatorClass Category => TranslatorClass.WorldEvent;

        /// <inheritdoc/>
        public int DiscardIngress()
        {
            if (_reader is null) return 0;
            int discarded = 0;
            while (true)
            {
                using var loan = _reader.Take();
                if (loan.Count == 0) return discarded;
                discarded += loan.Count;
            }
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
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
                HasBodyPart = (byte)(msg.BodyPart != 0 ? 1 : 0),   // ⭐ CE-3210 — the wire carries id + 1, 0 = none
                BodyPart    = msg.BodyPart != 0 ? (byte)(msg.BodyPart - 1) : Fdp.Toolkit.Physics.BodyParts.PersonPart.None,
                Effect        = (Fdp.Toolkit.Combat.HitEffect)msg.EffectKind,   // ⭐ CE-3219
                EffectSeconds = msg.EffectSeconds,
                // ⭐ CE-3237 (B-15, DS-2) — who caused it, as this node's entity; 0 or not known here ⇒ Null (unknown)
                Source        = msg.SourceEntityId != 0 && _entityMap.TryGetEntity(msg.SourceEntityId, out var source) ? source : Entity.Null,
                IsRemote    = true,
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
