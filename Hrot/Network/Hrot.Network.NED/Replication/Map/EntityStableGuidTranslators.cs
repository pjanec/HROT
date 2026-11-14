using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-4153</c> S3 (docs/DESIGN_Scenario_Entity_Keys.md) — publishes an entity's <see cref="EntityGuid"/> (its stable scenario key) from
    /// the node that owns <c>dtEntityGuid</c> (no ownership group ⇒ the entity's creator keeps it), ONCE: the key never changes after creation.
    /// Every other node then knows it, so whichever node owns the entity when a scenario is saved writes the same file key. The
    /// <c>EntityDescent</c> / <c>dtObstacleShape</c> shape; lifecycle <c>All</c> so a still-<c>Constructing</c> entity is published too.
    /// </summary>
    public sealed class EntityStableGuidEgressTranslator : IDescriptorTranslator
    {
        // ⭐ CE-2407 (DESIGN_Egress_Change_Detection §5, E1) — skips the entities none of these inputs changed for; NetworkIdentity is implied
        private readonly Fdp.Toolkit.Replication.Utilities.EgressVisitGate _gate = new(typeof(EntityGuid));

        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the exactly-once record AND the instances it deletes on a
        //   destruction, a wipe or (as an unregister) a hand-over.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityStableGuid> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityStableGuid>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtEntityGuid;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityStableGuidEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityStableGuid>(participant) : null, static key => new EntityStableGuid { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<EntityGuid>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            _gate.Begin(view);
            foreach (var e in view.Query().With<EntityGuid>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build())
            {
                if (!_gate.Visit(e)) continue;   // ⭐ CE-2407 E1 — nothing this translator reads has changed since its last scan
                if (!view.HasAuthority(e, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                if (_instances.TryGetLast(netId, out _)) continue;   // exactly once per entity
                _instances.Write(netId, ToSample(netId, view.GetComponentRO<EntityGuid>(e)));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam, and the creation-request builder's).</summary>
        public static EntityStableGuid ToSample(int netId, in EntityGuid g)
        {
            Span<byte> b = stackalloc byte[16];
            g.Value.TryWriteBytes(b);
            return new EntityStableGuid
            {
                EntityId = netId,
                Lo = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(b),
                Hi = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(b.Slice(8)),
            };
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ <c>CE-4153</c> S3 — applies an entity's published <see cref="EntityGuid"/> on this node. The key is immutable, so a sample that
    /// arrives BEFORE its entity exists (DDS gives no ordering across topics) is held and retried each poll — ⛔ never dropped, or this node
    /// would not know the key when the entity's ownership later moves here. The recorded owner never takes its own sample back.
    /// </summary>
    public sealed class EntityStableGuidIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtEntityGuid;
        private readonly DdsReader<EntityStableGuid>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<int, Guid> _waiting = new();
        private readonly List<int> _applied = new();

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityStableGuid>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        /// <summary>Samples still waiting for their entity (diagnostics and rails).</summary>
        public int WaitingCount => _waiting.Count;
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityStableGuidIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityStableGuid>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc />
        public int DiscardIngress() { _waiting.Clear(); return _reader.DiscardAll(); }   // ⭐ CE-3329 W5

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is not null)
            {
                using var loan = _reader.Take();
                foreach (var sample in loan)
                {
                    if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive) continue;
                    ReceivedSampleCount++;
                    _waiting[sample.Data.EntityId] = FromSample(sample.Data);
                }
            }
            if (_waiting.Count == 0) return;
            _applied.Clear();
            foreach (var (netId, guid) in _waiting)
                if (TryApply(netId, guid, cmd, view)) _applied.Add(netId);
            foreach (var id in _applied) _waiting.Remove(id);
        }

        /// <summary>Applies one sample (the test seam); false = its entity is not here yet.</summary>
        internal bool TryApply(int netId, Guid guid, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(netId, out var e) || !view.IsAlive(e)) return false;
            if (view is EntityRepository repo)
            {
                if (AuthorityExtensions.IsRecordedOwner(repo, e, OwnershipExtensions.PackKey(OrdinalValue, 0))) return true;
                if (!repo.IsComponentTypeRegistered<EntityGuid>()) return true;
            }
            if (!view.HasComponent<EntityGuid>(e)) cmd.AddComponent(e, new EntityGuid(guid));
            return true;
        }

        public static Guid FromSample(in EntityStableGuid s)
        {
            Span<byte> b = stackalloc byte[16];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b, s.Lo);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b.Slice(8), s.Hi);
            return new Guid(b);
        }

        /// <summary>The creation-request mapper's form: the component a forwarded request seeds.</summary>
        public static EntityGuid ComponentFrom(in EntityStableGuid s) => new(FromSample(s));

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { _waiting.Remove(networkEntityId); }
    }
}
