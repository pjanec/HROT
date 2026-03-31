using System;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Replication.Extensions;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace Hrot.SimHost.Network.Egress
{
    /// <summary>
    /// Brain-node egress translator: consumes <see cref="WeaponFireIntent"/> ECS events
    /// from the local event bus and publishes a <see cref="WeaponFireRequest"/> DDS message
    /// to the Muscle node.
    ///
    /// <para>
    /// <b>Authority gate:</b> Only publishes if the local node has authority for the
    /// shooter entity (i.e. the Brain is the cognitive owner).  Remote shooters are
    /// skipped to avoid duplicate publishes in a multi-Brain topology.
    /// </para>
    ///
    /// <para>
    /// <b>AllInOne compatibility:</b> In an AllInOne process <see cref="FireProcessingSystem"/>
    /// runs in the Input phase and consumes all <see cref="WeaponFireIntent"/> events before
    /// <see cref="ScanAndPublish"/> is reached in the egress phase.  The translator therefore
    /// finds an empty span and produces no DDS traffic, which is the desired behaviour.
    /// </para>
    /// </summary>
    public sealed class WeaponFireIntentEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "WeaponFireRequest";

        private readonly IDdsWriter<WeaponFireRequest> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => 80;

        /// <summary>Production constructor — creates a live DDS writer.</summary>
        public WeaponFireIntentEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : this(new DdsWriterAdapter<WeaponFireRequest>(participant, DdsTopicName), entityMap)
        {
        }

        /// <summary>Testable constructor — accepts an injected writer stub.</summary>
        internal WeaponFireIntentEgressTranslator(
            IDdsWriter<WeaponFireRequest> writer,
            NetworkEntityMap entityMap)
        {
            _writer    = writer    ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Drains <see cref="WeaponFireIntent"/> events from the view and publishes a
        /// <see cref="WeaponFireRequest"/> DDS message for each shooter entity that the
        /// local node has authority over.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var events = view.ConsumeEvents<WeaponFireIntent>();

            foreach (ref readonly var evt in events)
            {
                // Resolve the shooter entity to perform the authority check.
                // If the entity is not in the map it cannot be local — skip it.
                if (!_entityMap.TryGetEntity(evt.ShooterEntityId, out var shooterEntity))
                    continue;

                // Only publish fire intents for entities owned by this node.
                if (!view.HasAuthority(shooterEntity))
                    continue;

                _writer.Write(new WeaponFireRequest
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
