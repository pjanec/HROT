using System;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

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

        private readonly IDdsWriter<WeaponFire> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly long _localNodeId;   // ⭐ CE-1148 S2 — stamped as SourceNodeId so this node's own ingress drops it

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<WeaponFire>();
        public long   DescriptorOrdinal => (long)EDescriptorType.dtWeaponFire;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        /// <summary>Production constructor — creates a live DDS writer.</summary>
        public WeaponFireNotificationEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap, long localNodeId)
            : this(new DdsWriterAdapter<WeaponFire>(participant), entityMap, localNodeId)
        {
        }

        /// <summary>Testable constructor — accepts an injected writer stub.</summary>
        internal WeaponFireNotificationEgressTranslator(IDdsWriter<WeaponFire> writer, NetworkEntityMap entityMap, long localNodeId = 0)
        {
            _writer    = writer    ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
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
            var events = view.ReadEvents<WeaponFireNotification>();

            foreach (ref readonly var evt in events)
            {
                if (evt.IsRemote) continue;

                if (!_entityMap.TryGetNetworkId(evt.Shooter, out long shooterNetId))
                {
                    FdpLog<WeaponFireNotificationEgressTranslator>.Warn(
                        "[WeaponFireNotificationEgress] Shooter entity #{0} not in NetworkEntityMap — skipping notification.",
                        evt.Shooter.Index);
                    continue;
                }

                long targetNetId = 0;   // ⭐ CE-1032 W-8 — a point fire has no target: the shot (muzzle flash, sound) still goes out
                if (evt.Target != Entity.Null && !_entityMap.TryGetNetworkId(evt.Target, out targetNetId))
                {
                    FdpLog<WeaponFireNotificationEgressTranslator>.Warn(
                        "[WeaponFireNotificationEgress] Target entity #{0} not in NetworkEntityMap — skipping notification.",
                        evt.Target.Index);
                    continue;
                }

                // ⭐ CE-1111 (BS-1-DESIGN §5.4b F1) — the shot itself; the round's id when it is replicated (a visible round), else 0
                long roundNetId = 0;
                if (evt.Round != Entity.Null) _entityMap.TryGetNetworkId(evt.Round, out roundNetId);
                _writer.Write(new WeaponFire
                {
                    SourceNodeId = _localNodeId,
                    ShooterEntityId = shooterNetId,
                    TargetEntityId  = targetNetId,
                    WeaponIndex     = evt.WeaponIndex,
                    AmmoGuid        = evt.AmmoGuid,
                    RoundEntityId   = roundNetId,
                    OriginX = evt.Origin.X, OriginY = evt.Origin.Y, OriginZ = evt.Origin.Z,
                    VelocityX = evt.Velocity.X, VelocityY = evt.Velocity.Y, VelocityZ = evt.Velocity.Z,
                    Quantity        = evt.Quantity,
                    FiredAt         = evt.FiredAt,
                    RequestId       = evt.RequestId,        // ⭐ Q94 A-1 (R-3018)
                    Refusal         = (byte)evt.Refusal,
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
