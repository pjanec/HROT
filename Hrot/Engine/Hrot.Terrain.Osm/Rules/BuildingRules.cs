using System.Globalization;
using Clipper2Lib;
using Hrot.Terrain.InputSet;

namespace Hrot.Terrain.Osm;

/// <summary>
/// ⭐ Terrain From OSM §5.5 + F-4 / F-6 + R-4193 (E-9: a PURE function of the model). Footprints, types, storeys, roofs, party walls
/// (shared edges), doors and windows; garages, sheds and anything under 25 m² are SOLID (not enterable) and counted apart.
/// </summary>
public static class BuildingRules
{
    public const double MinArea = 12, MinWidth = 2.5, SolidBelowArea = 25, BackDoorAbove = 150;
    public const double PartyTolerance = 0.3, WindowSpacing = 2.75, Margin = 1.0, DoorClearance = 1.0;

    private static readonly HashSet<string> SolidTypes = new() { "garage", "garages", "shed", "hut", "kiosk", "service", "transformer_tower" };
    private static readonly HashSet<string> NoWallTypes = new() { "roof", "carport" };
    private static readonly HashSet<string> LowPrisms = new() { "ruins", "construction" };
    private static readonly HashSet<string> TallTypes = new() { "industrial", "warehouse", "church", "cathedral", "chapel", "hangar", "barn", "stable", "cowshed" };
    private static readonly HashSet<string> WindowlessGround = new() { "industrial", "warehouse", "garage", "garages", "shed", "hangar", "barn", "stable", "cowshed" };
    private static readonly HashSet<string> PitchedTypes = new() { "house", "detached", "semidetached_house", "terrace", "residential", "bungalow", "farm", "church", "cathedral", "chapel", "barn", "cabin", "stable", "cowshed" };
    private static readonly HashSet<string> FlatTypes = new() { "apartments", "commercial", "industrial", "retail", "office", "warehouse", "hospital", "school", "university", "supermarket", "hotel", "hangar" };

    public static string TypeOf(IReadOnlyDictionary<string, string> tags) =>
        tags.TryGetValue("building", out var b) && b != "no" ? b : (tags.TryGetValue("building:part", out var p) ? p : "yes");

    public static bool IsBuildingTag(IReadOnlyDictionary<string, string> t) =>
        (t.TryGetValue("building", out var b) && b != "no") || t.ContainsKey("building:part");

    // ---------------------------------------------------------------- the rules

    public static BuildingRulesResult Apply(OsmModel model, OsmImportOptions options, AreaBounds bounds)
    {
        var res = new BuildingRulesResult();
        var asm = new GeometryAssembler(model);
        var candidates = new List<Candidate>();
        foreach (var shape in asm.Areas(IsBuildingTag).ToList())
        {
            string type = TypeOf(shape.Tags);
            bool isPart = !shape.Tags.ContainsKey("building") || shape.Tags["building"] == "no";
            if (isPart && ParseNumber(OsmTags.Get(shape.Tags, "min_level")) is double ml && ml > 0) { res.PartsIgnored++; continue; }
            if (NoWallTypes.Contains(type)) { res.NoWalls++; continue; }
            var parts = new List<(PathD Ring, long[]? Ids)>();
            foreach (var region in shape.Regions)
            {
                if (region.Holes.Count == 0) parts.Add((region.Outer, region.OuterIds));
                else
                {
                    res.SplitCourtyards++;
                    int filled = 0;
                    foreach (var p in Polygons.SplitHoles(region, MinArea, ref filled)) parts.Add((p, null));
                    res.FilledHoles += filled;
                }
            }
            for (int k = 0; k < parts.Count; k++)
                candidates.Add(new Candidate(parts.Count == 1 ? shape.Id : $"{shape.Id}-{k + 1}", parts[k].Ring, parts[k].Ids, shape.Tags, type, isPart));
        }
        res.BrokenRings = asm.BrokenRings;

        var kept = new List<Candidate>();
        foreach (var c in candidates)
        {
            if (c.Ring.Count < 3) continue;
            if (!InsideBounds(c.Ring, bounds)) { res.DroppedAtEdge++; continue; }
            if (Polygons.Area(c.Ring) < MinArea) { res.DroppedSmall++; continue; }
            if (MinExtent(c.Ring) < MinWidth) { res.DroppedThin++; continue; }
            kept.Add(c);
        }
        // a building:part that lies inside a building's outline is merged into it (the outline already is the building)
        var mains = kept.Where(c => !c.IsPart).ToList();
        foreach (var c in kept)
        {
            if (c.IsPart && mains.Any(m => Inside(c.Ring, m.Ring))) { res.PartsMerged++; continue; }
            res.Buildings.Add(Classify(c, options));
        }

        FindParties(res);
        var roads = RoadRules.HighwayIndex(model);
        var entrances = EntranceIndex(model);
        foreach (var b in res.Buildings)
        {
            if (b.HeightInferred && b.Enterable) res.HeightsInferred++;
            if (!b.Enterable) continue;
            if (!PlaceDoors(b, roads, entrances)) res.NoFreeWall++;
            PlaceWindows(b);
            res.Doors += b.Doors;
            res.Windows += b.Windows;
        }
        return res;
    }

