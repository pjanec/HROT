using System;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ WL-6 (docs/DESIGN_Weapon_Layers.md W-13d, R-272) — applies a published <see cref="WeaponPosture"/> to this node's unit, so every
    /// host draws what the weapon brain decided. The target's network id becomes this node's entity (unknown ⇒ <see cref="Entity.Null"/>:
    /// the figure then holds the rifle forward). An unknown unit is dropped (Reliable + TransientLocal re-sends on the next change); the
    /// recorded owner never takes its own sample back.
    /// </summary>
    public sealed class EntityWeaponPostureIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityWeaponPosture";
        private const long OrdinalValue = (long)EDescriptorType.dtWeaponPosture;
        private readonly DdsReader<EntityWeaponPosture>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityWeaponPostureIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityWeaponPosture>(participant, DdsTopicName) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

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

        /// <summary>Applies one sample (the test seam: a live participant is not needed to check the mapping).</summary>
        internal void Apply(in EntityWeaponPosture d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var unit) || !view.IsAlive(unit)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, unit, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            var target = Entity.Null;
            if (d.TargetNetworkId != 0 && !_entityMap.TryGetEntity(d.TargetNetworkId, out target)) target = Entity.Null;
            cmd.AddComponent(unit, new WeaponPosture
            {
                Kind = (WeaponPostureKind)d.Kind, Target = target, Point = new System.Numerics.Vector3(d.PointX, d.PointY, d.PointZ),
            });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
