using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Perception.LineOfSight
{
    /// <summary>
    /// ⭐⭐ <b>CE-3032 — the colliders a sight line can hit, without walking every collider in the world.</b>
    /// <para>🔴 Why: every <see cref="ILosStrategy.IsVisible"/> used to test the segment against EVERY collider, so a
    /// perception tick cost observers × candidates × colliders — CUBIC in the unit count. Measured (probe,
    /// <c>2026-10-04</c>): the tick crossed the module's 100 ms limit long before a realistic unit count, the circuit
    /// breaker opened and perception went dark for 10 s at a time.</para>
    /// <para>⭐ A uniform grid built once per batch: each collider is registered in every cell its (slightly inflated)
    /// bounding square overlaps; a query walks the cells the segment passes through (Amanatides–Woo) and returns
    /// each collider at most once. ⭐ It only NARROWS the candidates — the caller keeps its exact geometric test, so
    /// the answer is the brute-force answer (pinned by a parity rail). Deterministic: no clocks, no hashing order
    /// reaches the result (any blocker ⇒ blocked).</para>
    /// </summary>
    internal sealed class ColliderIndex
    {
        /// <summary>Cell edge in metres — a few soldier / vehicle radii, so a sight line touches few colliders per cell.</summary>
        public const float CellSize = 16f;

        // Registration inflation: a segment that grazes a circle exactly on a cell boundary still finds it. Only
        // ADDS candidates; the exact test decides.
        private const float Inflate = 0.5f;

        private readonly Dictionary<long, List<int>> _cells = new();
        private readonly Stack<List<int>> _pool = new();
        private int[] _stamp = Array.Empty<int>();
        private int _queryId;

        /// <summary>Rebuilds the index for <paramref name="count"/> colliders given by centre and radius.</summary>
        public void Build(int count, Func<int, Vector2> centre, Func<int, float> radius)
        {
            foreach (var list in _cells.Values) { list.Clear(); _pool.Push(list); }
            _cells.Clear();
            if (_stamp.Length < count) _stamp = new int[Math.Max(count, _stamp.Length * 2)];
            Array.Clear(_stamp, 0, count);
            _queryId = 0;

            for (int i = 0; i < count; i++)
            {
                var c = centre(i);
                float r = MathF.Max(0f, radius(i)) + Inflate;
                int x0 = Cell(c.X - r), x1 = Cell(c.X + r);
                int y0 = Cell(c.Y - r), y1 = Cell(c.Y + r);
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    {
                        long key = Key(x, y);
                        if (!_cells.TryGetValue(key, out var list))
                        {
                            list = _pool.Count > 0 ? _pool.Pop() : new List<int>(4);
                            _cells[key] = list;
                        }
                        list.Add(i);
                    }
            }
        }

        /// <summary>Fills <paramref name="result"/> with every collider whose cells the segment a→b passes through
        /// (each once, ascending registration order within a cell).</summary>
        public void Query(Vector2 a, Vector2 b, List<int> result)
        {
            result.Clear();
            if (_cells.Count == 0) return;
            if (++_queryId == int.MaxValue) { Array.Clear(_stamp, 0, _stamp.Length); _queryId = 1; }

            int cx = Cell(a.X), cy = Cell(a.Y);
            int ex = Cell(b.X), ey = Cell(b.Y);
            Visit(cx, cy, result);
            if (cx == ex && cy == ey) return;

            float dx = b.X - a.X, dy = b.Y - a.Y;
            int stepX = dx > 0 ? 1 : dx < 0 ? -1 : 0;
            int stepY = dy > 0 ? 1 : dy < 0 ? -1 : 0;
            float tDeltaX = stepX != 0 ? CellSize / MathF.Abs(dx) : float.PositiveInfinity;
            float tDeltaY = stepY != 0 ? CellSize / MathF.Abs(dy) : float.PositiveInfinity;
            float nextBoundX = (stepX > 0 ? (cx + 1) : cx) * CellSize;
            float nextBoundY = (stepY > 0 ? (cy + 1) : cy) * CellSize;
            float tMaxX = stepX != 0 ? (nextBoundX - a.X) / dx : float.PositiveInfinity;
            float tMaxY = stepY != 0 ? (nextBoundY - a.Y) / dy : float.PositiveInfinity;

            // Bounded walk: never more cells than the Manhattan distance between the end cells (+ slack).
            int guard = Math.Abs(ex - cx) + Math.Abs(ey - cy) + 2;
            while (guard-- > 0 && (cx != ex || cy != ey))
            {
                if (tMaxX < tMaxY) { cx += stepX; tMaxX += tDeltaX; }
                else if (tMaxY < tMaxX) { cy += stepY; tMaxY += tDeltaY; }
                else
                {
                    // Exactly through a corner: visit both side cells too, so a corner graze is not skipped.
                    Visit(cx + stepX, cy, result);
                    Visit(cx, cy + stepY, result);
                    cx += stepX; cy += stepY; tMaxX += tDeltaX; tMaxY += tDeltaY;
                }
                Visit(cx, cy, result);
            }
            // Float drift on a very long segment: make sure the end cell is in.
            Visit(ex, ey, result);
        }

        private void Visit(int x, int y, List<int> result)
        {
            if (!_cells.TryGetValue(Key(x, y), out var list)) return;
            foreach (int i in list)
            {
                if (_stamp[i] == _queryId) continue;
                _stamp[i] = _queryId;
                result.Add(i);
            }
        }

        private static int Cell(float v) => (int)MathF.Floor(v / CellSize);
        private static long Key(int x, int y) => ((long)x << 32) | (uint)y;
    }
}
