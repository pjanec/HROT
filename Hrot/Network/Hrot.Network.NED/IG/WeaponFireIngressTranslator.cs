using System;
using Hrot.NED.Messages;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.NED.IG
{
    /// <summary>
    /// IG-node ingress translator: polls the <c>WeaponFire</c> DDS topic and publishes
    /// each received sample as a local <see cref="Hrot.IG.IgWeaponFireEvent"/> on the IG's event bus.
    ///
    /// <para>
    /// <b>Unknown-entity tolerance:</b> The IG event is always published regardless of whether
    /// the shooter or target is present in the local <see cref="NetworkEntityMap"/>. The entity
    /// may have been destroyed between the muzzle-flash emit and its DDS delivery, but the IG
    /// visual layer can still draw a tracer or muzzle-flash by position if it chooses to.
    /// </para>
    /// </summary>
    public sealed class WeaponFireIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "WeaponFire";

        private readonly DdsReader<WeaponFire>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => 82;

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
        /// publishes each as an <see cref="Hrot.IG.IgWeaponFireEvent"/> on the local event bus.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                var data = sample.Data;
                ProcessSample(in data, cmd);
            }
        }

        /// <summary>
        /// Processes a single <see cref="WeaponFire"/> DDS sample.
        /// Exposed as <c>internal</c> so unit tests can inject samples without a live DDS stack.
        /// Always publishes <see cref="Hrot.IG.IgWeaponFireEvent"/> -- unknown entity IDs are tolerated.
        /// </summary>
        internal void ProcessSample(in WeaponFire msg, IEntityCommandBuffer cmd)
        {
            cmd.PublishEvent(new Hrot.IG.IgWeaponFireEvent
            {
                ShooterEntityId = msg.ShooterEntityId,
                TargetEntityId  = msg.TargetEntityId,
                WeaponIndex     = msg.WeaponIndex,
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
