using System;
using Bagira.BDC.SSTM;
using Bagira.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Events;
using ModuleHost.Core.Abstractions;

namespace Bagira.SimHost.Network.Egress
{
    /// <summary>
    /// Muscle-node egress translator: consumes <see cref="WeaponFireNotification"/> ECS events
    /// from the local event bus and publishes a <see cref="WeaponFire"/> DDS message for the IG
    /// to trigger a muzzle-flash visual effect.
    ///
    /// <para>
    /// <b>No authority check:</b> <see cref="WeaponFireNotification"/> is only ever emitted by
    /// <see cref="FireProcessingSystem"/> on the authoritative Muscle node.  A guard is therefore
    /// unnecessary and would only introduce noise.
    /// </para>
    /// </summary>
    public sealed class WeaponFireNotificationEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "WeaponFire";

        private readonly IDdsWriter<WeaponFire> _writer;

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => 81;

        /// <summary>Production constructor — creates a live DDS writer.</summary>
        public WeaponFireNotificationEgressTranslator(DdsParticipant participant)
            : this(new DdsWriterAdapter<WeaponFire>(participant, DdsTopicName))
        {
        }

        /// <summary>Testable constructor — accepts an injected writer stub.</summary>
        internal WeaponFireNotificationEgressTranslator(IDdsWriter<WeaponFire> writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Drains <see cref="WeaponFireNotification"/> events from the view and publishes a
        /// <see cref="WeaponFire"/> DDS message for each one.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var events = view.ConsumeEvents<WeaponFireNotification>();

            foreach (ref readonly var evt in events)
            {
                _writer.Write(new WeaponFire
                {
                    ShooterEntityId = evt.ShooterEntityId,
                    TargetEntityId  = evt.TargetEntityId,
                    WeaponIndex     = evt.WeaponIndex,
                });
            }
        }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
