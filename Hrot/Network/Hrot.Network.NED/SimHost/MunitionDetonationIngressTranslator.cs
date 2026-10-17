using System;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Damage-Assessment ingress translator: polls the <c>MunitionDetonation</c> DDS
    /// topic and re-publishes each decoded sample as a local <see cref="DetonationNotification"/>
    /// ECS event so that <c>DamageCalculationSystem</c> can compute HP loss.
    ///
    /// <para>
    /// Entity ID mapping: the <c>HitEntityId</c> long ID is validated against
    /// <see cref="NetworkEntityMap"/> to confirm the target is known on this node.
    /// If unknown, the sample is silently skipped.
    /// </para>
    /// </summary>
    public sealed class MunitionDetonationIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const string DdsTopicName = "MunitionDetonation";

        private readonly DdsReader<MunitionDetonation>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly long _localNodeId;   // ⭐ CE-1148 S2 — this node's own samples are dropped (SourceNodeId)

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtMunitionDetonation;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        /// <summary>
        /// Production constructor â€” creates a live DDS reader.
        /// Pass <c>null</c> for <paramref name="participant"/> in unit tests where no
        /// DDS infrastructure is available; <see cref="PollIngress"/> becomes a no-op.
        /// </summary>
        public MunitionDetonationIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap,
            long localNodeId)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
            _reader    = participant is not null
                ? new DdsReader<MunitionDetonation>(participant, DdsTopicName)
                : null;
        }

        /// <summary>
        /// Polls the DDS reader for incoming <see cref="MunitionDetonation"/> samples and
        /// republishes each valid sample as a <see cref="DetonationNotification"/> on the
        /// local event bus via the command buffer.
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
        /// Processes a single <see cref="MunitionDetonation"/> sample.
        /// Exposed as <c>internal</c> so unit tests can inject samples without a live DDS stack.
        /// </summary>
        internal void ProcessSample(
            in MunitionDetonation msg,
            IEntityCommandBuffer cmd,
            ISimulationView view)
        {
            // PACK-P003: DetonationNotification now carries Entity handles.
            // Resolve both network IDs to local Entity handles.
            // If the target is unknown on this node, skip (same guard as before).
            // ⭐ CE-1148 S2 (Node Roles §3.5, R-3015) — every node reads this topic; its own detonation is already a local event.
            if (msg.SourceNodeId != 0 && msg.SourceNodeId == _localNodeId) return;
            // ⭐ CE-1032 W-3 — HitEntityId 0 = a terrain burst: kept, with no target (the IG still shows it).
            var hitEntity = Entity.Null;
            if (msg.HitEntityId != 0 && !_entityMap.TryGetEntity(msg.HitEntityId, out hitEntity)) return;

            // Shooter may be unknown on Muscle (cross-node entity); default to Entity.Null if not found.
            _entityMap.TryGetEntity(msg.ShooterEntityId, out var shooterEntity);

            cmd.PublishEvent(new DetonationNotification
            {
                Shooter = shooterEntity,
                Target  = hitEntity,
                HitX    = msg.HitX,
                HitY    = msg.HitY,
                HitZ    = msg.HitZ,
                Ammo    = msg.MunitionType,
                IsRemote = true,
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
