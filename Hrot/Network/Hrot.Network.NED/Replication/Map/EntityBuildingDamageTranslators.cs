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
    /// ⭐ <c>CE-4391</c> S-1 (docs/DESIGN_Terrain_Import_UTM.md §8.2 B-11) — publishes a building's <see cref="BuildingDamage"/> (with its
    /// <see cref="TerrainObjectKey"/>) from the node that owns the entity's <c>dtBuildingDamage</c> descriptor — the node that decided the breaches —
    /// on change. Exactly <see cref="EntityDoorStateEgressTranslator"/>'s shape: send-on-change keyed by network id, the cache cleared when the world moves,
    /// a GHOST (a replica whose master has not arrived) never published as if it owned the entity.
    /// </summary>
    public sealed class EntityBuildingDamageEgressTranslator : IDescriptorTranslator
    {
        private const long OrdinalValue = (long)EDescriptorType.dtBuildingDamage;

        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityBuildingDamage> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityBuildingDamage>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityBuildingDamageEgressTranslator(DdsParticipant participant)
        {
            _instances = new(new DdsWriter<EntityBuildingDamage>(participant), static key => new EntityBuildingDamage { EntityId = (int)key });
        }

        private EntityQuery? _query;
        private ISimulationView? _queryView;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<BuildingDamage>()) return;

            // ⭐ CE-4427 R-3 (HOT PATH) — the query is built once per view, not per tick
            if (!ReferenceEquals(view, _queryView) || _query == null)
            {
                _query = view.Query()
                    .With<BuildingDamage>()
                    .WithManaged<TerrainObjectKey>()
                    .DefaultRoutePublishable()
                    .WithLifecycle(EntityLifecycle.AllButTearDown)
                    .Build();
                _queryView = view;
            }
            var query = _query;

            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            var terrain = view is EntityRepository tr && tr.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() ? tr.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() : null;
            foreach (var entity in query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;   // a ghost is never the owner (CE-2335)

                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                ref readonly var damage = ref view.GetComponentRO<BuildingDamage>(entity);
                string key = view.GetManagedComponentRO<TerrainObjectKey>(entity).Key;
                // ⭐ CE-4427 R-3 — compare BEFORE converting: an unchanged component builds no sample (no list, no panel conversion) on a steady tick
                if (_instances.TryGetLast(netId, out var prev) && Matches(in prev, in damage, key, terrain)) continue;
                var sample = ToSample(netId, damage, key, terrain);
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        /// <summary>⭐ <c>CE-4417</c> (O-3) — with the sender's <paramref name="terrain"/>, a breach's <c>Panel</c> travels as its index WITHIN the
        /// building (<see cref="BuildingPanelIndex"/>), and the receiver resolves the building by <paramref name="key"/>: neither side's GLOBAL
        /// index crosses the wire, so two nodes whose worlds index the house differently still breach the same wall. Without a terrain (a test
        /// seam) the indices travel as they are.</summary>
        internal static EntityBuildingDamage ToSample(int netId, in BuildingDamage c, string key, Fdp.Toolkit.Terrain.TerrainWorld? terrain = null)
        {
            var list = new List<DdsWallBreach>(c.Count);
            for (int i = 0; i < c.Count && i < BuildingDamage.Capacity; i++)
            {
                var b = c.Get(i);
                int panel = terrain != null ? BuildingPanelIndex.ToLocal(terrain, b.Panel) : b.Panel;
                list.Add(new DdsWallBreach { Panel = panel, From = b.From, To = b.To, Bottom = b.Bottom, Top = b.Top, Kind = (byte)b.Kind });
            }
            return new EntityBuildingDamage { EntityId = netId, Building = c.Building, Collapsed = c.Collapsed, Key = key, Breaches = list };
        }

        /// <summary>⭐ CE-4427 R-3 — true when <paramref name="prev"/> is exactly what <see cref="ToSample"/> would build for <paramref name="c"/> now. Allocation-free (the panel conversion is the world's cached table).</summary>
        internal static bool Matches(in EntityBuildingDamage prev, in BuildingDamage c, string key, Fdp.Toolkit.Terrain.TerrainWorld? terrain)
        {
            if (prev.Building != c.Building || prev.Collapsed != c.Collapsed || prev.Key != key) return false;
            int n = Math.Min((int)c.Count, BuildingDamage.Capacity);
            if ((prev.Breaches?.Count ?? 0) != n) return false;
            for (int i = 0; i < n; i++)
            {
                var b = c.Get(i); var y = prev.Breaches![i];
                int panel = terrain != null ? BuildingPanelIndex.ToLocal(terrain, b.Panel) : b.Panel;
                if (y.Panel != panel || y.From != b.From || y.To != b.To || y.Bottom != b.Bottom || y.Top != b.Top || y.Kind != (byte)b.Kind) return false;
            }
            return true;
        }

        internal static bool Same(in EntityBuildingDamage a, in EntityBuildingDamage b)
        {
            if (a.Building != b.Building || a.Collapsed != b.Collapsed || a.Key != b.Key) return false;
            int na = a.Breaches?.Count ?? 0, nb = b.Breaches?.Count ?? 0;
            if (na != nb) return false;
            for (int i = 0; i < na; i++)
            {
                var x = a.Breaches![i]; var y = b.Breaches![i];
                if (x.Panel != y.Panel || x.From != y.From || x.To != y.To || x.Bottom != y.Bottom || x.Top != y.Top || x.Kind != y.Kind) return false;
            }
            return true;
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
    /// ⭐ <c>CE-4391</c> S-1 (§8.2 B-11) — applies a building's published <see cref="BuildingDamage"/> (and its key) on every node that does not own it; every
    /// reader on this node then builds its table from the view it runs on (<c>DoorStates.Of</c> → <see cref="DoorStates.Damage"/>, R-219). The
    /// <see cref="EntityDoorStateIngressTranslator"/> shape: an unknown id makes a ghost (TransientLocal can deliver the state before the master), and the
    /// node that holds the RECORDED ownership of <c>dtBuildingDamage</c> never takes its own sample back (S8 / F-5). A plain
    /// <see cref="IDescriptorTranslator"/> with its own reader: the sample carries a string and a list.
    /// </summary>
    public sealed class EntityBuildingDamageIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtBuildingDamage;

        private readonly DdsReader<EntityBuildingDamage>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly NodeId _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityBuildingDamage>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityBuildingDamageIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap,
            GhostCreationSystem ghostCreationSystem, int localNodeId)
        {
            _reader = participant is not null ? new DdsReader<EntityBuildingDamage>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _localNodeId = (Fdp.Core.NodeId)(localNodeId);
        }

        /// <inheritdoc />
        public int DiscardIngress() => _reader.DiscardAll();   // ⭐ CE-3329 W5

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
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

        /// <summary>Takes one sample (the test seam), as <see cref="PollIngress"/> does for each one it reads.</summary>
        internal void Receive(in EntityBuildingDamage d, IEntityCommandBuffer cmd, ISimulationView view) { ReceivedSampleCount++; Apply(in d, cmd, view); }

        private void Apply(in EntityBuildingDamage data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            int netId = data.EntityId;
            if (!_entityMap.TryGetEntity(netId, out var entity))
            {
                if (view is not EntityRepository repo)
                {
                    FdpLog<EntityBuildingDamageIngressTranslator>.Warn(
                        "[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                    return;
                }
                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }

            if (view is EntityRepository ownerCheck && Owns(ownerCheck, entity)) return;

            // ⭐ CE-4427 R-2 — resolve in THIS node's world, or not at all: never the sender's index. A world that is still loading (no terrain, or one with no buildings yet) DEFERS the sample and retries it on
            //   the next poll; a resident world that does not know the key DROPS it (a breach must never land on another house).
            var terrain = TerrainOf(view);
            if (terrain == null || terrain.Buildings.Count == 0) { _deferred[netId] = data; return; }
            _deferred.Remove(netId);
            if (!TryFromSample(in data, terrain, out var component))
            {
                if (_unresolved.Add(netId))
                    FdpLog<EntityBuildingDamageIngressTranslator>.Warn("[Node-{0}] Building damage for NetID {1} names '{2}', which this node's terrain does not have: dropped (never applied to another building).", _localNodeId, netId, data.Key);
                return;
            }
            cmd.SetComponent(entity, component);
            if (!string.IsNullOrEmpty(data.Key)
                && !(view is EntityRepository r && r.HasComponent<TerrainObjectKey>(entity) && r.GetComponent<TerrainObjectKey>(entity).Key == data.Key))
                cmd.SetManagedComponent(entity, new TerrainObjectKey { Key = data.Key });
        }

        // ⭐ CE-4427 R-2 — samples that arrived before this node's terrain (the latest per instance), retried by PollIngress
        private readonly Dictionary<int, EntityBuildingDamage> _deferred = new();
        private readonly HashSet<int> _unresolved = new();
        private readonly List<int> _retry = new();

        /// <summary>The samples waiting for this node's terrain (the test seam).</summary>
        internal int DeferredCount => _deferred.Count;

        private void RetryDeferred(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_deferred.Count == 0) return;
            var terrain = TerrainOf(view);
            if (terrain == null || terrain.Buildings.Count == 0) return;   // still loading
            _retry.Clear();
            foreach (var id in _deferred.Keys) _retry.Add(id);
            foreach (var id in _retry)
                if (_deferred.TryGetValue(id, out var d)) Apply(in d, cmd, view);
        }

        /// <summary>The component a sample carries (the test seam); a sample this world cannot resolve is an EMPTY component with Building −1.</summary>
        internal static BuildingDamage FromSample(in EntityBuildingDamage d, Fdp.Toolkit.Terrain.TerrainWorld? terrain = null)
        {
            if (terrain == null)
            {
                // the no-terrain seam (a test with no world): the sender's indices as they are — production never gets here (Apply defers until a world is resident)
                var raw = default(BuildingDamage);
                raw.Building = d.Building; raw.Collapsed = d.Collapsed;
                int rn = Math.Min(d.Breaches?.Count ?? 0, BuildingDamage.Capacity);
                for (int i = 0; i < rn; i++) { var b = d.Breaches![i]; raw.Set(i, new WallBreach(b.Panel, b.From, b.To, b.Bottom, b.Top, (BreachKind)b.Kind)); }
                raw.Count = (byte)rn;
                return raw;
            }
            if (TryFromSample(in d, terrain, out var c)) return c;
            var none = default(BuildingDamage); none.Building = -1;
            return none;
        }

        /// <summary>
        /// ⭐ CE-4427 R-2 — the component in THIS world: the building the key names HERE and each breach's panel mapped from its within-building rank to this world's global index. False when the key names no building here
        /// (⛔ never the sender's raw index — it is a within-building rank, not a global one, and would breach another house).
        /// </summary>
        internal static bool TryFromSample(in EntityBuildingDamage d, Fdp.Toolkit.Terrain.TerrainWorld terrain, out BuildingDamage c)
        {
            c = default;
            int building = BuildingDamages.IndexOfKey(terrain, d.Key);
            if (building < 0) return false;
            c.Building = building;
            c.Collapsed = d.Collapsed;
            int n = Math.Min(d.Breaches?.Count ?? 0, BuildingDamage.Capacity);
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                var b = d.Breaches![i];
                int panel = BuildingPanelIndex.ToGlobal(terrain, building, b.Panel);   // the wire's panel is within the building
                if (panel < 0) continue;                                              // no such wall here: never breach another
                c.Set(k++, new WallBreach(panel, b.From, b.To, b.Bottom, b.Top, (BreachKind)b.Kind));
            }
            c.Count = (byte)k;
            return true;
        }

        private static Fdp.Toolkit.Terrain.TerrainWorld? TerrainOf(ISimulationView view)
            => view is EntityRepository r && r.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() ? r.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() : null;

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityBuildingDamage d || Owns(repo, entity)) return;
            var terrain = TerrainOf(repo);
            if (terrain == null || terrain.Buildings.Count == 0) { _deferred[d.EntityId] = d; return; }   // ⭐ CE-4427 R-2 — as Apply: wait for the world
            if (!TryFromSample(in d, terrain, out var component)) return;                                    // resident and unknown: dropped
            repo.SetComponent(entity, component);
            if (!string.IsNullOrEmpty(d.Key)) repo.SetManagedComponent(entity, new TerrainObjectKey { Key = d.Key });
        }

        public void Dispose(int networkEntityId) { _deferred.Remove(networkEntityId); _unresolved.Remove(networkEntityId); }

        private static bool Owns(EntityRepository repo, Entity entity)
            => AuthorityExtensions.IsRecordedOwner(repo, entity, OwnershipExtensions.PackKey(OrdinalValue, 0));
    }
}