    private sealed record Candidate(string Id, PathD Ring, long[]? Ids, IReadOnlyDictionary<string, string> Tags, string Type, bool IsPart);

    private static bool InsideBounds(PathD ring, AreaBounds b)
    {
        foreach (var p in ring) if (p.x < b.MinX || p.x > b.MaxX || p.y < b.MinY || p.y > b.MaxY) return false;
        return true;
    }

    private static bool Inside(PathD inner, PathD outer)
    {
        double cx = inner.Average(p => p.x), cy = inner.Average(p => p.y);
        return Polygons.Contains(outer, cx, cy);
    }

    /// <summary>The thinnest extent of the footprint over the directions of its edges.</summary>
    public static double MinExtent(IReadOnlyList<PointD> ring)
    {
        double best = double.MaxValue;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Count];
            double dx = b.x - a.x, dy = b.y - a.y, l = Math.Sqrt(dx * dx + dy * dy);
            if (l < 1e-9) continue;
            double nx = -dy / l, ny = dx / l, lo = double.MaxValue, hi = double.MinValue;
            foreach (var p in ring) { double d = p.x * nx + p.y * ny; if (d < lo) lo = d; if (d > hi) hi = d; }
            best = Math.Min(best, hi - lo);
        }
        return best;
    }

    // ---------------------------------------------------------------- type, storeys, roof

    private static BuildingDraft Classify(Candidate c, OsmImportOptions options)
    {
        var t = c.Tags;
        double area = Polygons.Area(c.Ring), sh = options.StoreyHeight;
        var d = new BuildingDraft { Id = c.Id, Ring = c.Ring, NodeIds = c.Ids, Type = c.Type, Area = area, StoreyHeight = sh };
        d.Enterable = !(SolidTypes.Contains(c.Type) || LowPrisms.Contains(c.Type) || area < SolidBelowArea);   // R-4193
        d.Material = MaterialOf(OsmTags.Get(t, "building:material"));

        double? levels = ParseNumber(OsmTags.Get(t, "building:levels")), roofLevels = ParseNumber(OsmTags.Get(t, "roof:levels")), height = ParseNumber(OsmTags.Get(t, "height"));
        bool tall = TallTypes.Contains(c.Type);
        if (LowPrisms.Contains(c.Type)) { d.Storeys = 1; d.Height = 3.0; d.Roof = "flat"; return d; }
        if (levels is double lv && lv >= 1)
        {
            d.Storeys = Math.Clamp((int)Math.Round(lv) + (roofLevels is double rl && rl >= 1 ? 1 : 0), 1, 60);
            d.Height = d.Storeys * sh;
        }
        else if (tall)
        {
            d.Storeys = 1; d.Height = height is double h0 && h0 >= 2 ? h0 : 6.0; d.HeightInferred = height == null;
        }
        else if (height is double h && h >= 2)
        {
            d.Storeys = Math.Clamp((int)Math.Round(h / sh), 1, 60);
            d.Height = d.Storeys * sh;
        }
        else
        {
            d.HeightInferred = true;
            d.Storeys = c.Type switch
            {
                "house" or "detached" or "residential" or "semidetached_house" or "terrace" => 2,
                "apartments" => 4,
                "commercial" or "retail" or "office" => 2,
                "greenhouse" or "garage" or "garages" or "shed" => 1,
                _ => options.DefaultStoreys,
            };
            if (c.Type == "greenhouse") d.Storeys = 1;
            d.Height = d.Storeys * sh;
        }
        if (c.Type == "greenhouse") { d.Storeys = 1; d.Height = Math.Min(d.Height, sh); }
        d.Roof = RoofOf(t, c.Type, d.Storeys);
        if (d.Roof == "pitched")
            d.RidgeRise = ParseNumber(OsmTags.Get(t, "roof:height")) is double rh && rh > 0 ? rh : Math.Max(0.8, 0.35 * MinExtent(c.Ring));
        return d;
    }

    private static string RoofOf(IReadOnlyDictionary<string, string> t, string type, int storeys)
    {
        if (t.TryGetValue("roof:shape", out var shape) && shape.Length > 0) return shape == "flat" ? "flat" : "pitched";
        if (PitchedTypes.Contains(type)) return "pitched";
        if (FlatTypes.Contains(type)) return "flat";
        return storeys <= 2 ? "pitched" : "flat";
    }

    private static string? MaterialOf(string? m) => m?.ToLowerInvariant() switch
    {
        "brick" or "bricks" => "brick",
        "concrete" or "cement_block" or "reinforced_concrete" => "concrete",
        "stone" or "sandstone" or "limestone" or "granite" => "stone",
        "wood" or "timber_framing" or "log" => "wood",
        "metal" or "steel" or "aluminium" or "aluminum" => "metal",
        "mud" or "adobe" => "mud",
        _ => null,
    };

    /// <summary>"12", "12 m", "12,5" → 12 / 12 / 12.5; null for anything else.</summary>
    public static double? ParseNumber(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().Replace(',', '.');
        int end = 0;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.' || (end == 0 && s[end] == '-'))) end++;
        return end > 0 && double.TryParse(s.AsSpan(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    // ---------------------------------------------------------------- party walls (F-4, E-5)

    private static void FindParties(BuildingRulesResult res)
    {
        var bs = res.Buildings;
        foreach (var b in bs) { b.Party = new List<(double, double)>[b.Ring.Count]; for (int i = 0; i < b.Party.Length; i++) b.Party[i] = new(); }

        // 1. by NODE IDS: two footprints holding the same consecutive node pair share that edge — written with identical vertices already
        var byPair = new Dictionary<(long, long), List<(int B, int E)>>();
        for (int bi = 0; bi < bs.Count; bi++)
        {
            var ids = bs[bi].NodeIds;
            if (ids == null || ids.Length != bs[bi].Ring.Count) continue;
            for (int e = 0; e < ids.Length; e++)
            {
                long a = ids[e], b = ids[(e + 1) % ids.Length];
                var key = a < b ? (a, b) : (b, a);
                if (!byPair.TryGetValue(key, out var l)) byPair[key] = l = new();
                l.Add((bi, e));
            }
        }
        foreach (var l in byPair.Values)
        {
            if (l.Select(x => x.B).Distinct().Count() < 2) continue;
            res.SharedWallsById++;
            foreach (var (bi, e) in l) bs[bi].Party[e].Add((0, EdgeLength(bs[bi].Ring, e)));
        }

        // 2. by DISTANCE (≤ 0.3 m apart, antiparallel, overlapping): first snap near-equal vertices, then record the overlaps
        const double cell = 30;
        var grid = new Dictionary<(int, int), List<int>>();
        for (int bi = 0; bi < bs.Count; bi++)
        {
            var (x0, y0, x1, y1) = Polygons.Bbox(bs[bi].Ring);
            for (int cx = (int)Math.Floor((x0 - 0.5) / cell); cx <= (int)Math.Floor((x1 + 0.5) / cell); cx++)
                for (int cy = (int)Math.Floor((y0 - 0.5) / cell); cy <= (int)Math.Floor((y1 + 0.5) / cell); cy++)
                {
                    if (!grid.TryGetValue((cx, cy), out var l)) grid[(cx, cy)] = l = new();
                    l.Add(bi);
                }
        }
        var pairs = new SortedSet<(int, int)>();
        foreach (var l in grid.Values)
            for (int i = 0; i < l.Count; i++) for (int j = i + 1; j < l.Count; j++) pairs.Add((Math.Min(l[i], l[j]), Math.Max(l[i], l[j])));
        foreach (var (i, j) in pairs) ProcessPair(bs[i], bs[j], snap: true, res);
        foreach (var (i, j) in pairs) ProcessPair(bs[i], bs[j], snap: false, res);
        foreach (var b in bs) for (int e = 0; e < b.Party.Length; e++) b.Party[e] = Merge(b.Party[e]);
    }

    private static List<(double T0, double T1)> Merge(List<(double T0, double T1)> l)
    {
        if (l.Count < 2) return l;
        l.Sort();
        var r = new List<(double, double)> { l[0] };
        for (int i = 1; i < l.Count; i++)
            if (l[i].T0 <= r[^1].Item2 + 0.05) r[^1] = (r[^1].Item1, Math.Max(r[^1].Item2, l[i].T1)); else r.Add(l[i]);
        return r;
    }

    private static double EdgeLength(PathD ring, int e)
    {
        var a = ring[e]; var b = ring[(e + 1) % ring.Count];
        return Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
    }

    private static void ProcessPair(BuildingDraft A, BuildingDraft B, bool snap, BuildingRulesResult res)
    {
        const double tol = PartyTolerance;
        int na = A.Ring.Count, nb = B.Ring.Count;
        for (int ea = 0; ea < na; ea++)
        {
            for (int eb = 0; eb < nb; eb++)
            {
                var p = A.Ring[ea]; var q = A.Ring[(ea + 1) % na]; var r = B.Ring[eb]; var s = B.Ring[(eb + 1) % nb];
                if (Math.Max(p.x, q.x) + tol < Math.Min(r.x, s.x) || Math.Max(r.x, s.x) + tol < Math.Min(p.x, q.x) ||
                    Math.Max(p.y, q.y) + tol < Math.Min(r.y, s.y) || Math.Max(r.y, s.y) + tol < Math.Min(p.y, q.y)) continue;
                double dax = q.x - p.x, day = q.y - p.y, la = Math.Sqrt(dax * dax + day * day);
                double dbx = s.x - r.x, dby = s.y - r.y, lb = Math.Sqrt(dbx * dbx + dby * dby);
                if (la < 0.5 || lb < 0.5) continue;
                if ((dax * dbx + day * dby) / (la * lb) > -0.9986) continue;     // antiparallel within 3°
                if (Math.Abs(dax * (r.y - p.y) - day * (r.x - p.x)) / la > tol || Math.Abs(dax * (s.y - p.y) - day * (s.x - p.x)) / la > tol) continue;
                double tr = ((r.x - p.x) * dax + (r.y - p.y) * day) / la, ts = ((s.x - p.x) * dax + (s.y - p.y) * day) / la;
                double lo = Math.Max(0, Math.Min(tr, ts)), hi = Math.Min(la, Math.Max(tr, ts));
                if (hi - lo < 0.5) continue;
                if (snap)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        int vi = k == 0 ? eb : (eb + 1) % nb;
                        var v = B.Ring[vi];
                        foreach (var target in new[] { p, q })
                            if (Math.Abs(v.x - target.x) <= tol && Math.Abs(v.y - target.y) <= tol && (v.x != target.x || v.y != target.y))
                            { B.Ring[vi] = target; res.SnappedVertices++; break; }
                    }
                }
                else
                {
                    bool already = A.Party[ea].Any(t => t.T0 <= lo + 0.05 && t.T1 >= hi - 0.05);
                    A.Party[ea].Add((lo, hi));
                    double tp = ((p.x - r.x) * dbx + (p.y - r.y) * dby) / lb, tq = ((q.x - r.x) * dbx + (q.y - r.y) * dby) / lb;
                    B.Party[eb].Add((Math.Max(0, Math.Min(tp, tq)), Math.Min(lb, Math.Max(tp, tq))));
                    if (!already) res.SharedWallsByDistance++;
                }
            }
        }
    }

    // ---------------------------------------------------------------- walls, doors, windows

    /// <summary>A straight run of one or more collinear edges.</summary>
    internal sealed class Wall
    {
        public double Ax, Ay, Dx, Dy, Length, Nx, Ny, Heading;
        public List<(double T0, double T1)> Party = new();
        public List<double> DoorAt = new();

        public (double X, double Y) At(double t) => (Ax + Dx * t, Ay + Dy * t);

        /// <summary>The longest stretch not shared with a neighbour, as (centre, length).</summary>
        public (double Centre, double Length) LongestFree()
        {
            double bestC = 0, bestL = 0, from = 0;
            foreach (var (t0, t1) in Merge(new List<(double, double)>(Party)).Append((Length, Length)))
            {
                double len = t0 - from;
                if (len > bestL) { bestL = len; bestC = from + len / 2; }
                from = Math.Max(from, t1);
            }
            return (bestC, bestL);
        }

        public bool InParty(double t, double pad) => Party.Any(p => t >= p.T0 - pad && t <= p.T1 + pad);
    }

    internal static List<Wall> Walls(BuildingDraft b)
    {
        var ring = b.Ring;
        int n = ring.Count;
        var walls = new List<Wall>();
        // start at an edge that does not continue its predecessor
        int start = 0;
        for (int i = 0; i < n; i++) if (!Continues(ring, (i + n - 1) % n, i)) { start = i; break; }
        Wall? cur = null;
        double offset = 0;
        for (int k = 0; k < n; k++)
        {
            int e = (start + k) % n;
            var a = ring[e]; var c = ring[(e + 1) % n];
            double dx = c.x - a.x, dy = c.y - a.y, l = Math.Sqrt(dx * dx + dy * dy);
            if (l < 1e-6) continue;
            if (cur == null || !Continues(ring, (e + n - 1) % n, e))
            {
                cur = new Wall { Ax = a.x, Ay = a.y, Dx = dx / l, Dy = dy / l };
                cur.Nx = cur.Dy; cur.Ny = -cur.Dx;                                   // outward for a counter-clockwise ring
                cur.Heading = Heading(cur.Nx, cur.Ny);
                walls.Add(cur);
                offset = 0;
            }
            foreach (var (t0, t1) in b.Party[e]) cur.Party.Add((offset + t0, offset + t1));
            offset += l;
            cur.Length = offset;
        }
        return walls;
    }

    private static bool Continues(PathD ring, int e1, int e2)
    {
        int n = ring.Count;
        var a = ring[e1]; var b = ring[(e1 + 1) % n]; var c = ring[(e2 + 1) % n];
        double d1x = b.x - a.x, d1y = b.y - a.y, d2x = c.x - b.x, d2y = c.y - b.y;
        double l1 = Math.Sqrt(d1x * d1x + d1y * d1y), l2 = Math.Sqrt(d2x * d2x + d2y * d2y);
        if (l1 < 1e-9 || l2 < 1e-9) return true;
        return Math.Abs(d1x * d2y - d1y * d2x) / (l1 * l2) < 0.035 && d1x * d2x + d1y * d2y > 0;   // within 2°
    }

    /// <summary>Compass degrees (0 = north, clockwise) of an outward normal, one decimal, in [0, 360).</summary>
    public static double Heading(double nx, double ny)
    {
        double h = Math.Atan2(nx, ny) * 180.0 / Math.PI;
        if (h < 0) h += 360.0;
        h = Math.Round(h, 1);
        return h >= 360.0 ? 0.0 : h;
    }

    private static Dictionary<(int, int), List<(double X, double Y)>> EntranceIndex(OsmModel m)
    {
        const double cell = 25;
        var idx = new Dictionary<(int, int), List<(double, double)>>();
        foreach (var (id, tags) in m.NodeTags)
        {
            if (!tags.TryGetValue("entrance", out var v) || v == "no" || !m.Nodes.TryGetValue(id, out var p)) continue;
            var key = ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell));
            if (!idx.TryGetValue(key, out var l)) idx[key] = l = new();
            l.Add((p.X, p.Y));
        }
        return idx;
    }

    /// <returns>false when no wall is free for a door</returns>
    private static bool PlaceDoors(BuildingDraft b, SegmentIndex roads, Dictionary<(int, int), List<(double X, double Y)>> entrances)
    {
        var walls = Walls(b);
        var (x0, y0, x1, y1) = Polygons.Bbox(b.Ring);
        const double cell = 25, reach = 0.6;
        var used = new List<(double X, double Y)>();
        for (int cx = (int)Math.Floor((x0 - 1) / cell); cx <= (int)Math.Floor((x1 + 1) / cell); cx++)
            for (int cy = (int)Math.Floor((y0 - 1) / cell); cy <= (int)Math.Floor((y1 + 1) / cell); cy++)
            {
                if (!entrances.TryGetValue((cx, cy), out var list)) continue;
                foreach (var (ex, ey) in list)
                {
                    Wall? best = null; double bestD = reach + 1e-9, bestT = 0;
                    foreach (var w in walls)
                    {
                        double t = Math.Clamp((ex - w.Ax) * w.Dx + (ey - w.Ay) * w.Dy, 0, w.Length);
                        var (px, py) = w.At(t);
                        double d = Math.Sqrt((ex - px) * (ex - px) + (ey - py) * (ey - py));
                        if (w.InParty(t, 0)) continue;
                        if (d < bestD - 0.05 || (best != null && Math.Abs(d - bestD) <= 0.05 && w.Length > best.Length)) { best = w; bestD = d; bestT = t; }
                    }
                    if (best == null || used.Any(u => Math.Abs(u.X - ex) < 1 && Math.Abs(u.Y - ey) < 1)) continue;
                    used.Add((ex, ey));
                    var (dx, dy) = best.At(bestT);
                    b.Openings.Add(new DraftOpening("door", dx, dy, best.Heading, 0, 0));
                    best.DoorAt.Add(bestT);
                }
            }
        if (used.Count > 0) return true;

        // no entrance mapped: ONE door on the wall facing the nearest road, a back door on a big building
        double cxm = b.Ring.Average(p => p.x), cym = b.Ring.Average(p => p.y);
        double rx = 0, ry = 0; bool haveRoad = false;
        if (roads.Nearest(cxm, cym, 400) is SegmentIndex.Hit h)
        {
            double dx = h.X - cxm, dy = h.Y - cym, l = Math.Sqrt(dx * dx + dy * dy);
            if (l > 1e-6) { rx = dx / l; ry = dy / l; haveRoad = true; }
        }
        Wall? front = null; double bestScore = double.MinValue;
        foreach (var w in walls)
        {
            var (_, free) = w.LongestFree();
            if (free < 1.5) continue;
            double score = haveRoad ? w.Nx * rx + w.Ny * ry + 1e-4 * w.Length : w.Length;
            if (score > bestScore) { bestScore = score; front = w; }
        }
        if (front == null) return false;
        AddDoor(b, front);
        if (b.Area > BackDoorAbove && haveRoad)
        {
            Wall? back = null; double worst = double.MaxValue;
            foreach (var w in walls)
            {
                if (w == front || w.LongestFree().Length < 1.5) continue;
                double score = w.Nx * rx + w.Ny * ry;
                if (score < worst) { worst = score; back = w; }
            }
            if (back != null) AddDoor(b, back);
        }
        return true;
    }

    private static void AddDoor(BuildingDraft b, Wall w)
    {
        var (c, _) = w.LongestFree();
        var (x, y) = w.At(c);
        b.Openings.Add(new DraftOpening("door", x, y, w.Heading, 0, 0));
        w.DoorAt.Add(c);
    }

    private static void PlaceWindows(BuildingDraft b)
    {
        // doors were recorded on walls rebuilt here by position
        var walls = Walls(b);
        foreach (var d in b.Openings.Where(o => o.Type == "door").ToList())
            foreach (var w in walls)
            {
                double t = (d.X - w.Ax) * w.Dx + (d.Y - w.Ay) * w.Dy;
                double off = Math.Abs((d.X - w.Ax) * w.Nx + (d.Y - w.Ay) * w.Ny);
                if (off < 0.05 && t >= -0.05 && t <= w.Length + 0.05) w.DoorAt.Add(t);
            }
        int storeys = b.Storeys;
        for (int s = 0; s < storeys; s++)
        {
            if (s == 0 && WindowlessGround.Contains(b.Type)) continue;
            foreach (var w in walls)
            {
                double usable = w.Length - 2 * Margin;
                if (usable < 0) continue;
                int k = (int)Math.Round(usable / WindowSpacing);
                int count = k + 1;
                for (int i = 0; i < count; i++)
                {
                    double t = k == 0 ? w.Length / 2 : Margin + i * usable / k;
                    if (w.InParty(t, 0.5) || w.DoorAt.Any(dt => Math.Abs(dt - t) < DoorClearance)) continue;
                    var (x, y) = w.At(t);
                    b.Openings.Add(new DraftOpening("window", x, y, w.Heading, s, 1.0));
                }
            }
        }
    }

    // ---------------------------------------------------------------- the ground

    /// <summary>
    /// The building as the input set stores it (⭐ TH-C / F-1): <c>baseZ</c> = the LOWEST ground under the footprint, storeys above it, openings
    /// on their floors. <paramref name="ground"/> null (the preview) stands it at 0.
    /// </summary>
    public static Building Finish(BuildingDraft d, Func<double, double, double>? ground)
    {
        double baseZ = ground == null ? 0 : R2(LowestGround(d.Ring, ground));
        var storeyZ = new List<double>();
        for (int i = 0; i < d.Storeys; i++) storeyZ.Add(R2(baseZ + i * d.StoreyHeight));
        var foot = new List<(double, double)>(d.Ring.Count + 1);
        foreach (var p in d.Ring) foot.Add((R2(p.x), R2(p.y)));
        foot.Add(foot[0]);
        var openings = new List<Opening>(d.Openings.Count);
        foreach (var o in d.Openings) openings.Add(new Opening(o.Type, R2(o.X), R2(o.Y), R2(baseZ + o.Storey * d.StoreyHeight + o.Dz), o.Heading, o.Storey));
        double height = R2(d.Height);
        bool pitched = d.Roof == "pitched";
        return new Building(d.Id, foot, baseZ, height, d.Roof, pitched ? R2(baseZ + height + Math.Max(0.5, d.RidgeRise)) : null, storeyZ, openings, d.Material, d.Enterable);
    }

    private static double R2(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public static double LowestGround(PathD ring, Func<double, double, double> ground)
    {
        double lo = double.MaxValue;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Count];
            lo = Math.Min(lo, Math.Min(ground(a.x, a.y), ground((a.x + b.x) / 2, (a.y + b.y) / 2)));
        }
        var (x0, y0, x1, y1) = Polygons.Bbox(ring);
        double step = Math.Max(1.0, Math.Sqrt((x1 - x0) * (y1 - y0) / 20000.0));
        for (double y = Math.Ceiling(y0); y <= y1; y += step)
            for (double x = Math.Ceiling(x0); x <= x1; x += step)
                if (Polygons.Contains(ring, x, y)) lo = Math.Min(lo, ground(x, y));
        return lo;
    }
}
