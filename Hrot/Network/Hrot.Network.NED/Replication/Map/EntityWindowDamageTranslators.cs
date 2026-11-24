using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Terrain;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-5033</c> (glass windows GW-2, docs/DESIGN_Glass_Windows.md D-3) — publishes a building's <see cref="WindowDamage"/> (with its <see cref="TerrainObjectKey"/>)
    /// from the node that owns the entity's <c>dtWindowDamage</c> descriptor, on change. Exactly <see cref="EntityBuildingDamageEgressTranslator"/>'s shape: send-on-change keyed
    /// by network id, a window's panel as its rank WITHIN the building, a ghost never published.
    /// </summary>
    public sealed class EntityWindowDamageEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtWindowDamage;

        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityWindowDamage> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityWindowDamage>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityWindowDamageEgressTranslator(DdsParticipant participant)
        {
            _instances = new(new DdsWriter<EntityWindowDamage>(participant), static key => new EntityWindowDamage { EntityId = (int)key });
        }

        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<WindowDamage>()) return;
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query()
                    .With<WindowDamage>()
                    .WithManaged<TerrainObjectKey>()
                    .DefaultRoutePublishable()
                    .WithLifecycle(EntityLifecycle.AllButTearDown)
                    .Build();
                _queryView = view;
            }
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            var terrain = view is EntityRepository tr && tr.HasSingletonManaged<TerrainWorld>() ? tr.GetSingletonManaged<TerrainWorld>() : null;
            foreach (var entity in _query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;   // a ghost is never the owner
                ref readonly var w = ref view.GetComponentRO<WindowDamage>(entity);
                if (w.Count == 0 && !_instances.TryGetLast(view.GetComponentRO<NetworkIdentity>(entity).Value, out _)) continue;   // nothing hit yet: nothing to say
                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                string key = view.GetManagedComponentRO<TerrainObjectKey>(entity).Key;
                if (_instances.TryGetLast(netId, out var prev) && Matches(in prev, in w, key, terrain)) continue;   // HOT PATH: compare before building
                _instances.Write(netId, ToSample(netId, w, key, terrain));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam): each window's panel as its rank within the building (<see cref="BuildingPanelIndex"/>) when a terrain is given.</summary>
        internal static EntityWindowDamage ToSample(int netId, in WindowDamage c, string key, TerrainWorld? terrain = null)
        {
            var list = new List<DdsWindow>(c.Count);
            for (int i = 0; i < c.Count && i < WindowDamage.Capacity; i++)
            {
                var w = c.Get(i);
                int panel = terrain != null ? BuildingPanelIndex.ToLocal(terrain, w.Panel) : w.Panel;
                list.Add(new DdsWindow { Panel = panel, Opening = w.Opening, Hits = w.Hits, U0 = w.U0, V0 = w.V0, U1 = w.U1, V1 = w.V1, HitAt = c.HitAtOf(i) });   // ⭐ CE-11051
            }
            return new EntityWindowDamage { EntityId = netId, Building = c.Building, Key = key, Windows = list };
        }

        /// <summary>True when <paramref name="prev"/> is exactly what <see cref="ToSample"/> would build now. Allocation-free.</summary>
        internal static bool Matches(in EntityWindowDamage prev, in WindowDamage c, string key, TerrainWorld? terrain)
        {
            if (prev.Building != c.Building || prev.Key != key) return false;
            int n = Math.Min((int)c.Count, WindowDamage.Capacity);
            if ((prev.Windows?.Count ?? 0) != n) return false;
            for (int i = 0; i < n; i++)
            {
                var w = c.Get(i); var y = prev.Windows![i];
                int panel = terrain != null ? BuildingPanelIndex.ToLocal(terrain, w.Panel) : w.Panel;
                if (y.Panel != panel || y.Opening != w.Opening || y.Hits != w.Hits || y.U0 != w.U0 || y.V0 != w.V0 || y.U1 != w.U1 || y.V1 != w.V1) return false;
                if (y.HitAt != c.HitAtOf(i).Seconds) return false;   // ⭐ CE-11051
            }
            return true;
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);

        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ <c>CE-5033</c> (GW-2) — applies a building's published <see cref="WindowDamage"/> on every node that does not own it (the
    /// <see cref="EntityBuildingDamageIngressTranslator"/> shape: an unknown id makes a ghost, the recorded owner never takes its own sample back, a sample
    /// that arrives before the terrain waits for it, one whose key this world does not have is dropped).
    /// </summary>
    public sealed class EntityWindowDamageIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtWindowDamage;

        private readonly DdsReader<EntityWindowDamage>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly NodeId _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityWindowDamage>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityWindowDamageIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, GhostCreationSystem ghostCreationSystem, int localNodeId)
        {
            _reader = participant is not null ? new DdsReader<EntityWindowDamage>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _localNodeId = (Fdp.Core.NodeId)(localNodeId);
        }

        public int DiscardIngress() => _reader.DiscardAll();

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _worldEpoch)) { _deferred.Clear(); }   // ⭐ R-4297 ④ (CE-1980) — a world replacement forgets the samples waiting for their entity (they named the old world)
            RetryDeferred(cmd, view);
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive) continue;
                ReceivedSampleCount++;
                Apply(sample.Data, cmd, view);
            }
        }

        /// <summary>Takes one sample (the test seam).</summary>
        internal void Receive(in EntityWindowDamage d, IEntityCommandBuffer cmd, ISimulationView view) { ReceivedSampleCount++; Apply(in d, cmd, view); }

        private void Apply(in EntityWindowDamage data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            int netId = data.EntityId;
            if (!_entityMap.TryGetEntity(netId, out var entity))
            {
                if (view is not EntityRepository repo)
                {
                    FdpLog<EntityWindowDamageIngressTranslator>.Warn("[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                    return;
                }
                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }
            if (view is EntityRepository ownerCheck && Owns(ownerCheck, entity)) return;
            var terrain = TerrainOf(view);
            if (terrain == null || WallGroups.Count(terrain) == 0) { _deferred[netId] = data; return; }
            _deferred.Remove(netId);
            if (!TryFromSample(in data, terrain, out var component))
            {
                if (_unresolved.Add(netId))
                    FdpLog<EntityWindowDamageIngressTranslator>.Warn("[Node-{0}] Window damage for NetID {1} names '{2}', which this node's terrain does not have: dropped.", _localNodeId, netId, data.Key);
                return;
            }
            cmd.SetComponent(entity, component);
            if (!string.IsNullOrEmpty(data.Key)
                && !(view is EntityRepository r && r.HasComponent<TerrainObjectKey>(entity) && r.GetComponent<TerrainObjectKey>(entity).Key == data.Key))
                cmd.SetManagedComponent(entity, new TerrainObjectKey { Key = data.Key });
        }

        [Fdp.Core.ProtocolCache("received damage samples waiting for the terrain/world to exist (deferred ingress)")]
        private readonly Dictionary<int, EntityWindowDamage> _deferred = new();
        private int _worldEpoch;
        [Fdp.Core.WorldDerived("log-once set of unresolved net ids")]
        private readonly HashSet<int> _unresolved = new();
        [Fdp.Core.FrameScratch("retry id list: cleared at 233/197 and refilled from _deferred each retry")]
        private readonly List<int> _retry = new();

        internal int DeferredCount => _deferred.Count;

        private void RetryDeferred(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_deferred.Count == 0) return;
            var terrain = TerrainOf(view);
            if (terrain == null || WallGroups.Count(terrain) == 0) return;
            _retry.Clear();
            foreach (var id in _deferred.Keys) _retry.Add(id);
            foreach (var id in _retry)
                if (_deferred.TryGetValue(id, out var d)) Apply(in d, cmd, view);
        }

        /// <summary>The component in THIS world: the building the key names here, each window's panel mapped from its within-building rank; a window this world does not
        /// have (no such panel, opening, or no glass) is dropped. False when the key names no building here.</summary>
        internal static bool TryFromSample(in EntityWindowDamage d, TerrainWorld terrain, out WindowDamage c)
        {
            c = default;
            int building = BuildingDamages.IndexOfKey(terrain, d.Key);
            if (building < 0) return false;
            c.Building = building;
            int n = Math.Min(d.Windows?.Count ?? 0, WindowDamage.Capacity);
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                var w = d.Windows![i];
                int panel = BuildingPanelIndex.ToGlobal(terrain, building, w.Panel);
                if (panel < 0 || w.Opening >= terrain.Panels[panel].Openings.Count || !terrain.Panels[panel].Openings[w.Opening].Glazed) continue;
                c.SetHitAt(k, w.HitAt);   // ⭐ CE-11051
                c.Set(k++, new WindowEntry(panel, w.Opening, w.Hits, w.U0, w.V0, w.U1, w.V1));
            }
            c.Count = (byte)k;
            return true;
        }

        private static TerrainWorld? TerrainOf(ISimulationView view)
            => view is EntityRepository r && r.HasSingletonManaged<TerrainWorld>() ? r.GetSingletonManaged<TerrainWorld>() : null;

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityWindowDamage d || Owns(repo, entity)) return;
            var terrain = TerrainOf(repo);
            if (terrain == null || WallGroups.Count(terrain) == 0) { _deferred[d.EntityId] = d; return; }
            if (!TryFromSample(in d, terrain, out var component)) return;
            if (repo.HasComponent<WindowDamage>(entity)) repo.SetComponent(entity, component); else repo.AddComponent(entity, component);
            if (!string.IsNullOrEmpty(d.Key)) repo.SetManagedComponent(entity, new TerrainObjectKey { Key = d.Key });
        }

        public void Dispose(int networkEntityId) { _deferred.Remove(networkEntityId); _unresolved.Remove(networkEntityId); }

        private static bool Owns(EntityRepository repo, Entity entity)
            => AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0));
    }
}
