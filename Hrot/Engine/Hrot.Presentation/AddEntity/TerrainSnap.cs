using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D.Gizmos;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐ CE-5009 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-6, §6 S-4 build notes; R-5033) — what a terrain LINE tool snaps to: the END of a placed terrain line
/// (it continues a hedge), a house WALL (a wall meets a house), a road EDGE offset by <see cref="RoadOffset"/> (a wall along the road). Priority end &gt; wall &gt;
/// road edge. Built from the ghost's context (the base terrain's houses, its road corridors with their half-widths — <c>TerrainRoads.Corridors</c>) once per
/// context; the line ends re-read from the world when its version moves. ⭐ <see cref="Snap"/> runs on every cursor move: it allocates nothing (a grid of
/// reused lists, value results — R-2084).
/// </summary>
public sealed class TerrainSnap
{
    /// <summary>The grid's cell side, metres (the snap reach is a few metres: a query reads a 3 × 3 block of cells).</summary>
    public const float Cell = 16f;

    private readonly List<(Vector2 A, Vector2 B, SnapKind Kind)> _segments = new();
    private readonly Dictionary<long, List<int>> _grid = new();
    private readonly List<Vector2> _ends = new();
    private TerrainWorld? _base;
    private IReadOnlyList<RoadCorridor>? _roads;
    private float _builtOffset = float.NaN;
    private uint _endsVersion = uint.MaxValue;
    private EntityRepository? _endsWorld;

    /// <summary>How far outside a road's edge a line runs (metres; the draw panel's field).</summary>
    public float RoadOffset { get; set; } = 1f;

    /// <summary>The segments (walls and road edges) currently indexed — for rails.</summary>
    public int SegmentCount => _segments.Count;

    /// <summary>Re-indexes the walls and road edges when the context or the offset changed (cold: allocates only then).</summary>
    public void Prepare(TerrainGhostContext? context) => Prepare(context?.Base, context?.Roads);

    /// <summary>The same from the base terrain and its roads directly.</summary>
    public void Prepare(TerrainWorld? baseWorld, TerrainRoads? roads) => Prepare(baseWorld, roads?.Corridors);

    /// <summary>The same from the base terrain and road corridors (a rail builds them by hand).</summary>
    public void Prepare(TerrainWorld? baseWorld, IReadOnlyList<RoadCorridor>? corridors)
    {
        if (ReferenceEquals(baseWorld, _base) && ReferenceEquals(corridors, _roads) && _builtOffset == RoadOffset) return;
        _base = baseWorld; _roads = corridors; _builtOffset = RoadOffset;
        _segments.Clear();
        foreach (var list in _grid.Values) list.Clear();
        if (baseWorld is not null)
        {
            // a house is a TEMPLATE building (its footprint in Buildings) or a SOLID one (a Building prism) — both have walls a line meets
            foreach (var b in baseWorld.Buildings)
            {
                var fp = b.Footprint;
                for (int i = 0; i < fp.Length; i++) Add(fp[i], fp[(i + 1) % fp.Length], SnapKind.Wall);
            }
            foreach (var pr in baseWorld.Prisms)
            {
                if (pr.Kind != TerrainPrismKind.Building) continue;
                var fp = pr.Footprint;
                for (int i = 0; i < fp.Length; i++) Add(fp[i], fp[(i + 1) % fp.Length], SnapKind.Wall);
            }
        }
        if (corridors is not null)
            foreach (var road in corridors)
            {
                float off = road.HalfWidth + RoadOffset;
                for (int i = 1; i < road.Line.Length; i++)
                {
                    var a = road.Line[i - 1]; var b = road.Line[i]; var d = b - a; float len = d.Length();
                    if (len < 1e-3f) continue;
                    var n = new Vector2(-d.Y, d.X) / len * off;
                    Add(a + n, b + n, SnapKind.RoadEdge);
                    Add(a - n, b - n, SnapKind.RoadEdge);
                }
            }
    }

