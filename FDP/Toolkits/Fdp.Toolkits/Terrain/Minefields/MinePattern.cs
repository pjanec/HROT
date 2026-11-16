using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>One derived mine: its lattice index (stable — MR-1), where it lies, and what it is.</summary>
    public readonly record struct Mine(short Row, short Col, Vector2 At, MineType Type);

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T1 (DESIGN_Minefields.md MF-2; §2 row mining) — a field's mines, DERIVED, never stored: rows <see cref="Minefield.SpacingM"/> apart running
    /// along <see cref="Minefield.OrientationDeg"/>, mines the same spacing apart along each row, every odd row offset half a spacing (STAGGERED), clipped to the
    /// outline. ⭐ The lattice is anchored at the field ENTITY's position (not the outline's bounds), so a (row, col) index survives an outline edit; the same
    /// points on every node. A mixed field alternates rows: anti-personnel on even rows, anti-tank on odd (lane decision R-5042). A seed ≠ 0 adds a small jitter
    /// (≤ 15 % of the spacing, hashed from seed, row and col). Mines inside a LANE are not listed (cleared by geometry, MF-10); struck / cleared mines (the
    /// field's <see cref="MinefieldStrikes"/>) are the caller's filter.
    /// </summary>
    public static class MinePattern
    {
        /// <summary>The most mines one field may derive (a guard against a huge field at a tiny spacing).</summary>
        public const int MaxMines = 200_000;

        /// <summary>The field frame: (u along the rows, v across them) of a world point relative to the anchor.</summary>
        public static Vector2 ToField(Vector2 world, Vector2 anchor, float orientationDeg)
        {
            float a = orientationDeg * (MathF.PI / 180f), c = MathF.Cos(a), s = MathF.Sin(a);
            var d = world - anchor;
            return new Vector2(d.X * c + d.Y * s, -d.X * s + d.Y * c);
        }

        /// <summary>The world point of field-frame (u, v).</summary>
        public static Vector2 ToWorld(Vector2 uv, Vector2 anchor, float orientationDeg)
        {
            float a = orientationDeg * (MathF.PI / 180f), c = MathF.Cos(a), s = MathF.Sin(a);
            return anchor + new Vector2(uv.X * c - uv.Y * s, uv.X * s + uv.Y * c);
        }

        /// <summary>The field-frame point of lattice (<paramref name="row"/>, <paramref name="col"/>) before jitter.</summary>
        public static Vector2 LatticeUv(int row, int col, float spacing) => new((col + ((row & 1) != 0 ? 0.5f : 0f)) * spacing, row * spacing);

        /// <summary>The mine type at <paramref name="row"/> of a field of <paramref name="type"/>.</summary>
        public static MineType TypeAt(MineType type, int row) => type == MineType.Mixed ? ((row & 1) == 0 ? MineType.AntiPersonnel : MineType.AntiTank) : type;

        /// <summary>The world point of mine (<paramref name="row"/>, <paramref name="col"/>) of <paramref name="f"/> anchored at <paramref name="anchor"/>.</summary>
        public static Vector2 At(in Minefield f, Vector2 anchor, int row, int col)
        {
            var uv = LatticeUv(row, col, f.SpacingM);
            if (f.Seed != 0)
            {
                uint h = Hash(f.Seed, row, col);
                float j = 0.15f * f.SpacingM;
                uv += new Vector2(((h & 0xFFFF) / 65535f - 0.5f) * 2f * j, (((h >> 16) & 0xFFFF) / 65535f - 0.5f) * 2f * j);
            }
            return ToWorld(uv, anchor, f.OrientationDeg);
        }

        /// <summary>
        /// Appends every live-by-geometry mine of <paramref name="f"/> (outline <paramref name="outline"/>, world points; anchored at <paramref name="anchor"/>) to
        /// <paramref name="into"/>; returns how many. Cold (a field's cache is built once per change).
        /// </summary>
        public static int MinesIn(in Minefield f, Vector2 anchor, IReadOnlyList<Vector2> outline, List<Mine> into)
        {
            if (outline.Count < 3 || !(f.SpacingM > 0.1f)) return 0;
            float minU = float.MaxValue, minV = float.MaxValue, maxU = float.MinValue, maxV = float.MinValue;
            for (int i = 0; i < outline.Count; i++)
            {
                var uv = ToField(outline[i], anchor, f.OrientationDeg);
                minU = MathF.Min(minU, uv.X); maxU = MathF.Max(maxU, uv.X); minV = MathF.Min(minV, uv.Y); maxV = MathF.Max(maxV, uv.Y);
            }
            int r0 = (int)MathF.Floor(minV / f.SpacingM) - 1, r1 = (int)MathF.Ceiling(maxV / f.SpacingM) + 1;
            int c0 = (int)MathF.Floor(minU / f.SpacingM) - 1, c1 = (int)MathF.Ceiling(maxU / f.SpacingM) + 1;
            int n = 0;
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    if (r < short.MinValue || r > short.MaxValue || c < short.MinValue || c > short.MaxValue) continue;
                    var p = At(f, anchor, r, c);
                    if (!Inside(outline, p) || InLane(f, p)) continue;
                    into.Add(new Mine((short)r, (short)c, p, TypeAt(f.Type, r)));
                    if (++n >= MaxMines) return n;
                }
            return n;
        }

        /// <summary>True when <paramref name="p"/> lies in one of <paramref name="f"/>'s lanes.</summary>
        public static unsafe bool InLane(in Minefield f, Vector2 p)
        {
            for (int i = 0; i < f.LaneCount && i < Minefield.MaxLanes; i++)
            {
                var l = f.Lane(i);
                var d = l.B - l.A; float len2 = d.LengthSquared();
                float t = len2 < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(p - l.A, d) / len2, 0f, 1f);
                if (Vector2.DistanceSquared(l.A + d * t, p) <= l.Width * l.Width * 0.25f) return true;
            }
            return false;
        }

        /// <summary>Point in polygon (even-odd).</summary>
        public static bool Inside(IReadOnlyList<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static uint Hash(uint seed, int row, int col)
        {
            uint h = seed * 0x9E3779B1u ^ (uint)row * 0x85EBCA77u ^ (uint)col * 0xC2B2AE3Du;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return h;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4624</c> (§6.1) — where a field's knowledge GRID lies: the outline's bounds in the field frame, cells of at least <see cref="MinCell"/> m, coarsened so the
    /// grid fits <see cref="MinefieldKnowledge.MaxCols"/> × <see cref="MinefieldKnowledge.MaxRows"/> (MF-14).
    /// </summary>
    public static class MinefieldKnowledgeOps
    {
        public const float MinCell = 10f;

        /// <summary>The grid of a field: its field-frame origin, cell size, columns and rows.</summary>
        public readonly record struct KnowledgeGrid(Vector2 OriginUv, float Cell, int Cols, int Rows, Vector2 Anchor, float OrientationDeg);

        public static KnowledgeGrid Grid(in Minefield f, Vector2 anchor, IReadOnlyList<Vector2> outline)
        {
            float minU = float.MaxValue, minV = float.MaxValue, maxU = float.MinValue, maxV = float.MinValue;
            for (int i = 0; i < outline.Count; i++)
            {
                var uv = MinePattern.ToField(outline[i], anchor, f.OrientationDeg);
                minU = MathF.Min(minU, uv.X); maxU = MathF.Max(maxU, uv.X); minV = MathF.Min(minV, uv.Y); maxV = MathF.Max(maxV, uv.Y);
            }
            if (outline.Count == 0) { minU = minV = maxU = maxV = 0f; }
            float w = MathF.Max(1f, maxU - minU), h = MathF.Max(1f, maxV - minV);
            float cell = MathF.Max(MinCell, MathF.Max(w / MinefieldKnowledge.MaxCols, h / MinefieldKnowledge.MaxRows));
            int cols = Math.Clamp((int)MathF.Ceiling(w / cell), 1, MinefieldKnowledge.MaxCols), rows = Math.Clamp((int)MathF.Ceiling(h / cell), 1, MinefieldKnowledge.MaxRows);
            return new KnowledgeGrid(new Vector2(minU, minV), cell, cols, rows, anchor, f.OrientationDeg);
        }

        /// <summary>The knowledge cell of world point <paramref name="p"/>, or false when it lies outside the grid.</summary>
        public static bool CellOf(in KnowledgeGrid g, Vector2 p, out int col, out int row)
        {
            var uv = MinePattern.ToField(p, g.Anchor, g.OrientationDeg) - g.OriginUv;
            col = (int)MathF.Floor(uv.X / g.Cell); row = (int)MathF.Floor(uv.Y / g.Cell);
            return (uint)col < (uint)g.Cols && (uint)row < (uint)g.Rows;
        }

        /// <summary>The world centre of cell (<paramref name="col"/>, <paramref name="row"/>).</summary>
        public static Vector2 CentreOf(in KnowledgeGrid g, int col, int row)
            => MinePattern.ToWorld(g.OriginUv + new Vector2((col + 0.5f) * g.Cell, (row + 0.5f) * g.Cell), g.Anchor, g.OrientationDeg);

        /// <summary>The outline of <paramref name="field"/> (its ONE <c>EditablePolyline</c>, world points), or false — the toolkit reads it through <see cref="IAreaOutline"/>.</summary>
        public static bool TryOutline(EntityRepository world, Entity field, List<Vector2> into, out Vector2 anchor)
        {
            into.Clear(); anchor = default;
            if (!world.IsAlive(field) || !world.HasComponent<SimTransform>(field)) return false;
            if (world.GetManagedComponentByTypeId(field, GlobalComponentIds.EditablePolyline) is not IAreaOutline o || o.Points.Count < 3) return false;
            var at = world.GetComponentRO<SimTransform>(field).Position;
            anchor = new Vector2(at.X, at.Y);
            for (int i = 0; i < o.Points.Count; i++) into.Add(anchor + o.Points[i]);
            return true;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4624</c> (§6.1, KR-5, KR-8) — the UNION of every side's place knowledge, per view: <see cref="Knows"/> is what a side knows of a field's cell (the
    /// lattice max over every deciding node's record). ⚠ CONTRACT STUB until MS-T2: it answers <see cref="MineCellKnowledge.Unknown"/>; MS-T2 makes it ride on
    /// <c>DoorStates</c> like <c>Craters</c> (0 B a steady frame).
    /// </summary>
    public sealed class PlaceKnowledgeTable
    {
        public static readonly PlaceKnowledgeTable Empty = new();

        /// <summary>The table of <paramref name="view"/>'s world.</summary>
        public static PlaceKnowledgeTable Of(ISimulationView view) => Empty;

        /// <summary>What side <paramref name="side"/> (<c>ForceId</c>) knows of cell (<paramref name="col"/>, <paramref name="row"/>) of <paramref name="field"/>.</summary>
        public MineCellKnowledge Knows(int side, Entity field, int col, int row) => MineCellKnowledge.Unknown;

        /// <summary>True when <paramref name="side"/> has marked <paramref name="field"/> (known in full).</summary>
        public bool Marked(int side, Entity field) => false;
    }
}
