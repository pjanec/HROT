using System;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.NED.SimHost
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
        private readonly NetworkEntityMap _entityMap;

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtEntityHitDamage;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        /// <summary>Production constructor â€” creates a live DDS writer.</summary>
        public DamageAssessedEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : this(new DdsWriterAdapter<EntityHitDamage>(participant, DdsTopicName), entityMap)
        {
        }

        /// <summary>Testable constructor â€” accepts an injected writer stub.</summary>
        internal DamageAssessedEgressTranslator(IDdsWriter<EntityHitDamage> writer, NetworkEntityMap entityMap)
        {
            _writer    = writer    ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Drains <see cref="DamageAssessedEvent"/> events from the view and publishes an
        /// <see cref="EntityHitDamage"/> DDS message for each one.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var events = view.ReadEvents<DamageAssessedEvent>();

            foreach (ref readonly var evt in events)
            {
                if (evt.IsRemote) continue;

                if (!_entityMap.TryGetNetworkId(evt.HitEntity, out long netId)) continue;
                _writer.Write(new EntityHitDamage
                {
                    HitEntityId = netId,
                    TotalDamage = evt.TotalDamage,
                });
                SentSampleCount++;
            }
        }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
