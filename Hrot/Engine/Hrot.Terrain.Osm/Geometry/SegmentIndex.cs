namespace Hrot.Terrain.Osm;

/// <summary>A uniform-grid index of line segments for nearest-segment queries — the road-facing door and the road levelling both use it.</summary>
public sealed class SegmentIndex
{
    public readonly record struct Hit(double Distance, double X, double Y, int Tag);

    private readonly double _cell;
    private readonly Dictionary<(int, int), List<int>> _cells = new();
    private readonly List<(double X1, double Y1, double X2, double Y2, int Tag)> _segs = new();

    public SegmentIndex(double cellSize) => _cell = cellSize;
    public int Count => _segs.Count;

    public void Add(double x1, double y1, double x2, double y2, int tag)
    {
        int id = _segs.Count;
        _segs.Add((x1, y1, x2, y2, tag));
        int cx0 = Cell(Math.Min(x1, x2)), cx1 = Cell(Math.Max(x1, x2)), cy0 = Cell(Math.Min(y1, y2)), cy1 = Cell(Math.Max(y1, y2));
        for (int cx = cx0; cx <= cx1; cx++)
            for (int cy = cy0; cy <= cy1; cy++)
            {
                if (!_cells.TryGetValue((cx, cy), out var l)) _cells[(cx, cy)] = l = new List<int>();
                l.Add(id);
            }
    }

    private int Cell(double v) => (int)Math.Floor(v / _cell);

    /// <summary>The nearest segment within <paramref name="maxDistance"/>, or null.</summary>
    public Hit? Nearest(double x, double y, double maxDistance)
    {
        int rings = (int)Math.Ceiling(maxDistance / _cell);
        int cx = Cell(x), cy = Cell(y);
        Hit? best = null;
        for (int r = 0; r <= rings; r++)
        {
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    if (!_cells.TryGetValue((cx + dx, cy + dy), out var l)) continue;
                    foreach (var id in l)
                    {
                        var s = _segs[id];
                        var h = Closest(s, x, y);
                        if (h.Distance <= maxDistance && (best == null || h.Distance < best.Value.Distance)) best = h;
                    }
                }
            if (best != null && best.Value.Distance <= r * _cell) break;
        }
        return best;
    }

    /// <summary>The nearest point of the segment <paramref name="s"/> to (x, y).</summary>
    public static Hit Closest((double X1, double Y1, double X2, double Y2, int Tag) s, double x, double y)
    {
        double dx = s.X2 - s.X1, dy = s.Y2 - s.Y1, l2 = dx * dx + dy * dy;
        double t = l2 < 1e-12 ? 0 : Math.Clamp(((x - s.X1) * dx + (y - s.Y1) * dy) / l2, 0, 1);
        double px = s.X1 + t * dx, py = s.Y1 + t * dy;
        return new Hit(Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py)), px, py, s.Tag);
    }
}
