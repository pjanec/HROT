using System;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Contracts;
using ModuleHost.Core.Abstractions;

namespace Hrot.SimHost.Network.Egress
{
    /// <summary>
    /// Muscle-node egress translator: consumes <see cref="DetonationNotification"/> ECS events
    /// from the local event bus and publishes a <see cref="MunitionDetonation"/> DDS message.
    ///
    /// <para>
    /// Consumed by the IG for explosion particle effects and by the Damage Assessment Module
    /// (via <c>MunitionDetonationIngressTranslator</c>) to trigger damage computation.
    /// </para>
    ///
    /// <para>
    /// <b>No authority check:</b> <see cref="DetonationNotification"/> is only ever emitted by
    /// <c>HitResolutionSystem</c> on the authoritative Muscle node; a guard is therefore
    /// unnecessary.
    /// </para>
    /// </summary>
    public sealed class MunitionDetonationEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "MunitionDetonation";

        private readonly IDdsWriter<MunitionDetonation> _writer;

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => 82;

        /// <summary>Production constructor — creates a live DDS writer.</summary>
        public MunitionDetonationEgressTranslator(DdsParticipant participant)
            : this(new DdsWriterAdapter<MunitionDetonation>(participant, DdsTopicName))
        {
        }

        /// <summary>Testable constructor — accepts an injected writer stub.</summary>
        internal MunitionDetonationEgressTranslator(IDdsWriter<MunitionDetonation> writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Drains <see cref="DetonationNotification"/> events from the view and publishes a
        /// <see cref="MunitionDetonation"/> DDS message for each one.
        /// Hit coordinates are copied directly (no coordinate transform).
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var events = view.ConsumeEvents<DetonationNotification>();

            foreach (ref readonly var evt in events)
            {
                _writer.Write(new MunitionDetonation
                {
                    ShooterEntityId = evt.ShooterEntityId,
                    HitEntityId     = evt.HitEntityId,
                    HitX            = evt.HitX,
                    HitY            = evt.HitY,
                    HitZ            = evt.HitZ,
                });
            }
        }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
