using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-3219</c> part 3 (civilians B-1, R-3014; docs/DESIGN_Civilians_And_Crowd_Control.md §7.3b) — publishes a gas / smoke cloud's
    /// <see cref="AreaEffectCloud"/> from the node owning its <c>dtAreaEffectCloud</c> (its creator), on change. The <c>dtAreaEffectCloud</c> shape.
    /// </summary>
    public sealed class EntityAreaEffectCloudEgressTranslator : IDescriptorTranslator
    {
        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the send-on-change cache AND the instances it deletes on a
        //   destruction, a wipe or (as an unregister) a hand-over.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityAreaEffectCloud> _instances;

        private static readonly int[] s_targetIds = { GlobalComponentIds.AreaEffectCloud };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityAreaEffectCloud>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtAreaEffectCloud;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityAreaEffectCloudEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityAreaEffectCloud>(participant) : null, static key => new EntityAreaEffectCloud { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<AreaEffectCloud>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<AreaEffectCloud>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var sample = ToSample(netId, view.GetComponentRO<AreaEffectCloud>(unit));
                if (_instances.Unchanged(netId, sample)) continue;
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityAreaEffectCloud ToSample(int netId, in AreaEffectCloud c) => new()
        {
            EntityId = netId, Kind = (byte)c.Kind, Radius = c.Radius, Height = c.Height, SightTransmittance = c.SightTransmittance,
            IrritantSeconds = c.IrritantSeconds, BornAtSimSeconds = c.BornAtSimSeconds, Seconds = c.Seconds,
            SourceNetworkId = c.SourceNetworkId,   // ⭐ CE-3236
            CreatorNetworkId = c.CreatorNetworkId, OriginalCreatorNetworkId = c.OriginalCreatorNetworkId,   // ⭐ CE-4155
        };

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-3219</c> part 3 — applies a published <see cref="AreaEffectCloud"/> on this node (sight queries and maps read it).
    /// An unknown unit is dropped; the recorded owner never takes its own sample back.</summary>
    public sealed class EntityAreaEffectCloudIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtAreaEffectCloud;
        private readonly DdsReader<EntityAreaEffectCloud>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityAreaEffectCloud>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityAreaEffectCloudIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityAreaEffectCloud>(participant) : null;
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
        internal void Apply(in EntityAreaEffectCloud d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var unit) || !view.IsAlive(unit)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, unit, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            cmd.AddComponent(unit, FromSample(in d));
        }

        /// <summary>⭐ The one sample → component mapping (this ingress, and a forwarded creation request — <c>CE-3228</c>).</summary>
        internal static AreaEffectCloud FromSample(in EntityAreaEffectCloud d) => new()
        {
            Kind = (CloudKind)d.Kind, Radius = d.Radius, Height = d.Height, SightTransmittance = d.SightTransmittance,
            IrritantSeconds = d.IrritantSeconds, BornAtSimSeconds = d.BornAtSimSeconds, Seconds = d.Seconds,
            SourceNetworkId = d.SourceNetworkId,   // ⭐ CE-3236
            CreatorNetworkId = d.CreatorNetworkId, OriginalCreatorNetworkId = d.OriginalCreatorNetworkId,   // ⭐ CE-4155
        };

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }
}
