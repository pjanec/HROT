using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using Hrot.NED.Descriptors.Orchestration;   // ⭐ AQ107 I — WireUnits
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
    public sealed class EntityEngagementAreaEgressTranslator : IDescriptorTranslator, Fdp.Network.Cyclone.Egress.IOffFrameEgress
    {
        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the send-on-change cache AND the instances it deletes on a
        //   destruction, a wipe or (as an unregister) a hand-over.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityEngagementArea> _instances;

        private static readonly int[] s_targetIds = { GlobalComponentIds.EngagementArea };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityEngagementArea>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtEngagementArea;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityEngagementAreaEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityEngagementArea>(participant) : null, static key => new EntityEngagementArea { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            BeginFrame(view);
            ScanState(view, offFrame: false);
        }

        /// <summary>⭐ CE-3427 N-1 (docs/DESIGN_Egress_Change_Detection.md §7) — on the LIVE world, in the egress system's slot: the world-epoch and
        /// destruction bookkeeping (it reads the bus).</summary>
        public void BeginFrame(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
        }

        /// <summary>⭐ CE-3427 N-1 — the state scan: reads only <paramref name="view"/>'s components and writes nothing to it, so it may run
        /// on the off-frame replica.</summary>
        public void ScanState(ISimulationView view, bool offFrame)
        {
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<EngagementArea>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<EngagementArea>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var sample = ToSample(netId, view.GetComponentRO<EngagementArea>(unit));
                if (_instances.Unchanged(netId, sample)) continue;
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityEngagementArea ToSample(int netId, in EngagementArea c)
            => new()
            {
                EntityId = netId, ArcSet = c.ArcSet, ArcOriginX = c.ArcOrigin.X, ArcOriginY = c.ArcOrigin.Y, ArcOriginZ = c.ArcOrigin.Z,
                LeftBearing = WireUnits.DegToRad(c.LeftBearing), RightBearing = WireUnits.DegToRad(c.RightBearing),   // ⭐ AQ107 I — radians on the wire, degrees in the ECS
                MinRange = c.MinRange, MaxRange = c.MaxRange, AllowArea = c.AllowArea,
                WatchBearing = WireUnits.DegToRad(c.WatchBearing), WatchSet = c.WatchSet, NoFireCount = c.NoFire.Count,
                NoFire0 = c.NoFire.Count > 0 ? c.NoFire.Items[0] : 0, NoFire1 = c.NoFire.Count > 1 ? c.NoFire.Items[1] : 0,
                NoFire2 = c.NoFire.Count > 2 ? c.NoFire.Items[2] : 0, NoFire3 = c.NoFire.Count > 3 ? c.NoFire.Items[3] : 0,
                SetBy = (byte)c.SetBy,
            };

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-3261</c> — applies a man's or a commander's published <see cref="EngagementArea"/> on this node (<c>EngagementAreas</c>
    /// reads it through the chain). An unknown unit is dropped; the recorded owner never takes its own sample back.</summary>
    public sealed class EntityEngagementAreaIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtEngagementArea;
        private readonly DdsReader<EntityEngagementArea>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityEngagementArea>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityEngagementAreaIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityEngagementArea>(participant) : null;
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
                LeftBearing = WireUnits.RadToDeg(d.LeftBearing), RightBearing = WireUnits.RadToDeg(d.RightBearing),
                MinRange = d.MinRange, MaxRange = d.MaxRange, AllowArea = d.AllowArea,
                WatchBearing = WireUnits.RadToDeg(d.WatchBearing), WatchSet = d.WatchSet, SetBy = (BehaviorOrigin)d.SetBy,
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
        public void Dispose(int networkEntityId) { }
    }
}
