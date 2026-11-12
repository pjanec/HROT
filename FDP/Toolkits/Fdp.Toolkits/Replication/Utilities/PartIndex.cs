using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Replication.Utilities
{
    /// <summary>
    /// ⭐ <c>CE-2412</c> — ONE answer to "the parts of this parent" (<see cref="PartMetadata"/>: a weapon mount, a turret, a sensor, a
    /// behaviour-owned sensor): parent → its parts, sorted by <see cref="PartMetadata.InstanceId"/> (ties in entity order, as a scan met them).
    /// 📐 Each asker used to scan EVERY part of the world per call — <c>UnitSensors.Of</c> alone was 11.5 % of the CGF node's thread on the
    /// CE-2408 stress world, the mounts 44 % before CE-2410's private index (now retired onto this one, with <c>SensorChildFactory</c>'s).
    /// <para>⭐ Built once per frame (<see cref="EntityRepository.GlobalVersion"/>) and again when a <see cref="PartMetadata"/> is ADDED within
    /// the frame (<see cref="EntityRepository.ComponentsAddedOf"/>) — so a part created this frame is found at once. Every part returned is
    /// re-checked LIVE (alive, still a part of this parent), so a removed or destroyed part never shows.
    /// ⚠ A part RE-PARENTED by a value write within the frame (none in production: every writer adds) is found under its new parent from
    /// the next frame.</para>
    /// <para>On a view that is not an <see cref="EntityRepository"/> (a snapshot) it scans, in the same order — the one fallback, here.</para>
    /// </summary>
    public sealed class PartIndex
    {
        private static readonly ConditionalWeakTable<EntityRepository, PartIndex> s_of = new();

        private int[] _start = Array.Empty<int>(), _count = Array.Empty<int>();
        private Entity[] _items = Array.Empty<Entity>();
        private int[] _idOf = Array.Empty<int>();
        private (int Parent, int Id, Entity E)[] _scratch = Array.Empty<(int, int, Entity)>();
        private uint _builtAt;
        private long _builtAdds = -1;
        private bool _built;
        private EntityQuery? _query;
        // ⭐ CE-4395 — the parts ADDED within the frame after the build (from EntityRepository.AddsThisVersion), merged into a parent's slice
        //   on read: (parent index, instance id, part). 📐 They used to force a full rebuild each — 27 a frame, O(every part) each, at 3 000 men.
        private (int Parent, int Id, Entity E)[] _late = Array.Empty<(int, int, Entity)>();
        private int _lateCount, _logAt;
        private Entity[] _merge = Array.Empty<Entity>();
        private int[] _mergeId = Array.Empty<int>();

        /// <summary>How many builds so far (a rail reads it).</summary>
        public int Builds { get; private set; }

        /// <summary>⭐ CE-4395 — how many parts were taken in WITHOUT a build: added within the frame, after it (a rail reads it).</summary>
        public int LateAdds { get; private set; }

        /// <summary>The index of <paramref name="repo"/> (a rail reads <see cref="Builds"/>).</summary>
        public static PartIndex Of(EntityRepository repo) => s_of.GetValue(repo, static r =>
        {
            r.LogAddsOf(ComponentType<PartMetadata>.ID);   // ⭐ CE-4395 — so an add within the frame is taken in, not rebuilt for
            return new PartIndex();
        });

        /// <summary>
        /// Writes <paramref name="parent"/>'s live parts, lowest <see cref="PartMetadata.InstanceId"/> first, into <paramref name="dest"/>
        /// (as many as fit) and returns how many there ARE — more than <paramref name="dest"/> holds means call again with a bigger one.
        /// </summary>
        public static int PartsOf(ISimulationView view, Entity parent, Span<Entity> dest)
        {
            if (parent.IsNull) return 0;
            if (view is EntityRepository repo)
            {
                if (!repo.IsComponentTypeRegistered<PartMetadata>()) return 0;
                var index = Of(repo);
                long t0 = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();   // ⭐ CE-4395 B-1 — measurement only
                try { lock (index) { index.EnsureFresh(repo); return index.Copy(repo, parent, dest); } }
                finally { Fdp.Toolkit.Diagnostics.QueryTimings.End(S_PartsOf, t0); }
            }
            return Scan(view, parent, dest);
        }

        /// <summary>⭐ N-11 (CE-4473) — refreshes <paramref name="repo"/>'s index serially before the brains' per-entity phase (BrainPhase): inside
        /// it nothing is added (structural changes are deferred), so every read there takes the fast path instead of rebuilding under the lock.</summary>
        public static void PrepareFrame(EntityRepository repo)
        {
            if (!repo.IsComponentTypeRegistered<PartMetadata>()) return;
            var index = Of(repo);
            lock (index) index.EnsureFresh(repo);
        }

        private void EnsureFresh(EntityRepository repo)
        {
            uint now = repo.GlobalVersion;
            long adds = repo.ComponentsAddedOf(ComponentType<PartMetadata>.ID);
            if (_built && now == _builtAt && adds == _builtAdds) return;
            bool sameFrame = _built && now == _builtAt;
            if (sameFrame && TakeLateAdds(repo, adds)) return;   // ⭐ CE-4395 — the frame's new parts, without walking every part again
            _built = true; _builtAt = now; _builtAdds = adds;
            Builds++;
            // ⭐ CE-4395 B-1 — measurement only: every rebuild, and those a part ADDED within the frame forced
            long t0 = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();
            if (sameFrame) Fdp.Toolkit.Diagnostics.QueryTimings.Add(S_RebuildsInFrame, 1);
            try { Rebuild(repo); }
            finally { Fdp.Toolkit.Diagnostics.QueryTimings.End(S_Rebuild, t0); }
            _lateCount = 0;
            _logAt = repo.AddsThisVersion(ComponentType<PartMetadata>.ID).Length;   // the adds so far this version are in the build
        }

        /// <summary>
        /// ⭐ CE-4395 — takes the parts added since the build from the repository's add log. False — and the caller rebuilds, exactly as before —
        /// unless the log accounts for EVERY add the counter saw (a type not logged, a version set back: never trusted alone).
        /// </summary>
        private bool TakeLateAdds(EntityRepository repo, long adds)
        {
            var log = repo.AddsThisVersion(ComponentType<PartMetadata>.ID);
            if (log.Length < _logAt || log.Length - _logAt != adds - _builtAdds) return false;
            for (int k = _logAt; k < log.Length; k++)
            {
                var e = repo.GetEntityByIndex(log[k]);
                if (!repo.IsAlive(e) || !repo.HasComponent<PartMetadata>(e)) continue;   // gone again: a read re-checks anyway
                bool seen = false;
                for (int j = 0; j < _lateCount && !seen; j++) seen = _late[j].E.Equals(e);
                if (seen) continue;                                                       // added, removed and added again
                ref readonly var meta = ref repo.GetComponentRO<PartMetadata>(e);
                if (meta.ParentEntity.IsNull) continue;
                if (_lateCount == _late.Length) Array.Resize(ref _late, Math.Max(16, _lateCount * 2));
                _late[_lateCount++] = (meta.ParentEntity.Index, meta.InstanceId, e);
                LateAdds++;
            }
            _logAt = log.Length;
            _builtAdds = adds;
            return true;
        }

        private static readonly int S_PartsOf = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("parts.PartsOf");
        private static readonly int S_Rebuild = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("parts.rebuild");
        private static readonly int S_RebuildsInFrame = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("parts.rebuild within a frame (count)");

        private void Rebuild(EntityRepository repo)
        {
            int parents = repo.MaxEntityIndex + 1;
            if (_start.Length < parents) { Array.Resize(ref _start, parents * 2); Array.Resize(ref _count, parents * 2); }
            Array.Clear(_count);
            int n = 0;
            _query ??= repo.Query().With<PartMetadata>().Build();
            foreach (var e in _query)
            {
                ref readonly var meta = ref repo.GetComponentRO<PartMetadata>(e);
                var parent = meta.ParentEntity;
                if (parent.IsNull || (uint)parent.Index >= (uint)parents) continue;
                if (n == _scratch.Length) Array.Resize(ref _scratch, Math.Max(256, n * 2));
                _scratch[n++] = (parent.Index, meta.InstanceId, e);
                _count[parent.Index]++;
            }
            int at = 0;   // a slice per parent (counting sort), then each slice by instance id (stable: a handful each)
            for (int p = 0; p < parents; p++) { _start[p] = at; at += _count[p]; _count[p] = 0; }
            if (_items.Length < n) { Array.Resize(ref _items, Math.Max(n, _items.Length * 2)); Array.Resize(ref _idOf, _items.Length); }
            for (int k = 0; k < n; k++)
            {
                var (p, id, e) = _scratch[k];
                int j = _start[p] + _count[p]++;
                while (j > _start[p] && _idOf[j - 1] > id) { _items[j] = _items[j - 1]; _idOf[j] = _idOf[j - 1]; j--; }
                _items[j] = e; _idOf[j] = id;
            }
        }

        private int Copy(EntityRepository repo, Entity parent, Span<Entity> dest)
        {
            if ((uint)parent.Index > (uint)repo.MaxEntityIndex) return 0;
            bool built = (uint)parent.Index < (uint)_start.Length;
            int from = built ? _start[parent.Index] : 0, to = built ? from + _count[parent.Index] : 0, total = 0;
            Entity[] items = _items;
            if (_lateCount > 0) MergeLate(parent.Index, ref from, ref to, ref items);
            for (int k = from; k < to; k++)
            {
                var e = items[k];
                // ⭐ re-checked on every read: a part removed or destroyed after the build, or a slot reused by another parent, never shows
                if (!repo.IsAlive(e) || !repo.HasComponent<PartMetadata>(e) || !repo.GetComponentRO<PartMetadata>(e).ParentEntity.Equals(parent)) continue;
                if (total < dest.Length) dest[total] = e;
                total++;
            }
            return total;
        }

        /// <summary>⭐ CE-4395 — the parent's slice with its late parts merged in, in the build's order: by instance id, ties by entity index
        /// (the order a build's walk meets them). Into a reused buffer; the slice itself is untouched.</summary>
        private void MergeLate(int parent, ref int from, ref int to, ref Entity[] items)
        {
            int extra = 0;
            for (int j = 0; j < _lateCount; j++) if (_late[j].Parent == parent) extra++;
            if (extra == 0) return;
            int n = to - from;
            if (_merge.Length < n + extra) { _merge = new Entity[Math.Max(16, (n + extra) * 2)]; _mergeId = new int[_merge.Length]; }
            Array.Copy(_items, from, _merge, 0, n);
            Array.Copy(_idOf, from, _mergeId, 0, n);
            for (int j = 0; j < _lateCount; j++)
            {
                var (p, id, e) = _late[j];
                if (p != parent) continue;
                bool dup = false;
                for (int k = 0; k < n && !dup; k++) dup = _merge[k].Equals(e);   // still in the build: added, removed and added again
                if (dup) continue;
                int at = n++;
                while (at > 0 && (_mergeId[at - 1] > id || (_mergeId[at - 1] == id && _merge[at - 1].Index > e.Index)))
                { _merge[at] = _merge[at - 1]; _mergeId[at] = _mergeId[at - 1]; at--; }
                _merge[at] = e; _mergeId[at] = id;
            }
            items = _merge; from = 0; to = n;
        }

        private static int Scan(ISimulationView view, Entity parent, Span<Entity> dest)
        {
            int total = 0, kept = 0;
            Span<int> ids = dest.Length <= 256 ? stackalloc int[dest.Length] : new int[dest.Length];
            foreach (var e in view.Query().With<PartMetadata>().Build())
            {
                ref readonly var meta = ref view.GetComponentRO<PartMetadata>(e);
                if (!meta.ParentEntity.Equals(parent)) continue;
                total++;
                if (dest.IsEmpty) continue;
                int id = meta.InstanceId, j = kept;
                if (kept < dest.Length) kept++;
                else if (ids[kept - 1] <= id) continue;   // full, and it sorts after the last kept
                else j = kept - 1;
                while (j > 0 && ids[j - 1] > id) { if (j < dest.Length) { dest[j] = dest[j - 1]; ids[j] = ids[j - 1]; } j--; }
                dest[j] = e; ids[j] = id;
            }
            return total;
        }
    }
}
