using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T3 (DESIGN_Minefields.md MR-3) — ONE minefield as the navmesh bake sees it: the field's stable id, its world OUTLINE, its parameters (the knowledge grid is derived
    /// from them), and its LANES — the authored ones plus every runtime lane a node cleared (<see cref="MinefieldLanes"/>), each with the side that cleared it. Immutable; gathered on the
    /// frame thread (<see cref="MinefieldSet.Of"/>), read by the bake thread.
    /// </summary>
    public sealed class TerrainMinefield
    {
        public Guid Field { get; init; }
        public Minefield Params { get; init; }
        public Vector2 Anchor { get; init; }
        /// <summary>The outline in world points.</summary>
        public Vector2[] Outline { get; init; } = Array.Empty<Vector2>();
        public MinefieldLane[] Lanes { get; init; } = Array.Empty<MinefieldLane>();
        public Vector2 Min { get; init; }
        public Vector2 Max { get; init; }
        /// <summary>A hash of everything the bake reads (outline, lanes, orientation) at 1 cm.</summary>
        public ulong Hash { get; init; }

        private MinefieldKnowledgeOps.KnowledgeGrid? _grid;
        /// <summary>Where this field's knowledge cells lie.</summary>
        public MinefieldKnowledgeOps.KnowledgeGrid Grid => _grid ??= MinefieldKnowledgeOps.Grid(Params, Anchor, Outline);

        /// <summary>True when <paramref name="p"/> lies inside the outline.</summary>
        public bool Contains(Vector2 p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && MinePattern.Inside(Outline, p);

        /// <summary>The index of the first lane whose strip holds <paramref name="p"/> (within <paramref name="slack"/> m of its half width), or −1.</summary>
        public int LaneAt(Vector2 p, float slack = 0f, float extend = 0f)
        {
            for (int i = 0; i < Lanes.Length; i++)
            {
                var l = Lanes[i];
                var d = l.B - l.A; float len2 = d.LengthSquared();
                float e = len2 < 1e-6f ? 0f : extend / MathF.Sqrt(len2);   // ⭐ CE-4655 — a lane runs on through the clearance ring at both ends
                float t = len2 < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(p - l.A, d) / len2, -e, 1f + e);
                float r = l.Width * 0.5f + slack;
                if (Vector2.DistanceSquared(l.A + d * t, p) <= r * r) return i;
            }
            return -1;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-T3 (MR-3) — every minefield of a world, gathered ON THE FRAME THREAD where the obstacles are gathered, and what the static-obstacle bake reads
    /// (<see cref="TerrainObstacles.With(TerrainWorld, System.Collections.Generic.IReadOnlyList{TerrainPrism}, BuildingDamageTable?, CraterTable?, FelledTrees?, MinefieldSet?)"/> lays it into
    /// <see cref="TerrainWorld.Minefields"/>). <see cref="SignatureOf"/> is the allocation-free per-frame hash the bake's change detection mixes in: a field added, moved, edited, laned — or a
    /// lane record gone in a seek — changes it and the touched tiles rebake.
    /// </summary>
    public sealed unsafe class MinefieldSet
    {
        public static readonly MinefieldSet Empty = new(Array.Empty<TerrainMinefield>());

        public IReadOnlyList<TerrainMinefield> Fields { get; }
        public int Count => Fields.Count;

        public MinefieldSet(IReadOnlyList<TerrainMinefield> fields) { Fields = fields; }

        private sealed class Queries
        {
            public EntityQuery? Fields, Lanes;
        }
        private static readonly ConditionalWeakTable<EntityRepository, Queries> Cache = new();

        private static Queries QueriesOf(EntityRepository world)
        {
            var q = Cache.GetOrCreateValue(world);
            q.Fields ??= world.Query().With<Minefield>().With<SimTransform>().Build();
            if (q.Lanes == null && world.IsComponentTypeRegistered<MinefieldLanes>()) q.Lanes = world.Query().With<MinefieldLanes>().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
            return q;
        }

        private static ulong Mix(ulong h, float f) { h ^= (ulong)(long)MathF.Round(f * 100f); return h * 1099511628211UL; }
        private static ulong Mix(ulong h, ulong v) { h ^= v; return h * 1099511628211UL; }

        /// <summary>
        /// The signature of every field's bake-relevant content NOW (0 when there are none). Allocation-free: cached queries, index loops, no strings. ⚠ Frame thread.
        /// </summary>
        public static ulong SignatureOf(EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<Minefield>()) return 0UL;
            var q = QueriesOf(world);
            ulong h = 0UL;
            int n = 0;
            foreach (var e in q.Fields!)
            {
                if (world.GetManagedComponentByTypeId(e, GlobalComponentIds.EditablePolyline) is not IAreaOutline o || o.Points.Count < 3) continue;
                n++;
                var f = world.GetComponentRO<Minefield>(e);
                var at = world.GetComponentRO<SimTransform>(e).Position;
                ulong fh = 1469598103934665603UL;
                fh = Mix(fh, at.X); fh = Mix(fh, at.Y); fh = Mix(fh, f.OrientationDeg); fh = Mix(fh, f.SpacingM);   // ⭐ CE-4655 — the clearance ring follows the spacing
                for (int i = 0; i < o.Points.Count; i++) { fh = Mix(fh, o.Points[i].X); fh = Mix(fh, o.Points[i].Y); }
                for (int i = 0; i < f.LaneCount && i < Minefield.MaxLanes; i++)
                {
                    var l = f.Lane(i);
                    fh = Mix(fh, l.A.X); fh = Mix(fh, l.A.Y); fh = Mix(fh, l.B.X); fh = Mix(fh, l.B.Y); fh = Mix(fh, l.Width); fh = Mix(fh, (ulong)l.ClearedBySidePlus1); fh = Mix(fh, (ulong)l.KnownToSides);
                }
                if (world.HasComponent<EntityGuid>(e)) { var g = world.GetComponentRO<EntityGuid>(e).Value; fh = Mix(fh, (ulong)g.GetHashCode()); }
                h ^= fh;   // order-free
            }
            if (n == 0) return 0UL;
            if (q.Lanes != null)
                foreach (var e in q.Lanes)
                {
                    var r = world.GetComponentRO<MinefieldLanes>(e);
                    ulong lh = 1099511628211UL;
                    lh = Mix(lh, (ulong)r.Field.GetHashCode());
                    for (int i = 0; i < r.Count && i < MinefieldLanes.Capacity; i++)
                    {
                        lh = Mix(lh, r.Ax[i]); lh = Mix(lh, r.Ay[i]); lh = Mix(lh, r.Bx[i]); lh = Mix(lh, r.By[i]); lh = Mix(lh, r.Width[i]); lh = Mix(lh, (ulong)r.SidePlus1[i]);
                    }
                    h ^= lh;
                }
            return h == 0UL ? 1UL : h;
        }

        /// <summary>
        /// Gathers the fields of <paramref name="world"/> — each field with an outline of ≥ 3 points and a stable id — with their authored lanes and every runtime lane record of any node (the union).
        /// ⚠ Frame thread; allocates (a bake is starting).
        /// </summary>
        public static MinefieldSet Of(EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<Minefield>()) return Empty;
            var q = QueriesOf(world);
            var list = new List<TerrainMinefield>();
            var outline = new List<Vector2>();
            var runtime = new List<LaneItem>();
            foreach (var e in q.Fields!)
            {
                if (!MinefieldIds.TryGuid(world, e, out var guid)) continue;
                if (!MinefieldKnowledgeOps.TryOutline(world, e, outline, out var anchor) || outline.Count < 3) continue;
                var f = world.GetComponentRO<Minefield>(e);
                var lanes = new List<MinefieldLane>();
                for (int i = 0; i < f.LaneCount && i < Minefield.MaxLanes; i++) if (f.Lane(i).Width > 0f) lanes.Add(f.Lane(i));
                runtime.Clear();
                if (q.Lanes != null) MinefieldLanesSystem.LanesOf(world, guid, runtime);
                foreach (var l in runtime) lanes.Add(new MinefieldLane(l.A, l.B, l.Width, l.SidePlus1, 0));
                var pts = outline.ToArray();
                var min = pts[0]; var max = pts[0];
                foreach (var p in pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
                ulong h = 1469598103934665603UL;
                h = Mix(h, f.OrientationDeg); h = Mix(h, anchor.X); h = Mix(h, anchor.Y); h = Mix(h, f.SpacingM);   // ⭐ CE-4655
                foreach (var p in pts) { h = Mix(h, p.X); h = Mix(h, p.Y); }
                foreach (var l in lanes) { h = Mix(h, l.A.X); h = Mix(h, l.A.Y); h = Mix(h, l.B.X); h = Mix(h, l.B.Y); h = Mix(h, l.Width); h = Mix(h, (ulong)l.ClearedBySidePlus1); h = Mix(h, (ulong)l.KnownToSides); }
                list.Add(new TerrainMinefield { Field = guid, Params = f, Anchor = anchor, Outline = pts, Lanes = lanes.ToArray(), Min = min, Max = max, Hash = h });
            }
            list.Sort(static (a, b) => a.Field.CompareTo(b.Field));
            return list.Count == 0 ? Empty : new MinefieldSet(list);
        }
    }
}
