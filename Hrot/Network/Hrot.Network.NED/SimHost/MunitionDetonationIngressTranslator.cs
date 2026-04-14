using System;
using Hrot.NED.Messages;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Contracts;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

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
    public sealed class MunitionDetonationIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "MunitionDetonation";

        private readonly DdsReader<MunitionDetonation>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => 82;

        /// <summary>
        /// Production constructor â€” creates a live DDS reader.
        /// Pass <c>null</c> for <paramref name="participant"/> in unit tests where no
        /// DDS infrastructure is available; <see cref="PollIngress"/> becomes a no-op.
        /// </summary>
        public MunitionDetonationIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader    = participant is not null
                ? new DdsReader<MunitionDetonation>(participant, DdsTopicName)
                : null;
        }

        /// <summary>
        /// Polls the DDS reader for incoming <see cref="MunitionDetonation"/> samples and
        /// republishes each valid sample as a <see cref="DetonationNotification"/> on the
        /// local event bus via the command buffer.
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
            if (!_entityMap.TryGetEntity(msg.HitEntityId, out var hitEntity)) return;

            // Shooter may be unknown on Muscle (cross-node entity); default to Entity.Null if not found.
            _entityMap.TryGetEntity(msg.ShooterEntityId, out var shooterEntity);

            cmd.PublishEvent(new DetonationNotification
            {
                Shooter = shooterEntity,
                Target  = hitEntity,
                HitX    = msg.HitX,
                HitY    = msg.HitY,
                HitZ    = msg.HitZ,
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
