using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐⭐ <c>CE-8001</c> (CE-4616 ST-0 ③, RV-10; docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md) — publishes a unit's
    /// <see cref="AggregateUnit"/> from the node owning its <c>dtAggregateUnit</c> (the brain owner), on change. The <c>dtRadarEmission</c> shape.
    /// ⛔ The suppression ring and the step stamp stay node-local (the attrition runs where the brain runs); every other field crosses.
    /// </summary>
    public sealed class EntityAggregateUnitEgressTranslator : IDescriptorTranslator, Fdp.Network.Cyclone.Egress.IOffFrameEgress
    {
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityAggregateUnit> _instances;
        private static readonly int[] s_targetIds = { GlobalComponentIds.AggregateUnit };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityAggregateUnit>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtAggregateUnit;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityAggregateUnitEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityAggregateUnit>(participant) : null, static key => new EntityAggregateUnit { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ScanAndPublish(ISimulationView view) { BeginFrame(view); ScanState(view, offFrame: false); }
        public void BeginFrame(ISimulationView view) => _instances.OnFrame(view);

        public void ScanState(ISimulationView view, bool offFrame)
        {
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<AggregateUnit>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<AggregateUnit>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var sample = ToSample(netId, view.GetComponentRO<AggregateUnit>(unit));
                if (_instances.Unchanged(netId, sample)) continue;
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityAggregateUnit ToSample(int netId, in AggregateUnit u) => new()
        {
            EntityId = netId, Resolution = (byte)u.Resolution, Echelon = u.Echelon, Splitting = (byte)u.Splitting, Pin = (byte)u.Pin,
            Formation = (byte)u.Formation, Posture = (byte)u.Posture, Activity = (byte)u.Activity, Prepared = (byte)u.Prepared, Move = (byte)u.Move,
            Width = u.Width, Depth = u.Depth, HeadingDeg = u.Heading.Degrees,
            DestX = u.Destination.X, DestY = u.Destination.Y, DestZ = u.Destination.Z,
            PathDistanceM = u.PathDistanceM, SpeedMps = u.SpeedMps, SuppressedShare = u.SuppressedShare,
            RouteRequestedAt = u.RouteRequestedAt, LastFiredAt = u.LastFiredAt, LastContactAt = u.LastContactAt,
            PreparedSince = u.PreparedSince, SplittingSince = u.SplittingSince,
        };

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-8001</c> — publishes a unit's <see cref="UnitSlotTable"/> (the slots in use) from the <c>dtUnitSlotTable</c> owner, when the
    /// table changed (compared as bytes against the last one sent — 0 B while it does not change).
    /// </summary>
    public sealed class EntityUnitSlotTableEgressTranslator : IDescriptorTranslator, Fdp.Network.Cyclone.Egress.IOffFrameEgress
    {
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityUnitSlotTable> _instances;
        private readonly Dictionary<int, UnitSlotTable> _sent = new();
        private static readonly int[] s_targetIds = { GlobalComponentIds.UnitSlotTable };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityUnitSlotTable>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtUnitSlotTable;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityUnitSlotTableEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityUnitSlotTable>(participant) : null, static key => new EntityUnitSlotTable { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ScanAndPublish(ISimulationView view) { BeginFrame(view); ScanState(view, offFrame: false); }
        public void BeginFrame(ISimulationView view) { if (_instances.OnFrame(view)) _sent.Clear(); }

        public void ScanState(ISimulationView view, bool offFrame)
        {
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<UnitSlotTable>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<UnitSlotTable>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                ref readonly var t = ref view.GetComponentRO<UnitSlotTable>(unit);
                if (_sent.TryGetValue(netId, out var prev) && _instances.TryGetLast(netId, out _)
                    && Fdp.Network.Cyclone.Translators.SampleEquality.Same(prev, t)) continue;
                _sent[netId] = t;
                _instances.Write(netId, ToSample(netId, t));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam): the slots in use, Health / Stress as their Half bits.</summary>
        internal static EntityUnitSlotTable ToSample(int netId, in UnitSlotTable t)
        {
            int n = Math.Min((int)t.Count, UnitSlotTable.Capacity);
            var slots = new List<DdsUnitSlot>(n);
            for (int i = 0; i < n; i++)
            {
                var s = t.Slots[i];
                slots.Add(new DdsUnitSlot
                {
                    HealthBits = BitConverter.HalfToUInt16Bits(s.Health), StressBits = BitConverter.HalfToUInt16Bits(s.Stress),
                    Ammo = s.Ammo, Role = (byte)s.Role, CompositeSlot = s.CompositeSlot,
                });
            }
            return new EntityUnitSlotTable { EntityId = netId, Chunk = t.Chunk, Slots = slots };
        }

        public void Dispose(int networkEntityId) { _sent.Remove(networkEntityId); _instances.Dispose(networkEntityId); }
        public void ReleaseInstance(int networkEntityId, long instanceId) { _sent.Remove(networkEntityId); _instances.Release(networkEntityId); }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>⭐⭐ <c>CE-8001</c> — publishes a unit's <see cref="UnitHoldings"/> from the <c>dtUnitHoldings</c> owner, when it changed.</summary>
    public sealed class EntityUnitHoldingsEgressTranslator : IDescriptorTranslator, Fdp.Network.Cyclone.Egress.IOffFrameEgress
    {
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityUnitHoldings> _instances;
        private readonly Dictionary<int, UnitHoldings> _sent = new();
        private static readonly int[] s_targetIds = { GlobalComponentIds.UnitHoldings };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityUnitHoldings>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtUnitHoldings;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityUnitHoldingsEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityUnitHoldings>(participant) : null, static key => new EntityUnitHoldings { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ScanAndPublish(ISimulationView view) { BeginFrame(view); ScanState(view, offFrame: false); }
        public void BeginFrame(ISimulationView view) { if (_instances.OnFrame(view)) _sent.Clear(); }

        public void ScanState(ISimulationView view, bool offFrame)
        {
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<UnitHoldings>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<UnitHoldings>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                ref readonly var h = ref view.GetComponentRO<UnitHoldings>(unit);
                if (_sent.TryGetValue(netId, out var prev) && _instances.TryGetLast(netId, out _)
                    && Fdp.Network.Cyclone.Translators.SampleEquality.Same(prev, h)) continue;
                _sent[netId] = h;
                _instances.Write(netId, ToSample(netId, h));
                SentSampleCount++;
            }
        }

        internal static EntityUnitHoldings ToSample(int netId, in UnitHoldings h)
        {
            int n = Math.Min((int)h.Count, UnitHoldings.Capacity);
            var items = new List<DdsUnitHolding>(n);
            for (int i = 0; i < n; i++)
            {
                var x = h.Items[i];
                items.Add(new DdsUnitHolding
                {
                    Type = x.Type, Count = x.Count, Operational = x.Operational, Ammo = x.Ammo,
                    Kind = (byte)x.Kind, Mobility = (byte)x.Mobility, Sensor = (byte)x.Sensor, Immobile = x.Immobile,
                });
            }
            return new EntityUnitHoldings { EntityId = netId, Items = items };
        }

        public void Dispose(int networkEntityId) { _sent.Remove(networkEntityId); _instances.Dispose(networkEntityId); }
        public void ReleaseInstance(int networkEntityId, long instanceId) { _sent.Remove(networkEntityId); _instances.Release(networkEntityId); }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐⭐ <c>CE-8001</c> — the shared ingress shape of the three aggregate descriptors: take the samples, drop an unknown unit,
    /// never take the recorded owner's own sample back, apply.</summary>
    public abstract class AggregateIngressTranslator<TWire> : IDescriptorTranslator, IDiscardableIngress where TWire : struct
    {
        private readonly DdsReader<TWire>? _reader;
        protected readonly NetworkEntityMap EntityMap;
        private readonly long _ordinal;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<TWire>();
        public long DescriptorOrdinal => _ordinal;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        protected AggregateIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, EDescriptorType ordinal)
        {
            _reader = participant is not null ? new DdsReader<TWire>(participant) : null;
            EntityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _ordinal = (long)ordinal;
        }

        public int DiscardIngress() => _reader.DiscardAll();

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
        internal void Apply(in TWire d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!EntityMap.TryGetEntity(IdOf(d), out var unit) || !view.IsAlive(unit)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo && AuthorityExtensions.IsRecordedOwner(repo, unit, OwnershipExtensions.PackKey(_ordinal, 0))) return;
            Write(unit, d, cmd);
        }

        protected abstract int IdOf(in TWire d);
        protected abstract void Write(Entity unit, in TWire d, IEntityCommandBuffer cmd);

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }

    /// <summary>⭐⭐ <c>CE-8001</c> — applies a unit's published <see cref="AggregateUnit"/>.</summary>
    public sealed class EntityAggregateUnitIngressTranslator : AggregateIngressTranslator<EntityAggregateUnit>
    {
        public EntityAggregateUnitIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
            : base(participant, entityMap, EDescriptorType.dtAggregateUnit) { }

        protected override int IdOf(in EntityAggregateUnit d) => d.EntityId;

        protected override void Write(Entity unit, in EntityAggregateUnit d, IEntityCommandBuffer cmd) => cmd.AddComponent(unit, FromSample(d));

        internal static AggregateUnit FromSample(in EntityAggregateUnit d) => new()
        {
            Resolution = (AggregateResolution)d.Resolution, Echelon = d.Echelon, Splitting = (SplitStage)d.Splitting, Pin = (ResolutionPin)d.Pin,
            Formation = (UnitFormation)d.Formation, Posture = (UnitPosture)d.Posture, Activity = (UnitActivity)d.Activity,
            Prepared = (PreparedLevel)d.Prepared, Move = (UnitMoveState)d.Move,
            Width = d.Width, Depth = d.Depth, Heading = d.HeadingDeg, Destination = new WorldPoint(d.DestX, d.DestY, d.DestZ),
            PathDistanceM = d.PathDistanceM, SpeedMps = d.SpeedMps, SuppressedShare = d.SuppressedShare,
            RouteRequestedAt = (SimInstant)d.RouteRequestedAt, LastFiredAt = (SimInstant)d.LastFiredAt, LastContactAt = (SimInstant)d.LastContactAt,
            PreparedSince = (SimInstant)d.PreparedSince, SplittingSince = (SimInstant)d.SplittingSince,
        };
    }

    /// <summary>⭐⭐ <c>CE-8001</c> — applies a unit's published <see cref="UnitSlotTable"/>.</summary>
    public sealed class EntityUnitSlotTableIngressTranslator : AggregateIngressTranslator<EntityUnitSlotTable>
    {
        public EntityUnitSlotTableIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
            : base(participant, entityMap, EDescriptorType.dtUnitSlotTable) { }

        protected override int IdOf(in EntityUnitSlotTable d) => d.EntityId;

        protected override void Write(Entity unit, in EntityUnitSlotTable d, IEntityCommandBuffer cmd) => cmd.AddComponent(unit, FromSample(d));

        internal static UnitSlotTable FromSample(in EntityUnitSlotTable d)
        {
            var t = new UnitSlotTable { Chunk = d.Chunk };
            int n = d.Slots is null ? 0 : Math.Min(d.Slots.Count, UnitSlotTable.Capacity);
            for (int i = 0; i < n; i++)
            {
                var s = d.Slots![i];
                t.Slots[i] = new UnitSlot
                {
                    Health = BitConverter.UInt16BitsToHalf(s.HealthBits), Stress = BitConverter.UInt16BitsToHalf(s.StressBits),
                    Ammo = s.Ammo, Role = (SlotRole)s.Role, CompositeSlot = s.CompositeSlot,
                };
            }
            t.Count = (byte)n;
            return t;
        }
    }

    /// <summary>⭐⭐ <c>CE-8001</c> — applies a unit's published <see cref="UnitHoldings"/>.</summary>
    public sealed class EntityUnitHoldingsIngressTranslator : AggregateIngressTranslator<EntityUnitHoldings>
    {
        public EntityUnitHoldingsIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
            : base(participant, entityMap, EDescriptorType.dtUnitHoldings) { }

        protected override int IdOf(in EntityUnitHoldings d) => d.EntityId;

        protected override void Write(Entity unit, in EntityUnitHoldings d, IEntityCommandBuffer cmd) => cmd.AddComponent(unit, FromSample(d));

        internal static UnitHoldings FromSample(in EntityUnitHoldings d)
        {
            var h = new UnitHoldings();
            int n = d.Items is null ? 0 : Math.Min(d.Items.Count, UnitHoldings.Capacity);
            for (int i = 0; i < n; i++)
            {
                var x = d.Items![i];
                h.Items[i] = new UnitHolding
                {
                    Type = new TkbTypeId(x.Type), Count = x.Count, Operational = x.Operational, Ammo = x.Ammo,
                    Kind = (HoldingKind)x.Kind, Mobility = (HoldingMobility)x.Mobility, Sensor = (HoldingSensor)x.Sensor, Immobile = x.Immobile,
                };
            }
            h.Count = (byte)n;
            return h;
        }
    }
}