    /// <summary>Re-reads the ends of the placed terrain LINE features when <paramref name="world"/>'s version moved (cold).</summary>
    public void PrepareEnds(EntityRepository? world)
    {
        if (world is null) { _ends.Clear(); _endsWorld = null; return; }
        if (ReferenceEquals(world, _endsWorld) && world.GlobalVersion == _endsVersion) return;
        _endsWorld = world; _endsVersion = world.GlobalVersion;
        _ends.Clear();
        if (!world.IsComponentTypeRegistered<TerrainFeature>() || !world.IsComponentTypeRegistered<SimTransform>()) return;
        foreach (var e in world.Query().With<TerrainFeature>().With<SimTransform>().Build())
        {
            byte kind = world.GetComponentRO<TerrainFeature>(e).Kind;
            if (kind is not (TerrainFeature.Wall or TerrainFeature.Fence or TerrainFeature.Hedge or TerrainFeature.Row)) continue;
            if (!world.HasManagedComponent<Hrot.Map.Common.Components.EditablePolyline>(e)) continue;
            var pts = world.GetComponent<Hrot.Map.Common.Components.EditablePolyline>(e).Points;
            if (pts.Count < 2) continue;
            var at = world.GetComponentRO<SimTransform>(e).Position;
            var o = new Vector2(at.X, at.Y);
            _ends.Add(o + pts[0]); _ends.Add(o + pts[^1]);
        }
    }

    /// <summary>What <paramref name="p"/> snaps to within <paramref name="reach"/> metres: an end, else a wall, else a road edge, else nothing.</summary>
    public SnapHit Snap(Vector2 p, float reach)
    {
        float r2 = reach * reach, best = r2;
        int end = -1;
        for (int i = 0; i < _ends.Count; i++) { float d2 = Vector2.DistanceSquared(_ends[i], p); if (d2 <= best) { best = d2; end = i; } }
        if (end >= 0) return new SnapHit(_ends[end], _ends[end], _ends[end], SnapKind.LineEnd);

        float bestWall = r2, bestRoad = r2; int wall = -1, road = -1; Vector2 wallAt = default, roadAt = default;
        int cx = Cx(p.X), cy = Cx(p.Y);
        for (int gy = cy - 1; gy <= cy + 1; gy++)
            for (int gx = cx - 1; gx <= cx + 1; gx++)
            {
                if (!_grid.TryGetValue(Key(gx, gy), out var list)) continue;
                for (int k = 0; k < list.Count; k++)
                {
                    int s = list[k];
                    var (a, b, kind) = _segments[s];
                    var q = Closest(a, b, p); float d2 = Vector2.DistanceSquared(q, p);
                    if (kind == SnapKind.Wall) { if (d2 <= bestWall) { bestWall = d2; wall = s; wallAt = q; } }
                    else if (d2 <= bestRoad) { bestRoad = d2; road = s; roadAt = q; }
                }
            }
        if (wall >= 0) return new SnapHit(wallAt, _segments[wall].A, _segments[wall].B, SnapKind.Wall);
        if (road >= 0) return new SnapHit(roadAt, _segments[road].A, _segments[road].B, SnapKind.RoadEdge);
        return SnapHit.None;
    }

    /// <summary>
    /// ⭐ CE-5009 — the snap the draw panel's options ask for: nothing when its <see cref="TerrainDrawOptions.Snap"/> is off (the click lands raw), else the index re-prepared with the panel's
    /// <see cref="TerrainDrawOptions.RoadOffset"/> (the road-edge lines move to <c>HalfWidth + offset</c>) and queried at <paramref name="p"/>.
    /// </summary>
    public SnapHit SnapWith(TerrainDrawOptions? options, TerrainGhostContext? context, EntityRepository? world, Vector2 p, float reach)
        => SnapWith(options, context?.Base, context?.Roads?.Corridors, world, p, reach);

