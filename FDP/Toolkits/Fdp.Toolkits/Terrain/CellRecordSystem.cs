using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-5000</c> — a per-CELL damage record (a ground cell's craters, a cell's felled trunks): a cell, a fixed capacity of items, the items in use. Implemented by the
    /// unmanaged component itself, so the generic writer reads and writes it through constrained calls (no boxing).
    /// </summary>
    public interface ICellRecord<TItem> where TItem : unmanaged, IEquatable<TItem>
    {
        int CellX { get; set; }
        int CellY { get; set; }
        int ItemCount { get; }
        bool IsFull { get; }
        TItem Item(int i);
        /// <summary>Adds <paramref name="item"/>: false when already there or the record is full (<paramref name="full"/>).</summary>
        bool TryAdd(in TItem item, out bool full);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-5000</c> (ruling 9: one implementation per concept) — THE writer of a per-cell damage record: ONE entity per (cell, DECIDING node), made at that node's first item
    /// in the cell and owned by it; past capacity a second entity; an item for a cell whose creation request is in flight waits and is applied when the entity appears (matched
    /// by the cell). ⛔ Never in a replay; writes only entities this node owns (<see cref="DescriptorKey"/>). <see cref="GroundDamageSystem"/> (craters) and
    /// <see cref="TreeDamageSystem"/> (felled trunks) are this class with their event and their cell rule. ⚠ <see cref="BuildingDamageSystem"/> is NOT: its record is keyed by a
    /// BUILDING (a terrain-object key), it MERGES overlapping breaches and carries a collapse — a different concept, said in CE-5000.
    /// </summary>
    public abstract class CellRecordSystem<TRecord, TItem> : IEcsModuleSystem
        where TRecord : unmanaged, ICellRecord<TItem>
        where TItem : unmanaged, IEquatable<TItem>
    {
        /// <summary>A request that never produced an entity within this many ticks is forgotten (a later item asks again).</summary>
        public const int PendingTimeoutTicks = 3600;

        /// <summary>Items written into an entity (rails, diagnostics).</summary>
        public long Applied { get; private set; }
        /// <summary>Creation requests published.</summary>
        public long Requested { get; private set; }
        /// <summary>How many cells are waiting on a creation request (rails).</summary>
        public int PendingCount => _pending.Count;

        private sealed class Pending { public int CellX, CellY, Age; public readonly List<TItem> Later = new(); }
        private readonly Dictionary<long, Pending> _pending = new();
        private readonly List<long> _done = new();
        private readonly List<TItem> _items = new();
        private EntityQuery? _query;
        private EntityRepository? _queryRepo;

        /// <summary>The ownership key the record is published under (its descriptor's).</summary>
        protected abstract long DescriptorKey { get; }
        /// <summary>The TKB type of a record entity.</summary>
        protected abstract long TkbType { get; }
        /// <summary>The cell an item belongs to.</summary>
        protected abstract (int X, int Y) CellOf(in TItem item);
        /// <summary>This tick's items from the deciding events (into <paramref name="into"/>, cleared by the caller).</summary>
        protected abstract void ReadItems(EntityRepository repo, List<TItem> into);

        private static long Key(int x, int y) => ((long)x << 32) | (uint)y;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // a replay: the recording owns the world
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<TRecord>()) return;
            if (_pending.Count > 0) DrainPending(repo);
            _items.Clear();
            ReadItems(repo, _items);
            for (int i = 0; i < _items.Count; i++) Apply(repo, _items[i]);
        }

        private void Apply(EntityRepository repo, in TItem item)
        {
            var (cx, cy) = CellOf(in item);
            if (_pending.TryGetValue(Key(cx, cy), out var waiting)) { waiting.Later.Add(item); return; }
            Entity space = Entity.Null;
            foreach (var e in Query(repo))
            {
                if (!Owns(repo, e)) continue;
                var d = repo.GetComponentRO<TRecord>(e);
                if (d.CellX != cx || d.CellY != cy) continue;
                for (int k = 0; k < d.ItemCount; k++) if (d.Item(k).Equals(item)) return;   // already here
                if (space.IsNull && !d.IsFull) space = e;
            }
            if (!space.IsNull)
            {
                var d = repo.GetComponentRO<TRecord>(space);
                if (d.TryAdd(in item, out _)) { repo.SetComponent(space, d); Applied++; }
                return;
            }
            var born = default(TRecord);
            born.CellX = cx; born.CellY = cy;
            born.TryAdd(in item, out _);
            int node = repo.HasSingletonManaged<INetworkTopology>() ? (int)(repo.GetSingletonManaged<INetworkTopology>()?.LocalNodeId ?? default) : 0;
            repo.Bus.PublishManaged(new EntityCreationRequest
            {
                RequestId = Guid.NewGuid(),
                OwnerAppInstanceId = node,           // THIS node owns it (0 = no topology: a single node)
                TkbType = (Fdp.Core.TkbTypeId)TkbType,
                InitType = ReliableInitType.None,    // a damage record waits for no peer
                IsTransient = true,                  // never written into a scenario as an entity (a save entry carries the items)
                InitialComponents = new List<object>
                {
                    new SimTransform { Position = GroundDamages.Anchor(cx, cy, 0f), Rotation = Quaternion.Identity },
                    born,
                },
            });
            _pending[Key(cx, cy)] = new Pending { CellX = cx, CellY = cy };
            Requested++;
        }

        private void DrainPending(EntityRepository repo)
        {
            _done.Clear();
            foreach (var (key, p) in _pending)
            {
                bool here = false;
                foreach (var e in Query(repo))
                {
                    if (!Owns(repo, e)) continue;
                    var d = repo.GetComponentRO<TRecord>(e);
                    if (d.CellX == p.CellX && d.CellY == p.CellY) { here = true; break; }
                }
                if (here || ++p.Age > PendingTimeoutTicks) _done.Add(key);
            }
            foreach (var key in _done)
            {
                var p = _pending[key];
                _pending.Remove(key);   // first: Apply must not see it as waiting
                for (int i = 0; i < p.Later.Count; i++) Apply(repo, p.Later[i]);
            }
        }

        /// <summary>True when this node writes <paramref name="e"/>: the owner of its descriptor (⛔ never a ghost).</summary>
        protected bool Owns(EntityRepository repo, Entity e) => OwnsRecord(repo, e, DescriptorKey);

        /// <summary>The ownership test, shared by the writer and its save section.</summary>
        public static bool OwnsRecord(EntityRepository repo, Entity e, long descriptorKey)
        {
            if (!repo.IsAlive(e) || !repo.HasComponent<TRecord>(e)) return false;
            if (!(repo.IsComponentTypeRegistered<NetworkAuthority>() && repo.HasComponent<NetworkAuthority>(e))
                && repo.GetLifecycleState(e) == EntityLifecycle.Ghost) return false;
            return Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, e, descriptorKey);
        }

        private EntityQuery Query(EntityRepository repo)
        {
            if (!ReferenceEquals(repo, _queryRepo)) { _query = repo.Query().With<TRecord>().WithLifecycle(EntityLifecycle.AllButTearDown).Build(); _queryRepo = repo; }
            return _query!;
        }
    }
}
