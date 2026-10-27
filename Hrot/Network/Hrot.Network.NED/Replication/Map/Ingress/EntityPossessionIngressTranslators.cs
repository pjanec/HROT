using System;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ <c>CE-3173</c> S4e (docs/DESIGN_Entity_Control.md §8c) — applies an operator's published <see cref="ManualDrive"/> to this
    /// node's unit, where its muscle (<c>CarKinematicsSystem</c>'s Manual branch) reads it — the latest sample wins. ⚠ No <c>Seq</c>
    /// check here: the ONE writer is the brain, whose samples arrive in order, and its let-go (axes zeroed after a release) and a new
    /// station's frames both restart the sequence — M-6's newest-seq rule is the brain's, on the frames it receives. An unknown unit is
    /// dropped; the recorded owner never takes its own sample back.
    /// </summary>
    public sealed class EntityManualDriveIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const string DdsTopicName = "EntityManualDrive";
        private const long OrdinalValue = (long)EDescriptorType.dtManualDrive;
        private readonly DdsReader<EntityManualDrive>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityManualDriveIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityManualDrive>(participant, DdsTopicName) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc />
        public int DiscardIngress() => _reader.DiscardAll();   // ⭐ CE-3329 W5

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive) continue;
                ReceivedSampleCount++;
                Apply(sample.Data, cmd, view);
            }
        }

        /// <summary>Applies one sample (the test seam).</summary>
        internal void Apply(in EntityManualDrive d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var unit) || !view.IsAlive(unit)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, unit, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            cmd.AddComponent(unit, new ManualDrive
            {
                Forward = d.Forward, Lateral = d.Lateral, Steer = d.Steer, Yaw = d.Yaw, Gait = d.Gait, Seq = d.Seq, Vertical = d.Vertical,
            });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// ⭐ <c>CE-3172</c> S4e (§8c, C-3) — applies who holds a unit on every node: <c>Held = 1</c> sets <see cref="PossessedBy"/>,
    /// <c>Held = 0</c> removes it. A station reads it to know it holds (or lost) the unit.
    /// </summary>
    public sealed class EntityPossessedByIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const string DdsTopicName = "EntityPossessedBy";
        private const long OrdinalValue = (long)EDescriptorType.dtPossessedBy;
        private readonly DdsReader<EntityPossessedBy>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityPossessedByIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityPossessedBy>(participant, DdsTopicName) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc />
        public int DiscardIngress() => _reader.DiscardAll();   // ⭐ CE-3329 W5

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive) continue;
                ReceivedSampleCount++;
                Apply(sample.Data, cmd, view);
            }
        }

        /// <summary>Applies one sample (the test seam).</summary>
        internal void Apply(in EntityPossessedBy d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var unit) || !view.IsAlive(unit)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, unit, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            if (d.Held == 0)
            {
                if ((view is not EntityRepository r || r.IsComponentTypeRegistered<PossessedBy>()) && view.HasComponent<PossessedBy>(unit))
                    cmd.RemoveComponent<PossessedBy>(unit);
                return;
            }
            cmd.AddComponent(unit, new PossessedBy { StationNodeId = d.StationNodeId, Mode = (PossessionMode)d.Mode, Epoch = d.Epoch });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
