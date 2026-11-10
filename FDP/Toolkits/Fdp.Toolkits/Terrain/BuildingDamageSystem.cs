using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-4391</c> S-1 (UTM §8.2 B-5) — "a detonation breached this wall": published by <c>AreaEffectSystem.BreachWalls</c> (the deciding node, a
    /// non-remote detonation) for each breach, read by <see cref="BuildingDamageSystem"/> the frame after. LOCAL (never on the wire): the entity the
    /// system writes is what replicates. <see cref="Panel"/> is the global panel index, <see cref="Kind"/> a <see cref="BreachKind"/>.
    /// </summary>
    [EventId(TerrainEventIds.BuildingDamage)]
    [StructLayout(LayoutKind.Sequential)]
    public struct BuildingDamageEvent
    {
        public int Building;
        public int Panel;
        public float From;
        public float To;
        public float Bottom;
        public float Top;
        public byte Kind;
        /// <summary>Who fired the round (for logs; <see cref="Entity.Null"/> when unknown).</summary>
        public Entity Shooter;

        public readonly WallBreach Breach => new(Panel, From, To, Bottom, Top, (BreachKind)Kind);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4391</c> S-1 (UTM §8.2 B-2, B-4) — turns <see cref="BuildingDamageEvent"/>s into <see cref="BuildingDamage"/> entities. ONE entity per
    /// (building, DECIDING node), made at that node's first breach of the building and owned by it (so there is no cross-node request and no creation
    /// race: two Muscles hitting one untouched house in one frame each make their own, and the view unions them — <see cref="BuildingDamageTable"/>).
    /// <para>A breach overlapping one of this node's on the same panel is MERGED into the union rectangle; past <see cref="BuildingDamage.Capacity"/>
    /// a second entity is opened. A breach for a building whose creation request is still in flight (the entity is made a few frames later, by the creation
    /// system) waits in a system-local list and is applied when the entity appears — matched by the building's key, never by a handle.</para>
    /// <para>⛔ Never runs during a replay (the recording owns the world: <c>AreaEffectSystem</c> does not decide there either). ⛔ Writes only entities THIS
    /// node owns (<see cref="DescriptorKey"/>); a replica or a ghost is never touched.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class BuildingDamageSystem : IEcsModuleSystem
    {
        /// <summary>
        /// The wire ordinal of the building-damage descriptor (<c>EDescriptorType.dtBuildingDamage</c> in the NED assembly, which this toolkit cannot
        /// reference). ⚠ A rail in the SimHost suite pins the two equal.
        /// </summary>
        public const long DescriptorOrdinal = 183;

        /// <summary>The ownership key a building-damage entity is published under — this system and the egress both gate on it.</summary>
        public static readonly long DescriptorKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

        /// <summary>A request that never produced an entity within this many ticks is forgotten (a later breach asks again).</summary>
        public const int PendingTimeoutTicks = 3600;

        /// <summary>Breaches written into an entity (a rail and a diagnostics counter read it).</summary>
        public long Applied { get; private set; }
        /// <summary>Creation requests published.</summary>
        public long Requested { get; private set; }

        private sealed class Pending
        {
            public int Building;
            public int Age;
            public readonly List<WallBreach> Later = new();
        }

        private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
        private readonly List<string> _done = new();
        private EntityQuery? _query;
        private EntityRepository? _queryRepo;

        /// <summary>How many buildings are waiting on a creation request (rails).</summary>
        public int PendingCount => _pending.Count;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // a replay: the recording owns the world
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<BuildingDamage>() || !repo.HasSingletonManaged<TerrainWorld>()) return;
            var terrain = repo.GetSingletonManaged<TerrainWorld>();
            if (terrain == null) return;

            if (_pending.Count > 0) DrainPending(repo, terrain);

            if (!repo.Bus.IsRegistered<BuildingDamageEvent>()) return;
            var events = repo.ReadEvents<BuildingDamageEvent>();
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                if ((uint)evt.Building >= (uint)terrain.Buildings.Count || (uint)evt.Panel >= (uint)terrain.Panels.Count) continue;
                Apply(repo, terrain, evt.Building, evt.Breach);
            }
        }

        /// <summary>Writes <paramref name="breach"/> into this node's entity for the building (merging or appending), else asks for one.</summary>
        private void Apply(EntityRepository repo, TerrainWorld terrain, int building, in WallBreach breach)
        {
            string key = BuildingDamages.KeyOf(terrain, building);
            if (_pending.TryGetValue(key, out var waiting)) { waiting.Later.Add(breach); return; }   // the entity is not here yet: applied when it is

            Entity space = Entity.Null;   // the first owned entity of this building with room
            foreach (var e in Query(repo))
            {
                if (!OwnsDamage(repo, e)) continue;
                var d = repo.GetComponentRO<BuildingDamage>(e);
                if (d.Building != building) continue;
                bool overlaps = false;
                for (int i = 0; i < d.Count && !overlaps; i++) overlaps = d.Get(i).Overlaps(breach);
                if (overlaps) { Write(repo, terrain, e, d, breach); return; }   // merging never needs room
                if (space.IsNull && d.Count < BuildingDamage.Capacity) space = e;
            }
            if (!space.IsNull) { Write(repo, terrain, space, repo.GetComponentRO<BuildingDamage>(space), breach); return; }
            Request(repo, terrain, building, key, breach);   // no entity of ours (or all full): make one — it is born holding this breach
        }

        private void Write(EntityRepository repo, TerrainWorld terrain, Entity e, BuildingDamage d, in WallBreach breach)
        {
            // copy → change → write back only when something changed: an already-covered breach writes nothing (no version bump, no republish)
            if (BuildingDamages.Add(ref d, in breach, terrain) is BreachAdd.Unchanged or BreachAdd.Full) return;
            repo.SetComponent(e, d);
            Applied++;
        }

        private void DrainPending(EntityRepository repo, TerrainWorld terrain)
        {
            _done.Clear();
            foreach (var (key, p) in _pending)
            {
                bool here = false;
                foreach (var e in Query(repo))
                    if (OwnsDamage(repo, e) && repo.GetComponentRO<BuildingDamage>(e).Building == p.Building) { here = true; break; }
                if (here || ++p.Age > PendingTimeoutTicks) _done.Add(key);
            }
            foreach (var key in _done)
            {
                var p = _pending[key];
                _pending.Remove(key);   // first: Apply must not see it as waiting
                for (int i = 0; i < p.Later.Count; i++) Apply(repo, terrain, p.Building, p.Later[i]);
            }
        }

        private void Request(EntityRepository repo, TerrainWorld terrain, int building, string key, in WallBreach breach)
        {
            var d = new BuildingDamage { Building = building };
            BuildingDamages.Add(ref d, in breach, terrain);
            var b = terrain.Buildings[building];
            int node = repo.HasSingletonManaged<INetworkTopology>() ? (int)(repo.GetSingletonManaged<INetworkTopology>()?.LocalNodeId ?? default) : 0;
            repo.Bus.PublishManaged(new EntityCreationRequest
            {
                RequestId = Guid.NewGuid(),
                OwnerAppInstanceId = node,           // ⭐ B-2: THIS node owns it (0 = no topology: a single node, the default processor is this one)
                TkbType = (Fdp.Core.TkbTypeId)BuildingDamages.TkbType,
                InitType = Fdp.Toolkit.Replication.ReliableInitType.None,   // a breach waits for no peer
                IsTransient = true,                  // never written into a scenario as an entity: the terrain-object section saves the breaches (S-4)
                InitialComponents = new List<object>
                {
                    new SimTransform { Position = new Vector3(b.Position, b.BaseZ), Rotation = Quaternion.Identity },
                    d,
                    new TerrainObjectKey { Key = key },
                },
            });
            _pending[key] = new Pending { Building = building };
            Requested++;
        }

        /// <summary>
        /// True when this node writes <paramref name="e"/>: the owner of its <c>dtBuildingDamage</c> descriptor, the same test its egress publishes under.
        /// With no <see cref="NetworkAuthority"/> (one node, the editor) every such entity is ours — ⛔ except a GHOST, a replica whose master has not
        /// arrived (applying there would fork the state).
        /// </summary>
        public static bool OwnsDamage(EntityRepository repo, Entity e)
        {
            if (!repo.IsAlive(e) || !repo.HasComponent<BuildingDamage>(e)) return false;
            if (!(repo.IsComponentTypeRegistered<NetworkAuthority>() && repo.HasComponent<NetworkAuthority>(e))
                && repo.GetLifecycleState(e) == EntityLifecycle.Ghost) return false;
            return Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, e, DescriptorKey);
        }

        private EntityQuery Query(EntityRepository repo)
        {
            if (!ReferenceEquals(repo, _queryRepo))
            {
                _query = repo.Query().With<BuildingDamage>().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
                _queryRepo = repo;
            }
            return _query!;
        }
    }
}
