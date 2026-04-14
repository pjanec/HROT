using System;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Combat.Contracts;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace Hrot.Network.NED.SimHost
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
    ///
    /// <para>
    /// <b>PACK-P003:</b> <see cref="DetonationNotification"/> now carries local ECS
    /// <see cref="Entity"/> handles.  This translator resolves them to network IDs via
    /// <see cref="NetworkEntityMap"/>.  If either entity is not in the map the event is
    /// logged and skipped â€” no exception is thrown.
    /// </para>
    /// </summary>
    public sealed class MunitionDetonationEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "MunitionDetonation";

        private readonly IDdsWriter<MunitionDetonation> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => 82;

        /// <summary>Production constructor â€” creates a live DDS writer.</summary>
        public MunitionDetonationEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : this(new DdsWriterAdapter<MunitionDetonation>(participant, DdsTopicName), entityMap)
        {
        }

        /// <summary>Testable constructor â€” accepts an injected writer stub.</summary>
        internal MunitionDetonationEgressTranslator(IDdsWriter<MunitionDetonation> writer, NetworkEntityMap entityMap)
        {
            _writer    = writer     ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap  ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Drains <see cref="DetonationNotification"/> events from the view and publishes a
        /// <see cref="MunitionDetonation"/> DDS message for each one.
        /// Resolves local ECS <see cref="Entity"/> handles to network IDs via
        /// <see cref="NetworkEntityMap"/>.  Skips events where either entity is unmapped.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var events = view.ConsumeEvents<DetonationNotification>();

            foreach (ref readonly var evt in events)
            {
                if (!_entityMap.TryGetNetworkId(evt.Shooter, out long shooterNetId))
                {
                    FdpLog<MunitionDetonationEgressTranslator>.Warn(
                        "[MunitionDetonationEgress] Shooter entity #{0} not in NetworkEntityMap â€” skipping detonation.",
                        evt.Shooter.Index);
                    continue;
                }

                if (!_entityMap.TryGetNetworkId(evt.Target, out long hitNetId))
                {
                    FdpLog<MunitionDetonationEgressTranslator>.Warn(
                        "[MunitionDetonationEgress] Target entity #{0} not in NetworkEntityMap â€” skipping detonation.",
                        evt.Target.Index);
                    continue;
                }

                _writer.Write(new MunitionDetonation
                {
                    ShooterEntityId = shooterNetId,
                    HitEntityId     = hitNetId,
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
