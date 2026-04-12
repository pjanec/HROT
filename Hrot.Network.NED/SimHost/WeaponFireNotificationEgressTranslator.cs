using System;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace Hrot.Network.NED.SimHost
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
    ///
    /// <para>
    /// <b>PACK-P003:</b> <see cref="WeaponFireNotification"/> now carries local ECS
    /// <see cref="Entity"/> handles. This translator resolves them to network IDs via
    /// <see cref="NetworkEntityMap"/>.  If either entity is not in the map the event is
    /// skipped without an exception.
    /// </para>
    /// </summary>
    public sealed class WeaponFireNotificationEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "WeaponFire";

        private readonly IDdsWriter<WeaponFire> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => 81;

        /// <summary>Production constructor — creates a live DDS writer.</summary>
        public WeaponFireNotificationEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : this(new DdsWriterAdapter<WeaponFire>(participant, DdsTopicName), entityMap)
        {
        }

        /// <summary>Testable constructor — accepts an injected writer stub.</summary>
        internal WeaponFireNotificationEgressTranslator(IDdsWriter<WeaponFire> writer, NetworkEntityMap entityMap)
        {
            _writer    = writer    ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Drains <see cref="WeaponFireNotification"/> events from the view and publishes a
        /// <see cref="WeaponFire"/> DDS message for each one.
        /// Resolves local ECS <see cref="Entity"/> handles to network IDs via
        /// <see cref="NetworkEntityMap"/>.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var events = view.ConsumeEvents<WeaponFireNotification>();

            foreach (ref readonly var evt in events)
            {
                if (!_entityMap.TryGetNetworkId(evt.Shooter, out long shooterNetId))
                {
                    FdpLog<WeaponFireNotificationEgressTranslator>.Warn(
                        "[WeaponFireNotificationEgress] Shooter entity #{0} not in NetworkEntityMap — skipping notification.",
                        evt.Shooter.Index);
                    continue;
                }

                if (!_entityMap.TryGetNetworkId(evt.Target, out long targetNetId))
                {
                    FdpLog<WeaponFireNotificationEgressTranslator>.Warn(
                        "[WeaponFireNotificationEgress] Target entity #{0} not in NetworkEntityMap — skipping notification.",
                        evt.Target.Index);
                    continue;
                }

                _writer.Write(new WeaponFire
                {
                    ShooterEntityId = shooterNetId,
                    TargetEntityId  = targetNetId,
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
