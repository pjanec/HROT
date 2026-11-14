namespace Hrot.Terrain.Import;

/// <summary>
/// ⭐ I-f: an edge whose two vertices match another building's edge (identical, or ≤ 0.05 m) is ONE wall — written by the building that comes
/// first by id (ordinal), skipped by the others, so the expander never builds two walls 0 m apart.
/// </summary>
internal sealed class PartyWalls
{
    public const double Tolerance = 0.05;
    private readonly Dictionary<(long, long), List<int>> _cells = new();
    private readonly List<(double X, double Y)> _vertices = new();
    private readonly Dictionary<(int, int), List<(string Id, int Edge)>> _edges = new();
    private readonly Dictionary<string, int[]> _vertexIds = new();

    public int SharedEdges { get; private set; }

    public PartyWalls(IEnumerable<(string Id, IReadOnlyList<(double X, double Y)> Ring)> buildings)
    {
        foreach (var (id, ring) in buildings)
        {
            int n = ring.Count;
            var vids = new int[n];
            for (int i = 0; i < n; i++) vids[i] = Vertex(ring[i].X, ring[i].Y);
            _vertexIds[id] = vids;
            for (int e = 0; e < n; e++)
            {
                int a = vids[e], b = vids[(e + 1) % n];
                if (a == b) continue;
                var key = a < b ? (a, b) : (b, a);
                if (!_edges.TryGetValue(key, out var l)) _edges[key] = l = new();
                l.Add((id, e));
            }
        }
        foreach (var l in _edges.Values) if (l.Select(x => x.Id).Distinct().Count() > 1) SharedEdges++;
    }

    private int Vertex(double x, double y)
    {
        long cx = (long)Math.Floor(x / 0.1), cy = (long)Math.Floor(y / 0.1);
        for (long i = cx - 1; i <= cx + 1; i++)
            for (long j = cy - 1; j <= cy + 1; j++)
                if (_cells.TryGetValue((i, j), out var l))
                    foreach (var v in l)
                        if (Math.Abs(_vertices[v].X - x) <= Tolerance && Math.Abs(_vertices[v].Y - y) <= Tolerance) return v;
        _vertices.Add((x, y));
        if (!_cells.TryGetValue((cx, cy), out var cell)) _cells[(cx, cy)] = cell = new();
        cell.Add(_vertices.Count - 1);
        return _vertices.Count - 1;
    }

    private List<(string Id, int Edge)>? Group(string id, int edge)
    {
        var v = _vertexIds[id];
        int a = v[edge], b = v[(edge + 1) % v.Length];
        if (a == b) return null;
        return _edges.TryGetValue(a < b ? (a, b) : (b, a), out var l) ? l : null;
    }

    /// <summary>True when another building has the same edge.</summary>
    public bool IsShared(string id, int edge) => Group(id, edge) is { } l && l.Any(x => x.Id != id);

    /// <summary>The building that writes this edge's wall: the first by id among those holding it.</summary>
    public bool Writes(string id, int edge)
    {
        var l = Group(id, edge);
        if (l == null) return true;
        string owner = l.Select(x => x.Id).Min(StringComparer.Ordinal)!;
        return owner == id;
    }
}
