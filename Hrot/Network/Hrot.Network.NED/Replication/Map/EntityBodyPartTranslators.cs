using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Physics.BodyParts;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-3210</c> (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md HL-6) — publishes a body's <see cref="PartDamage"/>
    /// from the node owning its <c>dtPartDamage</c> (the Health owner, Brain group), on change. The <c>EntityWeaponAmmo</c> pattern.
    /// </summary>
    public sealed class EntityPartDamageEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityPartDamage";
        private readonly DdsWriter<EntityPartDamage>? _writer;
        private readonly Dictionary<long, EntityPartDamage> _last = new();
        private int _worldEpoch;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtPartDamage;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityPartDamageEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityPartDamage>(participant, DdsTopicName) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch)) _last.Clear();
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<PartDamage>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var e in view.Query().With<PartDamage>().With<NetworkIdentity>().Build())
            {
                if (!view.HasAuthority(e, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                var sample = ToSample(netId, view.GetComponentRO<PartDamage>(e));
                if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static unsafe EntityPartDamage ToSample(long netId, in PartDamage d)
        {
            var s = new EntityPartDamage { EntityId = netId, Plan = (byte)d.Plan };
            s.L0 = d.LevelOf(0); s.L1 = d.LevelOf(1); s.L2 = d.LevelOf(2); s.L3 = d.LevelOf(3);
            s.L4 = d.LevelOf(4); s.L5 = d.LevelOf(5); s.L6 = d.LevelOf(6); s.L7 = d.LevelOf(7);
            s.L8 = d.LevelOf(8); s.L9 = d.LevelOf(9); s.L10 = d.LevelOf(10); s.L11 = d.LevelOf(11);
            s.L12 = d.LevelOf(12); s.L13 = d.LevelOf(13); s.L14 = d.LevelOf(14); s.L15 = d.LevelOf(15);
            return s;
        }

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>
    /// ⭐ <c>CE-3210</c> (HL-8) — publishes a body's <see cref="HitMarks"/> from the node owning its <c>dtHitMarks</c> (the Muscle, which
    /// resolves the hits), when a mark was added (its <c>NextSeq</c> moved). The marks still in the ring go out with their sequences.
    /// </summary>
    public sealed class EntityHitMarksEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityHitMarks";
        private readonly DdsWriter<EntityHitMarks>? _writer;
        private readonly Dictionary<long, ushort> _lastSeq = new();
        private int _worldEpoch;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtHitMarks;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityHitMarksEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityHitMarks>(participant, DdsTopicName) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch)) _lastSeq.Clear();
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<HitMarks>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var e in view.Query().With<HitMarks>().With<NetworkIdentity>().Build())
            {
                if (!view.HasAuthority(e, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                var marks = view.GetComponentRO<HitMarks>(e);
                if (_lastSeq.TryGetValue(netId, out var prev) && prev == marks.NextSeq) continue;
                _lastSeq[netId] = marks.NextSeq;
                _writer?.Write(ToSample(netId, marks));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityHitMarks ToSample(long netId, in HitMarks m)
        {
            var list = new List<DdsHitMark>(m.Count);
            for (int seq = Math.Max(0, m.NextSeq - HitMarks.Capacity); seq < m.NextSeq; seq++)
            {
                if (!m.TryGet(seq, out var part, out var local, out var face, out var kind)) continue;
                list.Add(new DdsHitMark
                {
                    Seq = (ushort)seq, Part = part, X = HitMarks.Quantise(local.X), Y = HitMarks.Quantise(local.Y), Z = HitMarks.Quantise(local.Z),
                    Face = face, Kind = (byte)kind,
                });
            }
            return new EntityHitMarks { EntityId = netId, NextSeq = m.NextSeq, Marks = list };
        }

        public void Dispose(long networkEntityId) => _lastSeq.Remove(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-3210</c> (HL-6) — applies a body's published <see cref="PartDamage"/> on this node (wounds are derived from it).
    /// An unknown entity is dropped; the recorded owner never takes its own sample back.</summary>
    public sealed class EntityPartDamageIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityPartDamage";
        private const long OrdinalValue = (long)EDescriptorType.dtPartDamage;
        private readonly DdsReader<EntityPartDamage>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityPartDamageIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityPartDamage>(participant, DdsTopicName) : null;
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

        /// <summary>Applies one sample (the test seam).</summary>
        internal unsafe void Apply(in EntityPartDamage d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var e) || !view.IsAlive(e)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo && AuthorityExtensions.IsRecordedOwner(repo, e, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            var p = new PartDamage { Plan = (BodyPlan)d.Plan };
            p.Levels[0] = d.L0; p.Levels[1] = d.L1; p.Levels[2] = d.L2; p.Levels[3] = d.L3;
            p.Levels[4] = d.L4; p.Levels[5] = d.L5; p.Levels[6] = d.L6; p.Levels[7] = d.L7;
            p.Levels[8] = d.L8; p.Levels[9] = d.L9; p.Levels[10] = d.L10; p.Levels[11] = d.L11;
            p.Levels[12] = d.L12; p.Levels[13] = d.L13; p.Levels[14] = d.L14; p.Levels[15] = d.L15;
            cmd.AddComponent(e, p);
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>⭐ <c>CE-3210</c> (HL-8) — applies a body's published <see cref="HitMarks"/> on this node (every renderer draws them).</summary>
    public sealed class EntityHitMarksIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityHitMarks";
        private const long OrdinalValue = (long)EDescriptorType.dtHitMarks;
        private readonly DdsReader<EntityHitMarks>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityHitMarksIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityHitMarks>(participant, DdsTopicName) : null;
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

        /// <summary>Applies one sample (the test seam): the ring rebuilt from the marks it carries, in sequence order.</summary>
        internal void Apply(in EntityHitMarks d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var e) || !view.IsAlive(e)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo && AuthorityExtensions.IsRecordedOwner(repo, e, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            var m = new HitMarks();
            if (d.Marks != null && d.Marks.Count > 0)
            {
                m.NextSeq = (ushort)Math.Max(0, d.NextSeq - d.Marks.Count);
                foreach (var k in d.Marks)
                {
                    m.NextSeq = k.Seq;
                    m.Add(k.Part, new System.Numerics.Vector3(HitMarks.Unquantise(k.X), HitMarks.Unquantise(k.Y), HitMarks.Unquantise(k.Z)),
                          k.Face, (HitMarkKind)k.Kind);
                }
            }
            m.NextSeq = d.NextSeq;
            cmd.AddComponent(e, m);
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
