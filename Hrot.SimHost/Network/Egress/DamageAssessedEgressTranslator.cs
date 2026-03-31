using System;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Events;
using ModuleHost.Core.Abstractions;

namespace Hrot.SimHost.Network.Egress
{
    /// <summary>
    /// Damage-Assessment egress translator: consumes <see cref="DamageAssessedEvent"/> ECS
    /// events from the local event bus and publishes an <see cref="EntityHitDamage"/> DDS
    /// message for each one.
    ///
    /// <para>
    /// Registered on the authority node (Brain or collocated Muscle).  The authoritative node
    /// receives the <see cref="EntityHitDamage"/> message back via
    /// <c>EntityHitDamageIngressTranslator</c>, which republishes it as a local
    /// <see cref="DamageAssessedEvent"/> for <c>HealthApplicationSystem</c> to consume.
    /// </para>
    /// </summary>
    public sealed class DamageAssessedEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityHitDamage";

        private readonly IDdsWriter<EntityHitDamage> _writer;

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => 83;

        /// <summary>Production constructor — creates a live DDS writer.</summary>
        public DamageAssessedEgressTranslator(DdsParticipant participant)
            : this(new DdsWriterAdapter<EntityHitDamage>(participant, DdsTopicName))
        {
        }

        /// <summary>Testable constructor — accepts an injected writer stub.</summary>
        internal DamageAssessedEgressTranslator(IDdsWriter<EntityHitDamage> writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Drains <see cref="DamageAssessedEvent"/> events from the view and publishes an
        /// <see cref="EntityHitDamage"/> DDS message for each one.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var events = view.ConsumeEvents<DamageAssessedEvent>();

            foreach (ref readonly var evt in events)
            {
                _writer.Write(new EntityHitDamage
                {
                    HitEntityId = evt.HitEntityId,
                    TotalDamage = evt.TotalDamage,
                });
            }
        }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