    /// <summary>The same from the base terrain and road corridors directly (a rail builds them by hand).</summary>
    public SnapHit SnapWith(TerrainDrawOptions? options, TerrainWorld? baseWorld, IReadOnlyList<RoadCorridor>? corridors, EntityRepository? world, Vector2 p, float reach)
    {
        if (options is { Snap: false }) return SnapHit.None;
        if (options is not null) RoadOffset = options.RoadOffset;
        Prepare(baseWorld, corridors);
        PrepareEnds(world);
        return Snap(p, reach);
    }

    /// <summary>
    /// ⭐ CE-5009 — the hint line of a snap kind with the road offset in it ("snapped: road edge +1 m · Ctrl: free"); the strings are built by <see cref="HintTracker"/> only when the kind or the offset changes.
    /// </summary>
    public static string Hint(SnapKind kind, float roadOffset)
        => kind == SnapKind.RoadEdge ? "snapped: road edge +" + roadOffset.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " m · Ctrl: free" : Hint(kind);

    /// <summary>
    /// ⭐ CE-5009 — puts the snap hint on the tool's hint line ONLY when it changes: <see cref="Update"/> every frame is a compare (no string work, 0 B — R-2084) and reports a new text when the kind (or, for a road edge, the offset) moved,
    /// or <c>null</c> through <paramref name="text"/> with true when it went back to no snap (the host restores the tool's own hint).
    /// </summary>
    public sealed class HintTracker
    {
        private SnapKind _kind; private float _offset = float.NaN;

        /// <summary>The current kind shown (None = the tool's own hint).</summary>
        public SnapKind Kind => _kind;

        /// <summary>Forgets what was shown (the tool was re-armed or disarmed).</summary>
        public void Reset() { _kind = SnapKind.None; _offset = float.NaN; }

        /// <summary>True when the line must change: <paramref name="text"/> is the new hint, or null to restore the tool's own.</summary>
        public bool Update(SnapKind kind, float roadOffset, out string? text)
        {
            text = null;
            if (kind == _kind && (kind != SnapKind.RoadEdge || roadOffset == _offset)) return false;
            _kind = kind; _offset = roadOffset;
            text = kind == SnapKind.None ? null : Hint(kind, roadOffset);
            return true;
        }
    }

    /// <summary>The hint line for <paramref name="hit"/> (a fixed string per kind — no allocation).</summary>
    public static string Hint(SnapKind kind) => kind switch
    {
        SnapKind.LineEnd => "snapped: the end of a line · Ctrl: free",
        SnapKind.Wall => "snapped: a house wall · Ctrl: free",
        SnapKind.RoadEdge => "snapped: a road edge · Ctrl: free",
        _ => "",
    };

    private void Add(Vector2 a, Vector2 b, SnapKind kind)
    {
        int index = _segments.Count;
        _segments.Add((a, b, kind));
        int x0 = Cx(MathF.Min(a.X, b.X)), x1 = Cx(MathF.Max(a.X, b.X)), y0 = Cx(MathF.Min(a.Y, b.Y)), y1 = Cx(MathF.Max(a.Y, b.Y));
        for (int gy = y0; gy <= y1; gy++)
            for (int gx = x0; gx <= x1; gx++)
            {
                long key = Key(gx, gy);
                if (!_grid.TryGetValue(key, out var list)) _grid[key] = list = new List<int>();
                list.Add(index);
            }
    }

    private static Vector2 Closest(Vector2 a, Vector2 b, Vector2 p)
    {
        var d = b - a; float len2 = d.LengthSquared();
        if (len2 < 1e-9f) return a;
        float t = Math.Clamp(Vector2.Dot(p - a, d) / len2, 0f, 1f);
        return a + d * t;
    }

    private static int Cx(float v) => (int)MathF.Floor(v / Cell);
    private static long Key(int x, int y) => ((long)x << 32) | (uint)y;
}
