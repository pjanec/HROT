using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-1223</c> (Entity Control §8k S-2, R-1010) — publishes a man's <see cref="UnderFire"/> stamp from the node owning its
    /// <c>dtUnderFire</c> (the Muscle group — whoever the near-miss and hit producers stamp it on), keyed by network id, on change. Every
    /// other node's <c>HitModel.Sigma</c> then sees the same suppression: a remote first-person station draws the true spread ring.
    /// The cache clears when the world moves (<c>WorldEpoch</c>, CE-4007).
    /// </summary>
    public sealed class EntityUnderFireEgressTranslator : IDescriptorTranslator
    {
        // ⭐ CE-2407 (DESIGN_Egress_Change_Detection §5, E1) — skips the entities none of these inputs changed for; NetworkIdentity is implied
        private readonly Fdp.Toolkit.Replication.Utilities.EgressVisitGate _gate = new(typeof(UnderFire));

        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the send-on-change cache AND the instances it deletes on a
        //   destruction, a wipe or (as an unregister) a hand-over.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityUnderFire> _instances;

        private static readonly int[] s_targetIds = { GlobalComponentIds.UnderFire };

        /// <summary>A newer stamp is sent only this long (s) after the last one sent: the remote window ends at most this early.</summary>
        public const double RefreshSeconds = 0.5;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityUnderFire>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtUnderFire;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityUnderFireEgressTranslator(DdsParticipant? participant)
            : this(participant is not null ? new DdsWriterAdapter<EntityUnderFire>(participant) : null) { }

        /// <summary>The test seam: a capturing writer instead of a live participant.</summary>
        internal EntityUnderFireEgressTranslator(IDdsWriter<EntityUnderFire>? writer)
            => _instances = new(writer is null ? null : s => writer.Write(s), writer is null ? null : s => writer.DisposeInstance(s),
                writer is null ? null : s => writer.UnregisterInstance(s), static key => new EntityUnderFire { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<UnderFire>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            _gate.Begin(view);
            foreach (var e in view.Query().With<UnderFire>().DefaultRoutePublishable().Build())
            {
                if (!_gate.Visit(e)) continue;   // ⭐ CE-2407 E1 — nothing this translator reads has changed since its last scan
                if (!view.HasAuthority(e, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                var sample = ToSample(netId, view.GetComponentRO<UnderFire>(e));
                // ⭐ a man under a machine gun is re-stamped by every near miss: a stamp within RefreshSeconds of the last one sent changes
                //   no remote window by more than that (HitModel.UnderFireSeconds is 5 s), so it is not sent — never a sample per round
                if (_instances.TryGetLast(netId, out var prev)
                    && sample.LastTime >= prev.LastTime && sample.LastTime - prev.LastTime < RefreshSeconds) continue;
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityUnderFire ToSample(int netId, in UnderFire c) => new() { EntityId = netId, LastTime = c.LastTime };

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ <c>CE-1223</c> — applies a published <see cref="UnderFire"/> stamp to this node's copy of the man, so the hit model here
    /// doubles σ for the same window the Muscle does. The recorded owner never takes its own sample back; an unknown entity is dropped
    /// (Reliable + TransientLocal re-sends on the next change); the component is added when the man has none yet.
    /// </summary>
    public sealed class EntityUnderFireIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtUnderFire;
        private readonly DdsReader<EntityUnderFire>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityUnderFire>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityUnderFireIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityUnderFire>(participant) : null;
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

        /// <summary>Applies one sample (the test seam: a live participant is not needed to check the mapping).</summary>
        internal void Apply(in EntityUnderFire s, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(s.EntityId, out var e) || !view.IsAlive(e)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo)
            {
                if (!repo.IsComponentTypeRegistered<UnderFire>()) { DroppedSampleCount++; return; }
                if (AuthorityExtensions.IsRecordedOwner(repo, e, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            }
            cmd.AddComponent(e, new UnderFire { LastTime = (SimInstant)s.LastTime });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }
}
