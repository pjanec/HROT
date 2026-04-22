using System;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.NED.SimHost
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
    ///
    /// <para>
    /// <b>PACK-P003:</b> <see cref="WeaponFireIntent"/> now carries local ECS
    /// <see cref="Entity"/> handles. The authority check uses the <c>Shooter</c> entity
    /// directly; network IDs are resolved via <see cref="NetworkEntityMap"/> only when
    /// writing the DDS wire message.  If either entity is unmapped the event is skipped
    /// without an exception.
    /// </para>
    /// </summary>
    public sealed class WeaponFireIntentEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "WeaponFireRequest";

        private readonly IDdsWriter<WeaponFireRequest> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtWeaponFireRequest;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

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
            var events = view.ReadEvents<WeaponFireIntent>();

            foreach (ref readonly var evt in events)
            {
                if (evt.IsRemote) continue;

                // Authority check: only publish for locally-owned shooter entities.
                if (!view.HasAuthority(evt.Shooter))
                    continue;

                // Resolve shooter entity → network ID for the DDS wire message.
                if (!_entityMap.TryGetNetworkId(evt.Shooter, out long shooterNetId))
                {
                    FdpLog<WeaponFireIntentEgressTranslator>.Warn(
                        "[WeaponFireIntentEgress] Shooter entity #{0} not in NetworkEntityMap — skipping intent.",
                        evt.Shooter.Index);
                    continue;
                }

                // Resolve target entity → network ID for the DDS wire message.
                if (!_entityMap.TryGetNetworkId(evt.Target, out long targetNetId))
                {
                    FdpLog<WeaponFireIntentEgressTranslator>.Warn(
                        "[WeaponFireIntentEgress] Target entity #{0} not in NetworkEntityMap — skipping intent.",
                        evt.Target.Index);
                    continue;
                }

                _writer.Write(new WeaponFireRequest
                {
                    ShooterEntityId = shooterNetId,
                    TargetEntityId  = targetNetId,
                    WeaponIndex     = evt.WeaponIndex,
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
