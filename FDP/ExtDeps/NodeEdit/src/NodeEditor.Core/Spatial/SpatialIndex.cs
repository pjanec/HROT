using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Spatial;

/// <summary>
/// Simple uniform-grid spatial index for hit-testing and viewport culling.
/// Cells indexed by integer coordinates. Each node is registered with
/// its bounding rect; cells containing any part of the rect store the id.
///
/// Trade-off: cheap rebuild, O(visible) hit-test. Suitable for the editor's
/// modest entity count (target 2000 nodes). Not a quadtree by design —
/// simpler and faster for our scale.
/// </summary>
public sealed class SpatialIndex
{
    private readonly float _cellSize;
    private readonly Dictionary<(int X, int Y), List<NodeId>> _cells = new();
    private readonly Dictionary<NodeId, RectF> _bounds = new();

    /// <summary>Create an index with cells of the given size in canvas units.</summary>
    public SpatialIndex(float cellSize = 256f)
    {
        if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
        _cellSize = cellSize;
    }

    /// <summary>Number of indexed nodes.</summary>
    public int Count => _bounds.Count;

    /// <summary>Bulk reset: clear and re-populate.</summary>
    public void Rebuild(IEnumerable<(NodeId Id, RectF Bounds)> nodes)
    {
        _cells.Clear();
        _bounds.Clear();
        foreach (var (id, b) in nodes) Insert(id, b);
    }

    /// <summary>Insert or update a node's bounds.</summary>
    public void Insert(NodeId id, RectF bounds)
    {
        // If already present, remove first.
        if (_bounds.ContainsKey(id)) Remove(id);

        _bounds[id] = bounds;
        foreach (var key in CellsCovering(bounds))
        {
            if (!_cells.TryGetValue(key, out var list))
            {
                list = new List<NodeId>(4);
                _cells[key] = list;
            }
            list.Add(id);
        }
    }

    /// <summary>Remove a node from the index.</summary>
    public bool Remove(NodeId id)
    {
        if (!_bounds.TryGetValue(id, out var bounds)) return false;
        _bounds.Remove(id);
        foreach (var key in CellsCovering(bounds))
        {
            if (_cells.TryGetValue(key, out var list))
            {
                list.Remove(id);
                if (list.Count == 0) _cells.Remove(key);
            }
        }
        return true;
    }

    /// <summary>Get the bounds of an indexed node, or null.</summary>
    public RectF? GetBounds(NodeId id) =>
        _bounds.TryGetValue(id, out var b) ? b : null;

    /// <summary>Enumerate nodes whose bounds intersect the given rect.</summary>
    public IEnumerable<NodeId> Query(RectF area)
    {
        var seen = new HashSet<NodeId>();
        foreach (var key in CellsCovering(area))
        {
            if (!_cells.TryGetValue(key, out var list)) continue;
            foreach (var id in list)
            {
                if (!seen.Add(id)) continue;
                if (_bounds.TryGetValue(id, out var b) && b.Intersects(area))
                    yield return id;
            }
        }
    }

    /// <summary>Enumerate nodes whose bounds fully contain the given point.</summary>
    public IEnumerable<NodeId> QueryPoint(Vector2 p)
    {
        var key = ToCell(p);
        if (!_cells.TryGetValue(key, out var list)) yield break;
        foreach (var id in list)
        {
            if (_bounds.TryGetValue(id, out var b) && b.Contains(p))
                yield return id;
        }
    }

    /// <summary>Enumerate nodes fully enclosed by the given rect (used by box-select default).</summary>
    public IEnumerable<NodeId> QueryFullyEnclosed(RectF area)
    {
        foreach (var id in Query(area))
        {
            if (_bounds.TryGetValue(id, out var b) && area.FullyContains(b))
                yield return id;
        }
    }

    private IEnumerable<(int X, int Y)> CellsCovering(RectF r)
    {
        var (x0, y0) = ToCell(r.Min);
        var (x1, y1) = ToCell(r.Max);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
            yield return (x, y);
    }

    private (int X, int Y) ToCell(Vector2 p) => (
        (int)Math.Floor(p.X / _cellSize),
        (int)Math.Floor(p.Y / _cellSize));
}
