using System;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Spatial
{
    /// <summary>
    /// ⭐ <c>CE-2411</c> — ONE per-frame grid of an entity set's flat (X, Y) positions, for the "who is near / on this line" questions an
    /// AI or combat rule asks per entity per frame. 📐 On the CE-2408 stress world each such question scanned the whole world
    /// (<c>UnderFire.FriendNear</c> 9 %, <c>LineOfFire</c> 18 % of the CGF node's thread); answered from this, they walk the few cells near
    /// the point or along the line.
    /// <para>⭐ What it guarantees: a CANDIDATE superset. Every entity of the set whose frame position lies within the walk's reach is
    /// returned; the caller re-reads each candidate LIVE (position, force, state) and applies its exact rule, so a filter never goes stale.
    /// ⚠ What is the frame's: the cell an entity sits in — rebuilt when <see cref="EntityRepository.GlobalVersion"/> moves; an entity that JOINS the set within
    /// the frame is found from a late list (see <see cref="EnsureFresh"/>); an entity MOVED within the frame is found only within the caller's slack.</para>
    /// <para>⛔ Not <c>SpatialHashGrid</c> (CarKinem): that one is the collider broadphase for rounds — fixed extent with a capacity, hidden
    /// passengers left out, SimHost only; these rules must see exactly what their full scan saw, anywhere, on any host. Sorted cell keys:
    /// unbounded, no allocation once grown.</para>
    /// </summary>
    public sealed class FrameEntityGrid
    {
        private readonly float _cell;
        private long[] _keys = Array.Empty<long>();
        private int[] _order = Array.Empty<int>();
        private float[] _x = Array.Empty<float>(), _y = Array.Empty<float>();
        private Entity[] _entity = Array.Empty<Entity>();
        private int _n;
        private uint _builtAt;
        private long _seenStructure;
        private bool _built;
        private ushort[] _placedGen = Array.Empty<ushort>();   // [entity index] = the generation placed by the build + 1 (0 = not placed)
        private Entity[] _late = Array.Empty<Entity>();         // members that joined after the build, within the frame
        private int _lateCount;
        private int[] _found = new int[64];
        private int _foundCount;

        public FrameEntityGrid(float cellMetres = 16f) => _cell = cellMetres;

        /// <summary>How many entities the last build placed.</summary>
        public int Count => _n;

        /// <summary>How many builds so far (a rail reads it).</summary>
        public int Builds { get; private set; }

        /// <summary>
        /// Rebuild from <paramref name="set"/> when the frame moved. Within the frame, when the world's structure moved
        /// (<see cref="EntityRepository.StructuralChanges"/>) the entities structurally changed THIS frame are looked at, and a member of
        /// the set the build did not place joins a short late list every walk also checks — so an entity created (or given the set's
        /// components) within the frame is found at once, without a rebuild per creation. <paramref name="member"/> says whether an
        /// entity is in the set (the query's components). True when it rebuilt — the caller recomputes anything it derives per build.
        /// </summary>
        public bool EnsureFresh(EntityRepository world, EntityQuery set, Func<EntityRepository, Entity, bool> member)
        {
            _world = world;
            uint now = world.GlobalVersion;
            if (_built && now == _builtAt)
            {
                if (world.StructuralChanges != _seenStructure) CatchUp(world, member);
                return false;
            }
            _built = true; _builtAt = now; _seenStructure = world.StructuralChanges; _n = 0; _lateCount = 0;
            Builds++;
            int maxIndex = world.MaxEntityIndex;
            if (_placedGen.Length <= maxIndex) Array.Resize(ref _placedGen, Math.Max(256, (maxIndex + 1) * 2));
            Array.Clear(_placedGen);
            foreach (var e in set)
            {
                if (!world.HasComponent<SimTransform>(e)) continue;
                if (_n == _keys.Length)
                {
                    int size = Math.Max(256, _n * 2);
                    Array.Resize(ref _keys, size); Array.Resize(ref _order, size); Array.Resize(ref _x, size); Array.Resize(ref _y, size);
                    Array.Resize(ref _entity, size);
                }
                var p = world.GetComponentRO<SimTransform>(e).Position;
                _x[_n] = p.X; _y[_n] = p.Y; _entity[_n] = e;
                _keys[_n] = Key(CellOf(p.X), CellOf(p.Y));
                _order[_n] = _n;
                if (e.Index < _placedGen.Length) _placedGen[e.Index] = (ushort)(e.Generation + 1);
                _n++;
            }
            Array.Sort(_keys, _order, 0, _n);   // keys sorted; _order[i] = the entry at key position i
            return true;
        }

        /// <summary>⭐ CE-2413 — rebuild NOW, whatever the clocks say: for a caller whose old answer was a fresh collection per call
        /// (<c>TrafficReplicas</c>' anchors) and must stay exactly that.</summary>
        public void Rebuild(EntityRepository world, EntityQuery set, Func<EntityRepository, Entity, bool> member)
        {
            _built = false;
            EnsureFresh(world, set, member);
        }

        /// <summary>⭐ CE-2413 — is any member's frame position within <paramref name="radius"/> of <paramref name="at"/> (flat, inclusive)?
        /// Stops at the first. For a rule that only needs the yes/no (a vicinity band), not the candidates.</summary>
        public bool AnyWithin(Vector2 at, float radius)
        {
            float r2 = radius * radius;
            int x0 = CellOf(at.X - radius), x1 = CellOf(at.X + radius), y0 = CellOf(at.Y - radius), y1 = CellOf(at.Y + radius);
            for (int cx = x0; cx <= x1; cx++)
                for (int cy = y0; cy <= y1; cy++)
                {
                    long key = Key(cx, cy);
                    int i = Array.BinarySearch(_keys, 0, _n, key);
                    if (i < 0) continue;
                    while (i > 0 && _keys[i - 1] == key) i--;
                    for (; i < _n && _keys[i] == key; i++)
                    {
                        int k = _order[i];
                        float dx = _x[k] - at.X, dy = _y[k] - at.Y;
                        if (dx * dx + dy * dy <= r2) return true;
                    }
                }
            if (_lateCount > 0 && _world is not null)
                for (int j = 0; j < _lateCount; j++)
                {
                    var e = _late[j];
                    if (!_world.IsAlive(e) || !_world.HasComponent<SimTransform>(e)) continue;
                    var p = _world.GetComponentRO<SimTransform>(e).Position;
                    float dx = p.X - at.X, dy = p.Y - at.Y;
                    if (dx * dx + dy * dy <= r2) return true;
                }
            return false;
        }

        /// <summary>The structure moved within the frame: whoever was structurally changed this frame and is a member the build did not
        /// place joins the late list (destroyed ones simply stop being alive — callers re-check every candidate live).</summary>
        private void CatchUp(EntityRepository world, Func<EntityRepository, Entity, bool> member)
        {
            _seenStructure = world.StructuralChanges;
            _lateCount = 0;
            var index = world.GetEntityIndex();
            int max = world.MaxEntityIndex;
            for (int i = 0; i <= max; i++)
            {
                ref var meta = ref index.GetMetadata(i);
                if (!meta.IsActive || meta.LastChangeTick < _builtAt) continue;
                var e = new Entity(i, meta.Generation);
                if (i < _placedGen.Length && _placedGen[i] == (ushort)(meta.Generation + 1)) continue;   // placed by the build
                if (!world.HasComponent<SimTransform>(e) || !member(world, e)) continue;
                if (_lateCount == _late.Length) Array.Resize(ref _late, Math.Max(16, _lateCount * 2));
                _late[_lateCount++] = e;
            }
        }

        /// <summary>How many members joined within the frame after the build (a rail reads it).</summary>
        public int LateCount => _lateCount;

        /// <summary>The entity the last build placed at <paramref name="entry"/> (0 ≤ entry &lt; <see cref="Count"/>).</summary>
        public Entity EntityAt(int entry) => entry < _n ? _entity[entry] : _late[entry - _n];

        /// <summary>Candidates whose frame position is within <paramref name="radius"/> of <paramref name="at"/> (flat). Written to
        /// <see cref="Found"/>; valid until the next query.</summary>
        public int Circle(Vector2 at, float radius)
        {
            _foundCount = 0;
            float r2 = radius * radius;
            int x0 = CellOf(at.X - radius), x1 = CellOf(at.X + radius), y0 = CellOf(at.Y - radius), y1 = CellOf(at.Y + radius);
            for (int cx = x0; cx <= x1; cx++)
                for (int cy = y0; cy <= y1; cy++)
                    Collect(cx, cy, at, r2, default, 0f);
            CollectLate(_world!, at, r2, default, 0f);
            return _foundCount;
        }

        /// <summary>Candidates whose frame position is within <paramref name="pad"/> of the segment <paramref name="a"/>→<paramref name="b"/>
        /// (flat) — the cells along it, not its bounding box. Written to <see cref="Found"/>; valid until the next query.</summary>
        public int Segment(Vector2 a, Vector2 b, float pad)
        {
            _foundCount = 0;
            var ab = b - a;
            float len2 = ab.LengthSquared();
            float reach = pad + _cell * 0.70710678f;   // a cell is walked when its centre is within pad + half its diagonal
            float reach2 = reach * reach;
            int x0 = CellOf(MathF.Min(a.X, b.X) - pad), x1 = CellOf(MathF.Max(a.X, b.X) + pad);
            int y0 = CellOf(MathF.Min(a.Y, b.Y) - pad), y1 = CellOf(MathF.Max(a.Y, b.Y) + pad);
            for (int cx = x0; cx <= x1; cx++)
                for (int cy = y0; cy <= y1; cy++)
                {
                    var centre = new Vector2((cx + 0.5f) * _cell, (cy + 0.5f) * _cell);
                    float t = len2 > 1e-12f ? Math.Clamp(Vector2.Dot(centre - a, ab) / len2, 0f, 1f) : 0f;
                    if (Vector2.DistanceSquared(a + ab * t, centre) > reach2) continue;
                    Collect(cx, cy, a, pad * pad, ab, len2);
                }
            CollectLate(_world!, a, pad * pad, ab, len2);
            return _foundCount;
        }

        /// <summary>Every entry, as candidates — the full scan, for a rail that compares the walks against it.</summary>
        public int All()
        {
            if (_found.Length < _n + _lateCount) Array.Resize(ref _found, _n + _lateCount);
            for (int i = 0; i < _n + _lateCount; i++) _found[i] = i;
            return _foundCount = _n + _lateCount;
        }

        /// <summary>The entries the last <see cref="Circle"/> / <see cref="Segment"/> found (read with <see cref="EntityAt"/>).</summary>
        public ReadOnlySpan<int> Found => new(_found, 0, _foundCount);

        private EntityRepository? _world;

        private void CollectLate(EntityRepository world, Vector2 a, float reach2, Vector2 ab, float len2)
        {
            for (int j = 0; j < _lateCount; j++)
            {
                var e = _late[j];
                if (!world.IsAlive(e) || !world.HasComponent<SimTransform>(e)) continue;
                var p3 = world.GetComponentRO<SimTransform>(e).Position;
                var p = new Vector2(p3.X, p3.Y);
                var near = a;
                if (len2 > 1e-12f) near = a + ab * Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
                if (Vector2.DistanceSquared(p, near) > reach2) continue;
                if (_foundCount == _found.Length) Array.Resize(ref _found, _found.Length * 2);
                _found[_foundCount++] = _n + j;
            }
        }

        private void Collect(int cx, int cy, Vector2 a, float reach2, Vector2 ab, float len2)
        {
            long key = Key(cx, cy);
            int i = Array.BinarySearch(_keys, 0, _n, key);
            if (i < 0) return;
            while (i > 0 && _keys[i - 1] == key) i--;
            for (; i < _n && _keys[i] == key; i++)
            {
                int k = _order[i];
                var p = new Vector2(_x[k], _y[k]);
                var near = a;
                if (len2 > 1e-12f) near = a + ab * Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
                if (Vector2.DistanceSquared(p, near) > reach2) continue;
                if (_foundCount == _found.Length) Array.Resize(ref _found, _found.Length * 2);
                _found[_foundCount++] = k;
            }
        }

        private int CellOf(float v) => (int)MathF.Floor(v / _cell);
        private static long Key(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;
    }
}
