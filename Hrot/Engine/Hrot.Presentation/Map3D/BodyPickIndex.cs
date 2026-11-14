using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis3D;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-4558 (docs/DESIGN_Map_3D_Mode.md §6ak D-6, G-4) — the boxes the body layer DREW in its last window frame (every band — a body too small to draw can still
/// be hovered through the pick's pixel margin — and every door, shown or hidden), and a uniform grid over them on the ground plane, built only in the frame
/// a pick needs it. A pick walks the cells under its ray, nearest first, and stops once a hit is nearer than the next cell — so it tests the few boxes
/// near the ray instead of every entity in the world (the old pick read every entity's components and boxes, every frame: 21 % of the street frame).
/// ⛔ No allocation in the steady state: every array is grown, never shrunk.
/// </summary>
public sealed class BodyPickIndex
{
    private struct Box
    {
        public Entity Entity;
        public int Net;
        public Vector3 Position, Centre, Half;
        public Quaternion Rotation;
    }

    private Box[] _boxes = new Box[256];
    private int _count;
    private int _frame, _builtFrame = -1;
    private float _builtMargin = float.NaN;

    // the grid: _cellStart[c] .. _cellStart[c + 1] index _cellItems
    private int[] _cellStart = new int[1];
    private int[] _cellItems = new int[256];
    private int[] _seen = new int[256];
    private int _query;
    private float _minX, _minY, _cell = 1f;
    private int _cols, _rows;
    private int[] _cursor = new int[1];

    /// <summary>Boxes recorded by the last frame.</summary>
    public int Count => _count;

    /// <summary>True once a window frame recorded its boxes (a host that never draws — a headless rail — scans instead).</summary>
    public bool HasFrame { get; private set; }

    /// <summary>Boxes the last <see cref="TryPick"/> tested (the counter of §6ak.5 ④).</summary>
    public int LastTested { get; private set; }

    public void Begin() => _count = 0;

    public void Add(Entity e, int net, Vector3 position, Vector3 centre, Quaternion rotation, Vector3 half)
    {
        if (_count == _boxes.Length) Array.Resize(ref _boxes, _boxes.Length * 2);
        ref var b = ref _boxes[_count++];
        b.Entity = e; b.Net = net; b.Position = position; b.Centre = centre; b.Rotation = rotation; b.Half = half;
    }

    public void End() { _frame++; HasFrame = true; }

    /// <summary>
    /// The nearest recorded box the ray hits within <paramref name="maxDistance"/>, each grown by <paramref name="marginAt"/> metres at its centre (U-9: a few
    /// screen pixels; null = none); <paramref name="skip"/> is never hit (the eye's own body).
    /// </summary>
    public bool TryPick(Vector3 origin, Vector3 direction, float maxDistance, Func<Vector3, float>? marginAt, Entity skip,
                        out Entity entity, out int net, out Vector3 position, out float distance)
    {
        entity = default; net = 0; position = default; distance = maxDistance;
        LastTested = 0;
        if (_count == 0) return false;
        // the margin of the farthest box bounds every box's reach on the grid (rebuilt when the camera moved enough to change it)
        float margin = MaxMargin(marginAt);
        if (_builtFrame != _frame || MathF.Abs(margin - _builtMargin) > MathF.Max(0.5f, _builtMargin * 0.25f)) Build(margin);
        if (++_query == int.MaxValue) { Array.Clear(_seen); _query = 1; }

        // the ray's ground track through the grid, cell by cell (Amanatides & Woo), from where it enters the grid to where it leaves or ends
        float dx = direction.X, dy = direction.Y;
        float t0 = 0f, t1 = maxDistance;
        if (!Clip(origin.X, dx, _minX, _minX + _cols * _cell, ref t0, ref t1) || !Clip(origin.Y, dy, _minY, _minY + _rows * _cell, ref t0, ref t1)) return false;
        float sx = origin.X + dx * t0, sy = origin.Y + dy * t0;
        int cx = Math.Clamp((int)((sx - _minX) / _cell), 0, _cols - 1), cy = Math.Clamp((int)((sy - _minY) / _cell), 0, _rows - 1);
        int stepX = dx > 0f ? 1 : -1, stepY = dy > 0f ? 1 : -1;
        float tDeltaX = MathF.Abs(dx) > 1e-9f ? _cell / MathF.Abs(dx) : float.PositiveInfinity;
        float tDeltaY = MathF.Abs(dy) > 1e-9f ? _cell / MathF.Abs(dy) : float.PositiveInfinity;
        float nextX = MathF.Abs(dx) > 1e-9f ? t0 + ((dx > 0f ? _minX + (cx + 1) * _cell - sx : sx - (_minX + cx * _cell)) / MathF.Abs(dx)) : float.PositiveInfinity;
        float nextY = MathF.Abs(dy) > 1e-9f ? t0 + ((dy > 0f ? _minY + (cy + 1) * _cell - sy : sy - (_minY + cy * _cell)) / MathF.Abs(dy)) : float.PositiveInfinity;
        float tCell = t0;
        bool hit = false;
        while (true)
        {
            // a box the ray hits at t covers the ray's ground point at t, so it sits in a cell already walked by then: a box first met in this cell is hit
            // no nearer than the cell's entry — once the best hit is nearer than that, nothing further can beat it
            if (hit && tCell > distance) break;
            int c = cy * _cols + cx;
            for (int k = _cellStart[c]; k < _cellStart[c + 1]; k++)
            {
                int i = _cellItems[k];
                if (_seen[i] == _query) continue;
                _seen[i] = _query;
                ref readonly var b = ref _boxes[i];
                if (b.Entity == skip) continue;
                LastTested++;
                float m = marginAt is null ? 0f : EntityBoxPicker.MarginPixels * marginAt(b.Centre);
                if (!RayBox.Intersect(origin, direction, b.Centre, b.Rotation, b.Half + new Vector3(m), out float t) || t > distance) continue;
                distance = t; entity = b.Entity; net = b.Net; position = b.Position; hit = true;
            }
            if (nextX < nextY) { if (nextX > t1) break; tCell = nextX; nextX += tDeltaX; cx += stepX; if (cx < 0 || cx >= _cols) break; }
            else { if (nextY > t1) break; tCell = nextY; nextY += tDeltaY; cy += stepY; if (cy < 0 || cy >= _rows) break; }
        }
        BodyDrawStats.RecordPick(LastTested);
        return hit;
    }


    private float MaxMargin(Func<Vector3, float>? marginAt)
    {
        if (marginAt is null) return 0f;
        float m = 0f;
        // the farthest boxes have the largest margin: sampling every box is O(n) cheap arithmetic, no component reads
        for (int i = 0; i < _count; i++) m = MathF.Max(m, EntityBoxPicker.MarginPixels * marginAt(_boxes[i].Centre));
        return m;
    }

    /// <summary>The grid over the recorded boxes: each box in every cell its ground circle (its half-diagonal + <paramref name="margin"/>) touches.</summary>
    private void Build(float margin)
    {
        _builtFrame = _frame;
        _builtMargin = margin;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, reach = 0f;
        for (int i = 0; i < _count; i++)
        {
            ref readonly var b = ref _boxes[i];
            float r = b.Half.Length() + margin;
            reach = MathF.Max(reach, r);
            minX = MathF.Min(minX, b.Centre.X - r); maxX = MathF.Max(maxX, b.Centre.X + r);
            minY = MathF.Min(minY, b.Centre.Y - r); maxY = MathF.Max(maxY, b.Centre.Y + r);
        }
        float extent = MathF.Max(maxX - minX, maxY - minY);
        _cell = Math.Clamp(MathF.Max(extent / 128f, reach), 4f, 4096f);   // ≤ 128 × 128 cells; a cell at least one box's reach wide
        _minX = minX; _minY = minY;
        _cols = Math.Max(1, (int)MathF.Ceiling((maxX - minX) / _cell));
        _rows = Math.Max(1, (int)MathF.Ceiling((maxY - minY) / _cell));
        int cells = _cols * _rows;
        if (_cellStart.Length < cells + 1) _cellStart = new int[Math.Max(cells + 1, _cellStart.Length * 2)];
        Array.Clear(_cellStart, 0, cells + 1);
        if (_seen.Length < _count) _seen = new int[Math.Max(_count, _seen.Length * 2)];
        // a counting sort: each cell's count, the prefix sums (cell c's items are _cellItems[_cellStart[c] .. _cellStart[c + 1])), then the fill
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < _count; i++)
            {
                ref readonly var b = ref _boxes[i];
                float r = b.Half.Length() + margin;
                int x0 = Math.Clamp((int)((b.Centre.X - r - _minX) / _cell), 0, _cols - 1), x1 = Math.Clamp((int)((b.Centre.X + r - _minX) / _cell), 0, _cols - 1);
                int y0 = Math.Clamp((int)((b.Centre.Y - r - _minY) / _cell), 0, _rows - 1), y1 = Math.Clamp((int)((b.Centre.Y + r - _minY) / _cell), 0, _rows - 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int c = y * _cols + x;
                        if (pass == 0) _cellStart[c + 1]++;
                        else _cellItems[_cursor[c]++] = i;
                    }
            }
            if (pass == 0)
            {
                for (int c = 0; c < cells; c++) _cellStart[c + 1] += _cellStart[c];
                int total = _cellStart[cells];
                if (_cellItems.Length < total) _cellItems = new int[Math.Max(total, _cellItems.Length * 2)];
                if (_cursor.Length < cells) _cursor = new int[Math.Max(cells, _cursor.Length * 2)];
                Array.Copy(_cellStart, _cursor, cells);
            }
        }
    }

    /// <summary>Clips the ray parameter range to [lo, hi] on one axis (slab test); false when the ray misses it.</summary>
    private static bool Clip(float o, float d, float lo, float hi, ref float t0, ref float t1)
    {
        if (MathF.Abs(d) < 1e-9f) return o >= lo && o <= hi;
        float a = (lo - o) / d, b = (hi - o) / d;
        if (a > b) (a, b) = (b, a);
        t0 = MathF.Max(t0, a); t1 = MathF.Min(t1, b);
        return t0 <= t1;
    }
}
