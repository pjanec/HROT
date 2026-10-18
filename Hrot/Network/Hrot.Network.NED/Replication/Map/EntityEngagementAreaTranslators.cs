using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-3261</c> (behaviors' sectors note ②; docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §13c) — publishes a
    /// man's or a commander's <see cref="EngagementArea"/> (behaviors' CE-2282) from the node owning its <c>dtEngagementArea</c> (the brain
    /// owner), on change, so a commander's NoFire reaches the brain nodes of the men under him. The gesture translators' shape.
    /// </summary>
    public sealed class EntityEngagementAreaEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityEngagementArea";
        private readonly DdsWriter<EntityEngagementArea>? _writer;
        private readonly Dictionary<long, EntityEngagementArea> _last = new();
        private int _worldEpoch;

        private static readonly int[] s_targetIds = { GlobalComponentIds.EngagementArea };

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtEngagementArea;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityEngagementAreaEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityEngagementArea>(participant, DdsTopicName) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch))
            {
                // ⭐⭐ CE-4007 (Q91 §4-M) — a wiped world: DELETE every instance this node wrote, so a late joiner never receives a dead run.
                foreach (var id in _last.Keys) _writer?.DisposeInstance(new EntityEngagementArea { EntityId = id });
                _last.Clear();
            }
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<EngagementArea>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<EngagementArea>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var sample = ToSample(netId, view.GetComponentRO<EngagementArea>(unit));
                if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityEngagementArea ToSample(long netId, in EngagementArea c)
            => new()
            {
                EntityId = netId, ArcSet = c.ArcSet, ArcOriginX = c.ArcOrigin.X, ArcOriginY = c.ArcOrigin.Y, ArcOriginZ = c.ArcOrigin.Z,
                LeftBearing = c.LeftBearing, RightBearing = c.RightBearing, MinRange = c.MinRange, MaxRange = c.MaxRange, AllowArea = c.AllowArea,
                WatchBearing = c.WatchBearing, WatchSet = c.WatchSet, NoFireCount = c.NoFire.Count,
                NoFire0 = c.NoFire.Count > 0 ? c.NoFire.Items[0] : 0L, NoFire1 = c.NoFire.Count > 1 ? c.NoFire.Items[1] : 0L,
                NoFire2 = c.NoFire.Count > 2 ? c.NoFire.Items[2] : 0L, NoFire3 = c.NoFire.Count > 3 ? c.NoFire.Items[3] : 0L,
                SetBy = (byte)c.SetBy,
            };

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-3261</c> — applies a man's or a commander's published <see cref="EngagementArea"/> on this node (<c>EngagementAreas</c>
    /// reads it through the chain). An unknown unit is dropped; the recorded owner never takes its own sample back.</summary>
    public sealed class EntityEngagementAreaIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityEngagementArea";
        private const long OrdinalValue = (long)EDescriptorType.dtEngagementArea;
        private readonly DdsReader<EntityEngagementArea>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityEngagementAreaIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityEngagementArea>(participant, DdsTopicName) : null;
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
        internal void Apply(in EntityEngagementArea d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var unit) || !view.IsAlive(unit)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, unit, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            cmd.AddComponent(unit, FromSample(in d));
        }

        /// <summary>The component a sample carries (the test seam).</summary>
        internal static EngagementArea FromSample(in EntityEngagementArea d)
        {
            var a = new EngagementArea
            {
                ArcSet = d.ArcSet, ArcOrigin = new System.Numerics.Vector3(d.ArcOriginX, d.ArcOriginY, d.ArcOriginZ),
                LeftBearing = d.LeftBearing, RightBearing = d.RightBearing, MinRange = d.MinRange, MaxRange = d.MaxRange, AllowArea = d.AllowArea,
                WatchBearing = d.WatchBearing, WatchSet = d.WatchSet, SetBy = (BehaviorOrigin)d.SetBy,
            };
            int n = Math.Clamp(d.NoFireCount, 0, EngagementArea.MaxNoFire);
            a.NoFire.Count = n;
            if (n > 0) a.NoFire.Items[0] = d.NoFire0;
            if (n > 1) a.NoFire.Items[1] = d.NoFire1;
            if (n > 2) a.NoFire.Items[2] = d.NoFire2;
            if (n > 3) a.NoFire.Items[3] = d.NoFire3;
            return a;
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
