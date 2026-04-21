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
    /// Muscle-node ingress translator: polls the <c>WeaponFireRequest</c> DDS topic and
    /// re-publishes each decoded sample as a local <see cref="WeaponFireIntent"/> ECS event
    /// on the Muscle's event bus.
    ///
    /// <para>
    /// Entity ID mapping: the <c>long</c> IDs in the DDS message are resolved to local
    /// <see cref="Entity"/> handles via <see cref="NetworkEntityMap"/> to confirm that
    /// both shooter and target are known on this node.  If either entity is absent the
    /// sample is silently skipped â€” <see cref="FireProcessingSystem"/> requires valid
    /// entity handles to spawn a bullet.
    /// </para>
    /// </summary>
    public sealed class WeaponFireRequestIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "WeaponFireRequest";

        private readonly DdsReader<WeaponFireRequest>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => 80;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        /// <summary>
        /// Production constructor â€” creates a live DDS reader.
        /// Pass <c>null</c> for <paramref name="participant"/> in unit tests where no
        /// DDS infrastructure is available; <see cref="PollIngress"/> becomes a no-op.
        /// </summary>
        public WeaponFireRequestIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader    = participant is not null
                ? new DdsReader<WeaponFireRequest>(participant, DdsTopicName)
                : null;
        }

        /// <summary>
        /// Polls the DDS reader for incoming <see cref="WeaponFireRequest"/> samples and
        /// republishes each valid sample as a <see cref="WeaponFireIntent"/> on the local
        /// event bus via the command buffer.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;    // test / no-participant mode

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
        /// Processes a single <see cref="WeaponFireRequest"/> sample.
        /// Exposed as <c>internal</c> so unit tests can inject samples without a live DDS stack.
        /// </summary>
        internal void ProcessSample(
            in WeaponFireRequest request,
            IEntityCommandBuffer cmd,
            ISimulationView view)
        {
            // Both shooter and target must be known on this node for FireProcessingSystem to use.
            if (!_entityMap.TryGetEntity(request.ShooterEntityId, out var shooter)) return;
            if (!_entityMap.TryGetEntity(request.TargetEntityId,  out var target))  return;

            cmd.PublishEvent(new WeaponFireIntent
            {
                Shooter     = shooter,
                Target      = target,
                WeaponIndex = request.WeaponIndex,
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
