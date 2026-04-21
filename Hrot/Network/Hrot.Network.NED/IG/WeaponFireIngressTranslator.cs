using System;
using Hrot.NED.Messages;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.NED.IG
{
    /// <summary>
    /// IG-node ingress translator: polls the <c>WeaponFire</c> DDS topic and publishes
    /// each received sample as a local <see cref="WeaponFireNotification"/> on the IG's event bus.
    ///
    /// <para>
    /// <b>Entity resolution:</b> DDS long IDs are resolved to local <see cref="Entity"/> handles
    /// via the <see cref="NetworkEntityMap"/>. Unknown IDs resolve to <see cref="Entity.Null"/>;
    /// <see cref="Hrot.IG.Systems.EventToEffectSystem"/> skips tracers for null entities.
    /// </para>
    /// </summary>
    public sealed class WeaponFireIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "WeaponFire";

        private readonly DdsReader<WeaponFire>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => 82;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        /// <summary>
        /// Production constructor -- creates a live DDS reader.
        /// Pass <c>null</c> for <paramref name="participant"/> in unit tests.
        /// </summary>
        public WeaponFireIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader    = participant is not null
                ? new DdsReader<WeaponFire>(participant, DdsTopicName)
                : null;
        }

        /// <summary>
        /// Polls the DDS reader for incoming <see cref="WeaponFire"/> samples and
        /// publishes each as a <see cref="WeaponFireNotification"/> on the local event bus.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var data = sample.Data;
                ProcessSample(in data, cmd);
            }
        }

        /// <summary>
        /// Processes a single <see cref="WeaponFire"/> DDS sample.
        /// Exposed as <c>internal</c> so unit tests can inject samples without a live DDS stack.
        /// Unknown entity IDs resolve to <see cref="Entity.Null"/>.
        /// </summary>
        internal void ProcessSample(in WeaponFire msg, IEntityCommandBuffer cmd)
        {
            _entityMap.TryGetEntity(msg.ShooterEntityId, out var shooter);
            _entityMap.TryGetEntity(msg.TargetEntityId,  out var target);

            cmd.PublishEvent(new WeaponFireNotification
            {
                Shooter     = shooter,
                Target      = target,
                WeaponIndex = msg.WeaponIndex,
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
