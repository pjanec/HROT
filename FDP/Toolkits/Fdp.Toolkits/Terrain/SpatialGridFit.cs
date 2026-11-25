using System;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>A spatial grid's placement: bottom-left origin and cell edge length (metres).</summary>
    public readonly record struct GridGeometry(float OriginX, float OriginY, float CellSize);

    /// <summary>
    /// ⭐ <b>W9 / CE-3018 — where a fixed-count spatial grid sits so it covers the terrain</b>
    /// (docs/DESIGN_Terrain_World.md §4.4).
    /// <para>⭐ Coverage is the grid's default extent, widened on each side the terrain leaves (by a margin), so a terrain
    /// that already fits changes nothing (same origin, same cell) and coverage never shrinks below today's. ⚠ A larger terrain
    /// coarsens the cells (cell count is fixed): queries cost more, they are never wrong.</para>
    /// </summary>
    public static class SpatialGridFit
    {
        /// <summary>
        /// Metres added around the terrain bounds — an entity at the very edge still has a full cell around it. ⭐ CE-4694 ① — 250 m (was 50): what
        /// legitimately GRAZES a terrain's edge is not authored — SUMO's traffic at the ends of its net and aircraft turning at an air arena's rim.
        /// 📐 Measured on stechovice-battle: 14 bodies 18–198 m past the fitted edge, each "seen by nobody". A wider margin coarsens the cells a little.
        /// </summary>
        public const float Margin = 250f;

        /// <param name="defaultOrigin">The grid's composition-time origin.</param>
        /// <param name="width">Cell count along X (fixed).</param>
        /// <param name="height">Cell count along Y (fixed).</param>
        /// <param name="baseCellSize">The composition-time cell size — never made finer.</param>
        /// <param name="world">The resident terrain; null ⇒ the default placement.</param>
        public static GridGeometry For(Vector2 defaultOrigin, int width, int height, float baseCellSize, TerrainWorld? world)
        {
            var dflt = new GridGeometry(defaultOrigin.X, defaultOrigin.Y, baseCellSize);
            // ⚠ A terrain with no world file publishes an EMPTY world (zero bounds) — that is "no terrain", not a 0×0 one.
            if (world == null || !Covered(world, out var lo, out var hi))
                return dflt;

            // ⭐ Extend ONLY the sides the terrain leaves, each by the margin — a terrain inside the default extent
            //    changes nothing at all.
            var defaultMax = defaultOrigin + new Vector2(width * baseCellSize, height * baseCellSize);
            var min = defaultOrigin;
            var max = defaultMax;
            if (lo.X < min.X) min.X = lo.X - Margin;
            if (lo.Y < min.Y) min.Y = lo.Y - Margin;
            if (hi.X > max.X) max.X = hi.X + Margin;
            if (hi.Y > max.Y) max.Y = hi.Y + Margin;
            if (min == defaultOrigin && max == defaultMax) return dflt;

            var span = max - min;
            float cell = MathF.Max(baseCellSize, MathF.Max(span.X / width, span.Y / height));
            // ⚠ A cell size computed as span/count can land a hair short of max through float rounding; nudge it up 0.01%
            //   so the far edge maps inside the last cell.
            cell *= 1.0001f;
            return new GridGeometry(min.X, min.Y, cell);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-1959</c> (the user, `2026-11-17`: "SUMO cars ignore hits") — what the grids must COVER: the terrain's bounds AND the footprint of the
        /// ground attached beyond them (<see cref="TerrainWorld.Beyond"/> — the SUMO net, R-4021: its roads, its traffic and the units on them live there).
        /// ⛔ Was the bounds alone: on demo-city (bounds 800 m, its net ~4 km) every body on the net past the bounds was outside the collider grid ⇒ no
        /// round could meet it (a possessed IFV's and a rifleman's rounds alike — 📐 measured live, 90 of 90 "StoppedByTerrain"), nor avoidance or sight.
        /// False for an empty world.
        /// </summary>
        public static bool Covered(TerrainWorld world, out Vector2 min, out Vector2 max)
        {
            min = world.BoundsMin; max = world.BoundsMax;
            if (!(max.X > min.X && max.Y > min.Y)) return false;
            if (world.Beyond is { } beyond && beyond.FieldQuad.Length > 0)
            {
                min = Vector2.Min(min, beyond.FieldMin);
                max = Vector2.Max(max, beyond.FieldMax);
            }
            return true;
        }
    }

    /// <summary>
    /// ⭐ The ONE live read of the resident terrain for code that runs on a background view with no singletons — the LOS
    /// strategy and the perception grid builder (CE-3018, R-174: one source, not two lambdas).
    /// </summary>
    public static class TerrainWorldSource
    {
        /// <summary>A reader of <paramref name="world"/>'s <see cref="TerrainWorld"/> singleton (null when none is resident).
        /// ⭐ Safe off the main thread: the singleton is an immutable object swapped by reference at a terrain commit.</summary>
        public static Func<TerrainWorld?> Live(EntityRepository world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            return () => world.HasSingletonManaged<TerrainWorld>() ? world.GetSingletonManaged<TerrainWorld>() : null;
        }

        // ⭐⭐ CE-2244 — held BESIDE the world (as RecentShots): a load-time fact of this node, never saved, recorded or replicated.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, object> s_pending = new();
        private static readonly object s_marker = new();

        /// <summary>
        /// ⭐⭐ <c>CE-2244</c> — marks that a terrain is being loaded into <paramref name="world"/> and is not resident yet (the terrain load
        /// step, from its frame-thread <c>BeforePrepare</c> to its commit or abort). 📌 A node's commit is not a cluster barrier (CE-2242):
        /// a faster node's units replicate in while this one still prepares (SimHost bakes its navmesh), and sight judged with NO terrain
        /// saw through every wall — a man behind a tower was acquired and remembered (measured: ua-threat-ranking, frame 16).
        /// </summary>
        public static void MarkPending(EntityRepository world, bool pending)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (pending) s_pending.AddOrUpdate(world, s_marker);
            else s_pending.Remove(world);
        }

        /// <summary>⭐ <c>CE-2244</c> — is a terrain still coming into <paramref name="world"/>? (Sight is not judged until it is in.)</summary>
        public static bool IsPending(EntityRepository world) => world != null && s_pending.TryGetValue(world, out _);

        /// <summary>A reader of <see cref="IsPending"/> for code that runs on a background view (the LOS strategy).</summary>
        public static Func<bool> Pending(EntityRepository world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            return () => IsPending(world);
        }
    }
}
