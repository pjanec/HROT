using System;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.NED.IG
{
    /// <summary>
    /// IG-node ingress translator: polls the <c>WeaponFire</c> DDS topic and publishes
    /// each received sample as a local <see cref="WeaponFireNotification"/> on the IG's event bus.
    ///
    /// <para>
    /// <b>Entity resolution:</b> DDS long IDs are resolved to local <see cref="Entity"/> handles
    /// via the <see cref="NetworkEntityMap"/>. Unknown IDs resolve to <see cref="Entity.Null"/>;
    /// <see cref="Hrot.IG.Systems.EventToEffectSystem"/> skips tracers for null entities.
    /// </para>
    /// </summary>
    public sealed class WeaponFireIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const string DdsTopicName = "WeaponFire";

        private readonly DdsReader<WeaponFire>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly long _localNodeId;   // ⭐ CE-1148 S2 — this node's own shots are dropped (SourceNodeId)

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtWeaponFire;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        /// <summary>
        /// Production constructor -- creates a live DDS reader.
        /// Pass <c>null</c> for <paramref name="participant"/> in unit tests.
        /// </summary>
        public WeaponFireIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap,
            long localNodeId)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
            _reader    = participant is not null
                ? new DdsReader<WeaponFire>(participant, DdsTopicName)
                : null;
        }

        /// <summary>
        /// Polls the DDS reader for incoming <see cref="WeaponFire"/> samples and
        /// publishes each as a <see cref="WeaponFireNotification"/> on the local event bus.
        /// </summary>
        /// <summary>⭐ R-4007 (Q91 §4-M) — a one-shot world event: discarded while the node is frozen for a join.</summary>
        public TranslatorClass Category => TranslatorClass.WorldEvent;

        /// <inheritdoc/>
        public int DiscardIngress()
        {
            if (_reader is null) return 0;
            int discarded = 0;
            while (true)
            {
                using var loan = _reader.Take();
                if (loan.Count == 0) return discarded;
                discarded += loan.Count;
            }
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var data = sample.Data;
                ProcessSample(in data, cmd);
            }
        }

        /// <summary>
        /// Processes a single <see cref="WeaponFire"/> DDS sample.
        /// Exposed as <c>internal</c> so unit tests can inject samples without a live DDS stack.
        /// Unknown entity IDs resolve to <see cref="Entity.Null"/>.
        /// </summary>
        internal void ProcessSample(in WeaponFire msg, IEntityCommandBuffer cmd)
        {
            // ⭐ CE-1148 S2 (Node Roles §3.5, R-3015) — every node reads this topic; its own shot is already a local event.
            if (msg.SourceNodeId != 0 && msg.SourceNodeId == _localNodeId) return;
            _entityMap.TryGetEntity(msg.ShooterEntityId, out var shooter);
            _entityMap.TryGetEntity(msg.TargetEntityId,  out var target);

            var round = Entity.Null;   // ⭐ CE-1111 — a replicated round's id; 0 (a bullet, a decoy) stays null
            if (msg.RoundEntityId != 0) _entityMap.TryGetEntity(msg.RoundEntityId, out round);

            cmd.PublishEvent(new WeaponFireNotification
            {
                Shooter     = shooter,
                Target      = target,
                WeaponIndex = msg.WeaponIndex,
                IsRemote    = true,
                // ⭐ CE-1111 / CE-2188 (BS-1-DESIGN §5.4b F1, §5.4c) — the shot as the SimHost fired it
                AmmoGuid    = msg.AmmoGuid,
                Round       = round,
                Origin      = new System.Numerics.Vector3(msg.OriginX, msg.OriginY, msg.OriginZ),
                Velocity    = new System.Numerics.Vector3(msg.VelocityX, msg.VelocityY, msg.VelocityZ),
                Quantity    = msg.Quantity,
                FiredAt     = msg.FiredAt,
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
