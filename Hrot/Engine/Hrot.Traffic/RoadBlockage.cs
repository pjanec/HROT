using System.Numerics;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.Terrain;

namespace Hrot.Traffic;

/// <summary>
/// ⭐⭐ <c>CE-4590</c> (P-2; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §3.2, §4 G-10, §9.2 V-18…V-20, V-24) — the geometry of ROAD BLOCKAGE: which
/// static blockers lie on a road and whether one closes it. A road is CLOSED where, at one station along it, every point across its full width lies inside a
/// blocker; it is PARTLY covered where any point does. Measured against each net in its OWN geometry — our road graph's segments, SUMO's lanes — so no edge-id
/// mapping between the two is needed (V-18's need, removed). Pure; called only when the blocker set changes.
/// </summary>
public static class RoadBlockage
{
    /// <summary>The spacing (m) of the stations along a road and of the points across it.</summary>
    public const float Step = 0.5f;

    /// <summary>The footprints (terrain-local, counter-clockwise) of every ACTIVE static obstacle of <paramref name="world"/> — a roadblock, a wreck, sandbags.</summary>
    public static void Blockers(EntityRepository world, List<Vector2[]> into)
    {
        into.Clear();
        if (!world.IsComponentTypeRegistered<StaticObstacle>() || !world.IsComponentTypeRegistered<ObstacleShape>() || !world.IsComponentTypeRegistered<SimTransform>()) return;
        var library = TerrainMaterialLibrary.Shared;
        library.TryGet(TerrainMaterialLibrary.DefaultMaterial, out var material);
        foreach (var e in world.Query().With<StaticObstacle>().With<ObstacleShape>().With<SimTransform>().Build())
        {
            if (world.GetComponentRO<StaticObstacle>(e).Kind == ObstacleKind.WalkableDeck) continue;   // ⭐ CE-4692 BL-7 — a laid bridge CARRIES a road, it never blocks one
            ref readonly var t = ref world.GetComponentRO<SimTransform>(e);
            ref readonly var shape = ref world.GetComponentRO<ObstacleShape>(e);
            into.Add(TerrainObstacles.PrismOf(t.Position, TerrainObstacles.Yaw(t.Rotation), shape, material!).Footprint);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4591</c> (P-4, G-10) — appends each crater's BOWL (its radius, as a 16-gon inside the circle) to <paramref name="into"/>: a crater whose bowl spans a road's
    /// full width closes it like a roadblock (every node judges it from the replicated records). ⚠ A crater covering PART of a road is no SUMO lane obstacle (V-24 wants a
    /// synthetic body id) — SUMO cars keep the lane; our vehicles keep out of it by the navmesh (D-2).
    /// </summary>
    public static void AddCraters(CraterTable? craters, List<Vector2[]> into)
    {
        if (craters == null) return;
        const int sides = 16;
        for (int i = 0; i < craters.Count; i++)
        {
            var c = craters[i];
            var poly = new Vector2[sides];
            for (int k = 0; k < sides; k++)
            {
                float a = 2f * MathF.PI * k / sides;
                poly[k] = c.Centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * c.Radius;
            }
            into.Add(poly);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T3 (DESIGN_Minefields.md MR-16) — appends the OUTLINE of every minefield that is MARKED on the ground (a side learned and marked it — marking is
    /// physical, MR-13) or known at start (own fields, marked by their owners): a road crossing it closes to SUMO's cars like a roadblock, for every node (the replicated records).
    /// An unknown, unmarked field leaves the road open — cars drive into it and set mines off (their bodies are bodies). Cold: the closure recomputes only on a change.
    /// </summary>
    public static void AddMinefields(EntityRepository world, PlaceKnowledgeTable? knowledge, List<Vector2[]> into)
    {
        if (knowledge == null || !world.IsComponentTypeRegistered<Minefield>()) return;
        var outline = new List<Vector2>();
        foreach (var field in world.Query().With<Minefield>().With<SimTransform>().Build())
        {
            if (world.GetComponentRO<Minefield>(field).KnownAtStartSides == 0 && !knowledge.MarkedByAnySide(field)) continue;
            if (MinefieldKnowledgeOps.TryOutline(world, field, outline, out _)) into.Add(outline.ToArray());
        }
    }

    // ── ⭐⭐ CE-11070 — scenario feature WALLS close lanes by a LINE rule ─────────────────────────────────────────────────────────────────────────────────
    //   📄 docs/designs/engineering/DESIGN_Breachable_Obstacle_Walls.md §8 V-1, V-2, V-7. A wall is thin (0.3–0.6 m) and may cross a road at any angle, so the
    //   station sampling above (one station's whole cross-line inside a polygon) would catch it only by luck, and any 0.5 m hole would re-open the road. Instead:
    //   a wall line that crosses a LANE from edge to edge closes that lane; the lane re-opens only where a passable gap (merged Passages) at least
    //   minDriveThroughM wide covers the point where the wall crosses the lane's centreline. ⭐ Read from the wall PANELS and the REPLICATED breach table
    //   (every node the same), never the navmesh bake's re-cut prisms (V-2). ⭐ Scenario feature walls only (V-7): a base / imported wall never closes a road.

    /// <summary>One scenario feature wall line: its posts and the arclength (m) at each, and the passable gaps along it (arclength intervals, each at least the minimum width).</summary>
    public sealed record WallLine(string Key, Vector2[] Points, float[] At, (float From, float To)[] Gaps);

    /// <summary>
    /// ⭐ CE-11070 — true when <paramref name="label"/> is a scenario FEATURE's line label, <c>&lt;entity guid&gt;/0</c> (<see cref="FeatureExpander.OfKind"/>: the key is the feature
    /// entity's guid, on the live overlay and the compiled one alike). A base or imported wall carries its own name (or none) and never matches. Content-derived ⇒ the same on every node.
    /// </summary>
    public static bool IsScenarioWallLabel(string? label)
        => label is { Length: > 2 } && label.EndsWith("/0", StringComparison.Ordinal) && Guid.TryParse(label.AsSpan(0, label.Length - 2), out _);

    /// <summary>
    /// ⭐ CE-11070 — the scenario feature wall lines of <paramref name="world"/> (free panels, <see cref="IsScenarioWallLabel"/>, one line per <see cref="WallGroups"/> group, posts in
    /// panel order), each with its PASSABLE GAPS from <paramref name="damage"/>: the Passages on its panels in line arclength, merged where they touch, kept when at least
    /// <paramref name="minGapM"/> wide. Cold (called on a change only).
    /// </summary>
    public static void FeatureWallLines(TerrainWorld world, BuildingDamageTable? damage, float minGapM, List<WallLine> into)
    {
        into.Clear();
        var panels = world.Panels;
        int i = 0;
        var pts = new List<Vector2>(); var at = new List<float>(); var gaps = new List<(float, float)>();
        while (i < panels.Count)
        {
            var p = panels[i];
            if (p.Building >= 0 || !IsScenarioWallLabel(p.Label)) { i++; continue; }
            int group = WallGroups.Of(world, i);
            pts.Clear(); at.Clear(); gaps.Clear();
            pts.Add(p.A); at.Add(0f);
            float s = 0f;
            for (; i < panels.Count && panels[i].Building < 0 && WallGroups.Of(world, i) == group; i++)
            {
                var q = panels[i];
                if (damage != null)
                    foreach (var b in damage.BreachesOf(i))
                        if (b.Kind == BreachKind.Passage) gaps.Add((s + b.From, s + b.To));
                s += q.Length;
                pts.Add(q.B); at.Add(s);
            }
            gaps.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            var merged = new List<(float From, float To)>();
            foreach (var g in gaps)
            {
                if (merged.Count > 0 && g.Item1 <= merged[^1].To + 0.05f) merged[^1] = (merged[^1].From, MathF.Max(merged[^1].To, g.Item2));
                else merged.Add(g);
            }
            merged.RemoveAll(g => g.To - g.From < minGapM);
            into.Add(new WallLine(WallGroups.LineKey(world, group) ?? p.Label!, pts.ToArray(), at.ToArray(), merged.ToArray()));
        }
    }

    /// <summary>
    /// ⭐ CE-11070 — true when a wall of <paramref name="walls"/> CLOSES the lane along <paramref name="line"/> (its centreline, terrain-local) <paramref name="width"/> m wide: a wall
    /// segment crosses the centreline, the wall reaches BOTH lane edges from that crossing (on opposite sides, without ending inside the lane), and no passable gap covers the crossing.
    /// A wall that ends inside the lane closes nothing (it is partial); a diagonal wall closes like a square one.
    /// </summary>
    public static bool LaneClosedByWalls(Vector2[] line, float width, IReadOnlyList<WallLine> walls)
    {
        if (walls.Count == 0 || line.Length < 2 || width <= 0f) return false;
        float half = width * 0.5f;
        for (int w = 0; w < walls.Count; w++)
        {
            var wall = walls[w];
            for (int k = 0; k + 1 < wall.Points.Length; k++)
            {
                var a = wall.Points[k]; var b = wall.Points[k + 1];
                for (int j = 0; j + 1 < line.Length; j++)
                {
                    var c = line[j]; var d = line[j + 1];
                    if (!Intersect(a, b, c, d, out float tw, out _)) continue;
                    float u = wall.At[k] + (wall.At[k + 1] - wall.At[k]) * tw;
                    if (InGap(wall.Gaps, u)) continue;   // the crossing is in a passable gap: this wall leaves the lane open here
                    var dir = d - c; float len = dir.Length();
                    if (len < 1e-6f) continue;
                    var side = new Vector2(-dir.Y, dir.X) / len;
                    if (Reaches(wall, k, tw, c, side, half, +1) && Reaches(wall, k, tw, c, side, half, -1)) return true;
                }
            }
        }
        return false;
    }

    /// <summary>Walks the wall from the crossing (segment <paramref name="k"/> at fraction <paramref name="t"/>) toward its end (<paramref name="towards"/> +1) or start (−1): true when it
    /// reaches a lateral offset of <paramref name="half"/> (either side) from the lane line through <paramref name="origin"/> before the wall ends.</summary>
    private static bool Reaches(WallLine wall, int k, float t, Vector2 origin, Vector2 side, float half, int towards)
    {
        var pts = wall.Points;
        if (towards > 0)
        {
            for (int m = k + 1; m < pts.Length; m++) if (MathF.Abs(Vector2.Dot(pts[m] - origin, side)) >= half) return true;
        }
        else
        {
            for (int m = k; m >= 0; m--) if (MathF.Abs(Vector2.Dot(pts[m] - origin, side)) >= half) return true;
        }
        return false;
    }

    private static bool InGap((float From, float To)[] gaps, float u)
    {
        for (int i = 0; i < gaps.Length; i++) if (u >= gaps[i].From && u <= gaps[i].To) return true;
        return false;
    }

    /// <summary>Segment–segment intersection: the fractions along a→b and c→d.</summary>
    private static bool Intersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t, out float v)
    {
        t = v = 0f;
        var r = b - a; var q = d - c;
        float den = r.X * q.Y - r.Y * q.X;
        if (MathF.Abs(den) < 1e-9f) return false;
        var ac = c - a;
        t = (ac.X * q.Y - ac.Y * q.X) / den;
        v = (ac.X * r.Y - ac.Y * r.X) / den;
        return t >= 0f && t <= 1f && v >= 0f && v <= 1f;
    }

    /// <summary>
    /// ⭐ CE-11070 — the segments of <paramref name="blob"/> the walls CLOSE: every lane of the segment (its centreline offset across the road, <c>LaneCount</c> lanes of <c>LaneWidth</c>)
    /// is closed by a wall (<see cref="LaneClosedByWalls"/>). A segment with one lane open stays (our graph has no per-lane closure; a vehicle picks the open lane).
    /// </summary>
    public static void WallClosedSegments(in RoadNetworkBlob blob, IReadOnlyList<WallLine> walls, HashSet<int> into)
    {
        if (walls.Count == 0 || !blob.Segments.IsCreated) return;
        for (int i = 0; i < blob.Segments.Length; i++)
        {
            var s = blob.Segments[i];
            int lanes = Math.Max(1, s.LaneCount);
            float length = MathF.Max(s.Length, Vector2.Distance(s.P0, s.P1));
            int n = Math.Max(2, (int)MathF.Ceiling(length / 1f)) + 1;
            bool all = true;
            for (int l = 0; l < lanes && all; l++)
            {
                float off = -s.LaneWidth * lanes * 0.5f + s.LaneWidth * (l + 0.5f);
                var pts = new Vector2[n];
                for (int k = 0; k < n; k++)
                {
                    float t = (float)k / (n - 1);
                    var p = RoadGraphNavigator.EvaluateHermite(t, s.P0, s.T0, s.P1, s.T1);
                    var d = RoadGraphNavigator.EvaluateHermiteTangent(t, s.P0, s.T0, s.P1, s.T1);
                    var dir = d.LengthSquared() > 1e-9f ? Vector2.Normalize(d) : Vector2.UnitX;
                    pts[k] = p + new Vector2(-dir.Y, dir.X) * off;
                }
                all = LaneClosedByWalls(pts, s.LaneWidth, walls);
            }
            if (all) into.Add(i);
        }
    }

    /// <summary>
    /// ⭐ CE-11070 — SUMO, per LANE: each lane's shape and width is judged by <see cref="LaneClosedByWalls"/>; an edge whose every lane is closed goes into <paramref name="closedEdges"/>
    /// (SUMO closes edges: a two-way road is two edges, so a cut over one lane of a two-lane road re-opens that direction). <paramref name="closedLanes"/> gets every closed lane id
    /// (diagnostics, and the lanes of a multi-lane edge left partly open — ⚠ SumoSharp closes edges only, so such a lane stays drivable: a finding in the design §9).
    /// </summary>
    public static void WallClosedEdges(Sim.Ingest.NetworkModel net, Func<double, double, (double X, double Y)> toLocal, IReadOnlyList<WallLine> walls, List<string> closedEdges, List<string>? closedLanes = null)
    {
        if (walls.Count == 0) return;
        foreach (var edge in net.Edges)
        {
            if (edge.Lanes.Count == 0 || edge.Id.StartsWith(':')) continue;
            bool all = true;
            foreach (var lane in edge.Lanes)
            {
                var pts = new Vector2[lane.Shape.Count];
                for (int i = 0; i < pts.Length; i++) { var (x, y) = toLocal(lane.Shape[i].X, lane.Shape[i].Y); pts[i] = new Vector2((float)x, (float)y); }
                bool closed = LaneClosedByWalls(pts, (float)lane.Width, walls);
                if (closed) closedLanes?.Add(lane.Id);
                all &= closed;
            }
            if (all) closedEdges.Add(edge.Id);
        }
    }

    /// <summary>
    /// ⭐ CE-11070 — the change test: a hash of the scenario wall lines' geometry and of the PASSAGES on their panels in <paramref name="damage"/> — a hole in a house, a Hole in a wall or a
    /// new world with the same walls leaves it unchanged (no recompute). Allocation-free: a walk of the world's panels and the table's per-panel spans.
    /// </summary>
    public static ulong WallSignature(TerrainWorld world, BuildingDamageTable? damage)
    {
        ulong h = 1469598103934665603UL;
        var panels = world.Panels;
        for (int i = 0; i < panels.Count; i++)
        {
            var p = panels[i];
            if (p.Building >= 0 || !IsScenarioWallLabel(p.Label)) continue;
            h = Mix(h, (ulong)i);
            h = Mix(h, (ulong)(long)MathF.Round(p.A.X * 100f)); h = Mix(h, (ulong)(long)MathF.Round(p.A.Y * 100f));
            h = Mix(h, (ulong)(long)MathF.Round(p.B.X * 100f)); h = Mix(h, (ulong)(long)MathF.Round(p.B.Y * 100f));
            if (damage == null) continue;
            foreach (var b in damage.BreachesOf(i))
            {
                if (b.Kind != BreachKind.Passage) continue;
                h = Mix(h, (ulong)(long)MathF.Round(b.From * 100f)); h = Mix(h, (ulong)(long)MathF.Round(b.To * 100f));
            }
        }
        return h;
    }

    private static ulong Mix(ulong h, ulong v) { h ^= v; return h * 1099511628211UL; }

    /// <summary>
    /// The road along <paramref name="at"/>(s), s in [0, 1] over <paramref name="length"/> m, <paramref name="width"/> m wide: 2 = closed (a station fully
    /// covered), 1 = partly covered, 0 = clear.
    /// </summary>
    public static int Covered(Func<float, (Vector2 P, Vector2 Dir)> at, float length, float width, IReadOnlyList<Vector2[]> blockers)
    {
        if (blockers.Count == 0 || length <= 0f || width <= 0f) return 0;
        // a quick reject: no blocker's box near the road's box
        var (rmin, rmax) = Box(at, length, width);
        bool near = false;
        foreach (var b in blockers) { var (bmin, bmax) = Box(b); if (bmin.X <= rmax.X && bmax.X >= rmin.X && bmin.Y <= rmax.Y && bmax.Y >= rmin.Y) { near = true; break; } }
        if (!near) return 0;
        int stations = Math.Max(1, (int)MathF.Ceiling(length / Step));
        int across = Math.Max(1, (int)MathF.Ceiling(width / Step));
        int result = 0;
        for (int k = 0; k <= stations; k++)
        {
            var (p, dir) = at((float)k / stations);
            var side = new Vector2(-dir.Y, dir.X);
            int inside = 0;
            for (int j = 0; j < across; j++)
            {
                float off = -width * 0.5f + width * (j + 0.5f) / across;
                if (InsideAny(p + side * off, blockers)) inside++;
            }
            if (inside == across) return 2;
            if (inside > 0) result = 1;
        }
        return result;
    }

    /// <summary>The segments of <paramref name="blob"/> a blocker CLOSES (every lane covered at one station) — indices in <paramref name="blob"/>.</summary>
    public static void ClosedSegments(in RoadNetworkBlob blob, IReadOnlyList<Vector2[]> blockers, HashSet<int> into)
    {
        into.Clear();
        if (blockers.Count == 0 || !blob.Segments.IsCreated) return;
        for (int i = 0; i < blob.Segments.Length; i++)
        {
            var s = blob.Segments[i];
            float width = s.LaneWidth * Math.Max(1, s.LaneCount);
            if (Covered(t =>
                {
                    var p = RoadGraphNavigator.EvaluateHermite(t, s.P0, s.T0, s.P1, s.T1);
                    var d = RoadGraphNavigator.EvaluateHermiteTangent(t, s.P0, s.T0, s.P1, s.T1);
                    return (p, d.LengthSquared() > 1e-9f ? Vector2.Normalize(d) : Vector2.UnitX);
                }, MathF.Max(s.Length, Vector2.Distance(s.P0, s.P1)), width, blockers) == 2)
                into.Add(i);
        }
    }

    /// <summary>
    /// The SUMO edges a blocker CLOSES: at one fraction of the edge's length EVERY lane is covered across its width. <paramref name="toLocal"/> maps net
    /// coordinates to terrain-local.
    /// </summary>
    public static void ClosedEdges(Sim.Ingest.NetworkModel net, Func<double, double, (double X, double Y)> toLocal, IReadOnlyList<Vector2[]> blockers, List<string> into)
    {
        into.Clear();
        if (blockers.Count == 0) return;
        const int Fractions = 200;
        var all = new bool[Fractions + 1];
        foreach (var edge in net.Edges)
        {
            if (edge.Lanes.Count == 0 || edge.Id.StartsWith(':')) continue;   // an internal (junction) edge
            Array.Fill(all, true);
            bool any = false;
            foreach (var lane in edge.Lanes)
            {
                var pts = lane.Shape.Select(p => { var (x, y) = toLocal(p.X, p.Y); return new Vector2((float)x, (float)y); }).ToArray();
                if (pts.Length < 2) { Array.Fill(all, false); break; }
                float length = 0f; for (int i = 0; i + 1 < pts.Length; i++) length += Vector2.Distance(pts[i], pts[i + 1]);
                var cov = Coverage(pts, length, (float)lane.Width, blockers, Fractions);
                for (int f = 0; f <= Fractions; f++) { all[f] &= cov[f]; any |= cov[f]; }
            }
            if (!any) continue;
            for (int f = 0; f <= Fractions; f++) if (all[f]) { into.Add(edge.Id); break; }
        }
    }

    /// <summary>Per fraction of a lane's length: is it covered across its whole width.</summary>
    private static bool[] Coverage(Vector2[] line, float length, float width, IReadOnlyList<Vector2[]> blockers, int fractions)
    {
        var cov = new bool[fractions + 1];
        int across = Math.Max(1, (int)MathF.Ceiling(width / Step));
        for (int f = 0; f <= fractions; f++)
        {
            var (p, dir) = Along(line, length * f / fractions);
            var side = new Vector2(-dir.Y, dir.X);
            bool full = true;
            for (int j = 0; j < across && full; j++)
                full = InsideAny(p + side * (-width * 0.5f + width * (j + 0.5f) / across), blockers);
            cov[f] = full;
        }
        return cov;
    }

    private static (Vector2 P, Vector2 Dir) Along(Vector2[] line, float s)
    {
        for (int i = 0; i + 1 < line.Length; i++)
        {
            float len = Vector2.Distance(line[i], line[i + 1]);
            if ((s <= len || i + 2 == line.Length) && len > 1e-6f)
                return (line[i] + (line[i + 1] - line[i]) * MathF.Min(1f, s / len), (line[i + 1] - line[i]) / len);
            s -= len;
        }
        return (line[^1], Vector2.UnitX);
    }

    private static bool InsideAny(Vector2 p, IReadOnlyList<Vector2[]> blockers)
    {
        for (int i = 0; i < blockers.Count; i++) if (PolygonMath.Contains(blockers[i], p)) return true;
        return false;
    }

    private static (Vector2 Min, Vector2 Max) Box(Vector2[] poly)
    {
        var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
        foreach (var p in poly) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        return (min, max);
    }

    private static (Vector2 Min, Vector2 Max) Box(Func<float, (Vector2 P, Vector2 Dir)> at, float length, float width)
    {
        var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
        int n = Math.Max(2, (int)MathF.Ceiling(length / 5f));
        for (int k = 0; k <= n; k++) { var (p, _) = at((float)k / n); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        return (min - new Vector2(width), max + new Vector2(width));
    }
}
