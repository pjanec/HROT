using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-4391</c> S-1 (UTM §8.2 B-5) — a lazy, immutable, content-derived grid of the wall panels that belong to a BUILDING, so a detonation
    /// asks for "the panels within R of the burst" without scanning the 100 000 panels of a large terrain. Built once on first use
    /// (<see cref="TerrainWorld.BuildingPanels"/>) — the same lifetime and thread-safety as the woods grid — and never changes: a breach is live
    /// state held elsewhere (<see cref="BuildingDamageTable"/>), the panels themselves never move.
    /// </summary>
    /// <summary>⭐ <c>CE-4391</c> S-2 — the same lazy grid over BUILDING FOOTPRINT boxes (<see cref="TerrainWorld.BuildingFootprints"/>): "the buildings near this burst",
    /// for the collapse test (a bomb in the middle of a large hall is far from every panel).</summary>
    public sealed class BuildingFootprintGrid
    {
        private const float Cell = 32f;
        private readonly Dictionary<long, int[]> _cells;
        private BuildingFootprintGrid(Dictionary<long, int[]> cells) => _cells = cells;

        public static BuildingFootprintGrid Build(TerrainWorld world)
        {
            var lists = new Dictionary<long, List<int>>();
            for (int i = 0; i < world.Buildings.Count; i++)
            {
                var fp = world.Buildings[i].Footprint;
                if (fp.Length < 3) continue;
                var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
                foreach (var v in fp) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
                for (int cy = C(min.Y); cy <= C(max.Y); cy++)
                    for (int cx = C(min.X); cx <= C(max.X); cx++)
                    {
                        long k = ((long)cx << 32) ^ (uint)cy;
                        if (!lists.TryGetValue(k, out var l)) lists[k] = l = new List<int>();
                        l.Add(i);
                    }
            }
            var cells = new Dictionary<long, int[]>(lists.Count);
            foreach (var (k, l) in lists) cells[k] = l.ToArray();
            return new BuildingFootprintGrid(cells);
        }

        /// <summary>Appends (ascending, no duplicates) every building whose box cells touch the square of half-side <paramref name="radius"/> around <paramref name="at"/>.</summary>
        public void Near(Vector2 at, float radius, List<int> into)
        {
            int start = into.Count;
            for (int cy = C(at.Y - radius); cy <= C(at.Y + radius); cy++)
                for (int cx = C(at.X - radius); cx <= C(at.X + radius); cx++)
                    if (_cells.TryGetValue(((long)cx << 32) ^ (uint)cy, out var a)) into.AddRange(a);
            if (into.Count - start > 1)
            {
                into.Sort(start, into.Count - start, null);
                int w = start;
                for (int r = start; r < into.Count; r++) if (r == start || into[r] != into[r - 1]) into[w++] = into[r];
                into.RemoveRange(w, into.Count - w);
            }
        }
        private static int C(float v) => (int)MathF.Floor(v / Cell);
    }

    public sealed class BuildingPanelGrid
    {
        /// <summary>Cell side, metres: a house is a few cells; a burst's reach (≈ 10 m) a few more.</summary>
        public const float CellSize = 16f;

        private readonly Dictionary<long, int[]> _cells;

        private BuildingPanelGrid(Dictionary<long, int[]> cells, int panels) { _cells = cells; PanelCount = panels; }

        /// <summary>Panels indexed (those with <see cref="TerrainWallPanel.Building"/> ≥ 0).</summary>
        public int PanelCount { get; }

        public static BuildingPanelGrid Build(TerrainWorld world)
        {
            var lists = new Dictionary<long, List<int>>();
            int count = 0;
            for (int i = 0; i < world.Panels.Count; i++)
            {
                var p = world.Panels[i];
                if (p.Building < 0) continue;
                count++;
                float pad = p.Thickness * 0.5f;
                var min = Vector2.Min(p.A, p.B) - new Vector2(pad); var max = Vector2.Max(p.A, p.B) + new Vector2(pad);
                int x0 = Cell(min.X), x1 = Cell(max.X), y0 = Cell(min.Y), y1 = Cell(max.Y);
                for (int cy = y0; cy <= y1; cy++)
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        long key = Key(cx, cy);
                        if (!lists.TryGetValue(key, out var l)) lists[key] = l = new List<int>();
                        l.Add(i);
                    }
            }
            var cells = new Dictionary<long, int[]>(lists.Count);
            foreach (var (k, l) in lists) cells[k] = l.ToArray();
            return new BuildingPanelGrid(cells, count);
        }

        /// <summary>
        /// Appends to <paramref name="into"/> (ascending, no duplicates) every indexed panel whose cells touch the square of half-side
        /// <paramref name="radius"/> around <paramref name="at"/> — a superset of the panels within the radius.
        /// </summary>
        public void Near(Vector2 at, float radius, List<int> into)
        {
            int start = into.Count;
            int x0 = Cell(at.X - radius), x1 = Cell(at.X + radius), y0 = Cell(at.Y - radius), y1 = Cell(at.Y + radius);
            for (int cy = y0; cy <= y1; cy++)
                for (int cx = x0; cx <= x1; cx++)
                    if (_cells.TryGetValue(Key(cx, cy), out var a)) into.AddRange(a);
            if (into.Count - start > 1)
            {
                into.Sort(start, into.Count - start, null);
                int w = start;
                for (int r = start; r < into.Count; r++)
                    if (r == start || into[r] != into[r - 1]) into[w++] = into[r];
                into.RemoveRange(w, into.Count - w);
            }
        }

        private static int Cell(float v) => (int)MathF.Floor(v / CellSize);
        private static long Key(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;
    }
}
