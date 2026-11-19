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
    /// ⭐ <c>CE-4624</c> MS-T2 (DESIGN_Minefields.md KR-6) — a runtime record keyed by <typeparamref name="TKey"/>: its key, whether it is full, whether an item is already in it, and
    /// adding (or merging) one. Implemented by the unmanaged component itself, so the generic writer reads and writes it through constrained calls (no boxing).
    /// </summary>
    public interface IKeyedRecord<TKey, TItem> where TKey : unmanaged, IEquatable<TKey> where TItem : unmanaged
    {
        TKey RecordKey { get; set; }
        bool IsFull { get; }
        /// <summary>True when <paramref name="item"/> would change nothing (already there, or a lattice value already as high).</summary>
        bool Holds(in TItem item);
        /// <summary>Adds / merges <paramref name="item"/>; false when the record is full.</summary>
        bool TryAdd(in TItem item);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-T2 (DESIGN_Minefields.md KR-6, MR-2, KR-5) — THE writer of a KEYED runtime record, extracted from <see cref="CellRecordSystem{TRecord,TItem}"/>'s
    /// core (pending, drain, owns, the creation request) with the cell generalised to a key: ONE entity per (key, DECIDING node), made at that node's first item for the key and
    /// owned by it; past capacity another entity; an item for a key whose creation is in flight waits and is applied when the entity appears. ⛔ Never in a replay; writes only
    /// entities this node owns (<see cref="DescriptorKey"/>); readers take the UNION of every node's entities of a key (KR-5). ⚠ <c>CellRecordSystem</c> and
    /// <c>BuildingDamageSystem</c> are NOT folded onto it yet — CE-4627 (the design's order: no rush).
    /// </summary>
    public abstract class RecordWriterCore<TRecord, TKey, TItem> : IEcsModuleSystem
        where TRecord : unmanaged, IKeyedRecord<TKey, TItem>
        where TKey : unmanaged, IEquatable<TKey>
        where TItem : unmanaged
    {
        public const int PendingTimeoutTicks = 3600;

        [field: Fdp.Core.WorldDerived("diagnostic counter of items written (Applied)")]
        public long Applied { get; private set; }
        [field: Fdp.Core.WorldDerived("diagnostic counter of creation requests (Requested)")]
        public long Requested { get; private set; }
        public int PendingCount => _pending.Count;

        private sealed class Pending { public TKey Key; public int Age; public readonly List<TItem> Later = new(); }
        [Fdp.Core.ProtocolCache("keys waiting on an entity-creation request, with items queued: request in flight (Pending)")]
        private readonly Dictionary<TKey, Pending> _pending = new();
        private int _worldEpoch;
        [Fdp.Core.FrameScratch("keys resolved this drain: cleared at the top of DrainPending")]
        private readonly List<TKey> _done = new();
        [Fdp.Core.FrameScratch("items of this tick: cleared in Execute before BeforeItems/ReadItems")]
        private readonly List<TItem> _items = new();
        private EntityQuery? _query;
        private EntityRepository? _queryRepo;

        protected abstract long DescriptorKey { get; }
        protected abstract long TkbType { get; }
        protected abstract TKey KeyOf(in TItem item);
        /// <summary>This tick's items (into <paramref name="into"/>, cleared by the caller).</summary>
        protected abstract void ReadItems(EntityRepository repo, List<TItem> into);
        /// <summary>A new record for <paramref name="key"/> (its key set; the chunk numbered by <paramref name="existing"/>).</summary>
        protected abstract TRecord NewRecord(in TKey key, int existing);
        /// <summary>Where a record entity stands.</summary>
        protected virtual Vector3 AnchorOf(EntityRepository repo, in TKey key) => Vector3.Zero;
        /// <summary>Runs every tick before the items (a periodic rule: known-at-start, an edge seen).</summary>
        protected virtual void BeforeItems(EntityRepository repo, List<TItem> into) { }

        /// <summary>Whether this instance writes <paramref name="repo"/> (a writer registered by two packs of one host claims the world once).</summary>
        protected virtual bool ShouldRun(EntityRepository repo) => true;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _worldEpoch)) _pending.Clear();   // ⭐ R-4297 ④ (CE-1980) — a world replacement forgets the record entities asked for (the requests died with the world)
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<TRecord>() || !ShouldRun(repo)) return;
            if (_pending.Count > 0) DrainPending(repo);
            _items.Clear();
            BeforeItems(repo, _items);
            ReadItems(repo, _items);
            for (int i = 0; i < _items.Count; i++) Apply(repo, _items[i]);
        }

        private void Apply(EntityRepository repo, in TItem item)
        {
            var key = KeyOf(in item);
            if (_pending.TryGetValue(key, out var waiting)) { waiting.Later.Add(item); return; }
            Entity space = Entity.Null;
            int existing = 0;
            foreach (var e in Query(repo))
            {
                if (!Owns(repo, e)) continue;
                var d = repo.GetComponentRO<TRecord>(e);
                if (!d.RecordKey.Equals(key)) continue;
                existing++;
                if (d.Holds(in item)) return;
                if (space.IsNull && !d.IsFull) space = e;
            }
            if (!space.IsNull)
            {
                var d = repo.GetComponentRO<TRecord>(space);
                if (d.TryAdd(in item)) { repo.SetComponent(space, d); Applied++; }
                return;
            }
            var born = NewRecord(in key, existing);
            born.TryAdd(in item);
            int node = repo.HasSingletonManaged<INetworkTopology>() ? (int)(repo.GetSingletonManaged<INetworkTopology>()?.LocalNodeId ?? default) : 0;
            repo.Bus.PublishManaged(new EntityCreationRequest
            {
                RequestId = Guid.NewGuid(),
                OwnerAppInstanceId = node,
                TkbType = (Fdp.Core.TkbTypeId)TkbType,
                InitType = ReliableInitType.None,
                IsTransient = true,
                InitialComponents = new List<object> { new SimTransform { Position = AnchorOf(repo, key), Rotation = Quaternion.Identity }, born },
            });
            _pending[key] = new Pending { Key = key };
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
                    if (repo.GetComponentRO<TRecord>(e).RecordKey.Equals(key)) { here = true; break; }
                }
                if (here || ++p.Age > PendingTimeoutTicks) _done.Add(key);
            }
            foreach (var key in _done)
            {
                var p = _pending[key];
                _pending.Remove(key);
                for (int i = 0; i < p.Later.Count; i++) Apply(repo, p.Later[i]);
            }
        }

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
