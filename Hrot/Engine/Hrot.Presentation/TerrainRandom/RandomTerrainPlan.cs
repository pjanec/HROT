using System.Globalization;
using System.Numerics;
using Fdp.Toolkit.Terrain;
using Hrot.Terrain.Osm;
using Hrot.UI.Common.AddEntity;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using static Hrot.UI.Common.TerrainOsm.OsmWizardStyle;

namespace Hrot.UI.Common.TerrainRandom;

/// <summary>
/// ⭐⭐ Terrain authoring U-8 follow-up 2 (docs/DESIGN_Terrain_Authoring.md §15a; mockup 1, bottom) — the random-terrain wizard's 2-D PLAN: everything the generated <see cref="RandomTerrainView"/> holds, drawn in HROT's map colours — the ground SHADED by its relief, water, woods and
/// scrub, the roads by class, every house, and every laid-out BLOCK as a dashed outline with its chip (the recipe's icon + name, <i>Town block ×4</i> when four of them touch). Hovering a block shows its tooltip (<i>Town block ×4 — 112 houses / seed 4471 · 3–6 storeys / click a block to see it larger</i>);
/// clicking it zooms the plan to that group, a click elsewhere zooms back. ⭐ <b>Built ONCE per view</b> (the groups, the triangulated areas, the relief's node colours, every string); <see cref="Draw"/> allocates nothing in a steady frame (R-2084).
/// The outline is the editor's own (<see cref="Hrot.ScenarioEditor.Gizmos.TerrainBlockGizmo"/>: its colour, its margin) so a block looks the same in the wizard and on the map, and the chip text is the gizmo's <see cref="Hrot.ScenarioEditor.Gizmos.TerrainBlockGizmo.ChipText(string, int)"/>.
/// </summary>
public sealed class RandomTerrainPlan
{
    /// <summary>The plan's ground colours, low → high (the mockup's <c>GROUND</c> bands).</summary>
    private static readonly Vector3[] Ramp =
    {
        new(0x33 / 255f, 0x45 / 255f, 0x2C / 255f), new(0x3B / 255f, 0x51 / 255f, 0x31 / 255f), new(0x4A / 255f, 0x5C / 255f, 0x36 / 255f), new(0x5E / 255f, 0x64 / 255f, 0x3C / 255f), new(0x6E / 255f, 0x68 / 255f, 0x44 / 255f),
    };
    /// <summary>Two blocks of one recipe whose boxes stand within this many metres of each other are ONE group (<i>Town block ×4</i>).</summary>
    public const float GroupGap = 14f;

    /// <summary>The blocks of one recipe that stand together: what a chip and a tooltip speak for.</summary>
    public sealed class Group
    {
        public string Recipe = "";
        public int Count, Houses, MinStoreys, MaxStoreys;
        public Vector2 Min, Max;
        public string Chip = "", Key = "";
        public string IconGlyph = ""; public Vector4 IconColour;
        public readonly List<int> Members = new();
    }

    /// <summary>One row of "what was generated": a recipe, how many blocks, their houses (or their reach in hectares).</summary>
    public sealed record Row(string Recipe, int Blocks, int Houses, double Hectares);

    private readonly RandomTerrainView _view;
    private readonly Vector2 _boundsMin, _boundsMax;
    private readonly PlanBlock[] _blocks;
    private readonly string[][] _tips;
    private readonly (Vector2[] Ring, int[] Tris, int Kind)[] _areas;
    private readonly (Vector2[] Pts, Vector4 Colour, float Width)[] _roads;
    private readonly (Vector2[] Ring, uint Colour, Vector2 Min, Vector2 Max)[] _houses;
    private readonly uint[] _nodes;
    private readonly int _n;
    private Vector2[] _buf = new Vector2[64];

    public RandomTerrainPlan(RandomTerrainView view, int waterSetting = 2)
    {
        _view = view;
        var p = view.Preview;
        _boundsMin = new Vector2((float)p.Bounds[0], (float)p.Bounds[1]); _boundsMax = new Vector2((float)p.Bounds[2], (float)p.Bounds[3]);
        _blocks = view.Marks is { Count: > 0 } m ? m.ToArray() : Array.Empty<PlanBlock>();

        // the groups: blocks of one recipe whose boxes touch (union-find over the pairs; a layout has tens of blocks, the pairs are built once)
        var parent = new int[_blocks.Length];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        for (int i = 0; i < _blocks.Length; i++)
            for (int j = i + 1; j < _blocks.Length; j++)
                if (_blocks[i].Recipe == _blocks[j].Recipe && Near(_blocks[i], _blocks[j])) parent[Find(i)] = Find(j);
        var byRoot = new Dictionary<int, int>();
        var groups = new List<Group>();
        GroupOf = new int[_blocks.Length];
        for (int i = 0; i < _blocks.Length; i++)
        {
            int root = Find(i);
            if (!byRoot.TryGetValue(root, out int g))
            {
                byRoot[root] = g = groups.Count;
                var icon = Icons.Get(TerrainPickCatalog.IconKey(_blocks[i].Recipe));
                groups.Add(new Group { Recipe = _blocks[i].Recipe, Min = _blocks[i].Min, Max = _blocks[i].Max, IconGlyph = icon.Glyph, IconColour = icon.Color, Key = "chip:" + g });
            }
            var grp = groups[g];
            grp.Count++; grp.Houses += _blocks[i].Houses; grp.Members.Add(i);
            grp.Min = Vector2.Min(grp.Min, _blocks[i].Min); grp.Max = Vector2.Max(grp.Max, _blocks[i].Max);
            GroupOf[i] = g;
        }

        // the houses (drawn from their footprints; the storeys of each group's houses come from the buildings that stand inside its blocks)
        _houses = new (Vector2[], uint, Vector2, Vector2)[p.Buildings.Count];
        for (int i = 0; i < _houses.Length; i++)
        {
            var b = p.Buildings[i];
            var mn = new Vector2(float.MaxValue); var mx = new Vector2(float.MinValue);
            foreach (var q in b.Footprint) { mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q); }
            _houses[i] = (b.Footprint, Pack(Building(b.Storeys)), mn, mx);
            var c = (mn + mx) * 0.5f;
            int bi = BlockAt(c);
            if (bi >= 0) { var grp = groups[GroupOf[bi]]; grp.MinStoreys = grp.MinStoreys == 0 ? b.Storeys : Math.Min(grp.MinStoreys, b.Storeys); grp.MaxStoreys = Math.Max(grp.MaxStoreys, b.Storeys); }
        }
        foreach (var g in groups) g.Chip = Hrot.ScenarioEditor.Gizmos.TerrainBlockGizmo.ChipText(g.Recipe, g.Count);
        Groups = groups.ToArray();
        _tips = new string[_blocks.Length][];
        for (int i = 0; i < _blocks.Length; i++)
        {
            var g = Groups[GroupOf[i]];
            string storeys = g.MaxStoreys == 0 ? "" : g.MinStoreys == g.MaxStoreys ? $" · {g.MaxStoreys} storey{(g.MaxStoreys == 1 ? "" : "s")}" : $" · {g.MinStoreys}–{g.MaxStoreys} storeys";
            _tips[i] = new[] { g.Houses > 0 ? $"{g.Chip} — {g.Houses} houses" : g.Chip, $"seed {_blocks[i].Seed}{storeys}", "click a block to see it larger" };
        }

        var areas = new List<(Vector2[], int[], int)>();
        foreach (var a in p.Areas)
        {
            if (a.Ring.Length < 3 || a.Kind == "block") continue;
            areas.Add((a.Ring, PolygonMath.Triangulate(a.Ring), a.Kind == "water" ? 0 : a.Kind == "woods" ? 1 : 2));
        }
        _areas = areas.ToArray();
        _roads = new (Vector2[], Vector4, float)[p.Roads.Count];
        for (int i = 0; i < _roads.Length; i++) { var (rc, rw) = Road(p.Roads[i].Class); _roads[i] = (p.Roads[i].Points, rc, rw); }

        // the relief's node colours: shaded (a light from the north-west), banded by height through the ground ramp
        if (view.Relief is { N: > 1 } rel && rel.Heights.Length >= rel.N * rel.N)
        {
            _n = rel.N;
            _nodes = new uint[(_n + 1) * (_n + 1)];
            float span = Math.Max(1f, rel.Max - rel.Min), cell = (_boundsMax.X - _boundsMin.X) / _n;
            float H(int i, int j) => rel.Heights[Math.Clamp(j, 0, _n - 1) * _n + Math.Clamp(i, 0, _n - 1)];
            for (int j = 0; j <= _n; j++)
                for (int i = 0; i <= _n; i++)
                {
                    float h = (H(i - 1, j - 1) + H(i, j - 1) + H(i - 1, j) + H(i, j)) * 0.25f;
                    float dzdx = (H(i, j) + H(i, j - 1) - H(i - 1, j) - H(i - 1, j - 1)) * 0.5f / cell, dzdy = (H(i, j) + H(i - 1, j) - H(i, j - 1) - H(i - 1, j - 1)) * 0.5f / cell;
                    var nrm = Vector3.Normalize(new Vector3(-dzdx, -dzdy, 1f));
                    float shade = Math.Clamp(Vector3.Dot(nrm, Vector3.Normalize(new Vector3(-0.5f, 0.5f, 0.75f))), 0f, 1f);
                    float t = Math.Clamp((h - rel.Min) / span, 0f, 1f) * (Ramp.Length - 1);
                    int lo = Math.Min((int)t, Ramp.Length - 2);
                    var col = Vector3.Lerp(Ramp[lo], Ramp[lo + 1], t - lo) * (0.82f + 0.32f * shade);
                    _nodes[j * (_n + 1) + i] = Pack(new Vector4(Math.Min(col.X, 1f), Math.Min(col.Y, 1f), Math.Min(col.Z, 1f), 1f));
                }
        }
        else { _n = 0; _nodes = Array.Empty<uint>(); }

        // what was generated: one row per recipe, in the order they first appear
        var rows = new List<Row>();
        if (_blocks.Length > 0)
        {
            var order = new List<string>(); var counts = new Dictionary<string, (int B, int H, double A)>();
            foreach (var b in _blocks)
            {
                if (!counts.TryGetValue(b.Recipe, out var c)) { order.Add(b.Recipe); c = (0, 0, 0); }
                counts[b.Recipe] = (c.B + 1, c.H + b.Houses, c.A + (b.Max.X - b.Min.X) * (b.Max.Y - b.Min.Y) / 10000.0);
            }
            foreach (var r in order) rows.Add(new Row(r, counts[r].B, counts[r].H, counts[r].A));
        }
        else foreach (var (recipe, blocks, houses) in view.ByRecipe) rows.Add(new Row(recipe, blocks, houses, 0));
        Rows = rows;

        double w = (_boundsMax.X - _boundsMin.X) / 1000.0, h2 = (_boundsMax.Y - _boundsMin.Y) / 1000.0;
        var inv = CultureInfo.InvariantCulture;
        string water = waterSetting == 0 ? "" : waterSetting == 1 ? $" · {p.Counts.WaterAreas} pond{(p.Counts.WaterAreas == 1 ? "" : "s")}" : p.Counts.WaterAreas <= 1 ? " · 1 river" : $" · a river and {p.Counts.WaterAreas - 1} lake{(p.Counts.WaterAreas == 2 ? "" : "s")}";
        string relief = view.Relief is { } r2 ? $" · relief {Math.Round(r2.Max - r2.Min):0} m" : "";
        Caption = string.Create(inv, $"{w:0.0} × {h2:0.0} km{relief}{water}");
        ChipMin = new Vector2[Groups.Length]; ChipMax = new Vector2[Groups.Length];
        for (int i = 0; i < Groups.Length; i++) ChipMin[i] = ChipMax[i] = new Vector2(float.NaN);
        BlockMin = new Vector2[_blocks.Length]; BlockMax = new Vector2[_blocks.Length];
    }

    /// <summary>ImGui's packed ABGR — computed here, so the plan can be BUILT (and rail-tested) with no ImGui context.</summary>
    private static uint Pack(Vector4 c) => (uint)(c.W * 255f + 0.5f) << 24 | (uint)(c.Z * 255f + 0.5f) << 16 | (uint)(c.Y * 255f + 0.5f) << 8 | (uint)(c.X * 255f + 0.5f);

    private static bool Near(PlanBlock a, PlanBlock b) => a.Min.X - GroupGap <= b.Max.X && b.Min.X - GroupGap <= a.Max.X && a.Min.Y - GroupGap <= b.Max.Y && b.Min.Y - GroupGap <= a.Max.Y;

    private int BlockAt(Vector2 w)
    {
        for (int i = 0; i < _blocks.Length; i++) if (w.X >= _blocks[i].Min.X && w.X <= _blocks[i].Max.X && w.Y >= _blocks[i].Min.Y && w.Y <= _blocks[i].Max.Y) return i;
        return -1;
    }

    public Group[] Groups { get; }
    public int[] GroupOf { get; }
    public IReadOnlyList<Row> Rows { get; }
    /// <summary>"3.0 × 3.0 km · relief 120 m · 1 river".</summary>
    public string Caption { get; }
    public int BlockCount => _blocks.Length;
    /// <summary>The group the plan is zoomed to (−1: the whole map).</summary>
    public int Zoom { get; set; } = -1;
    /// <summary>Where each group's chip was drawn last frame (NaN until drawn / off the canvas) — a rail's measure.</summary>
    public Vector2[] ChipMin { get; }
    public Vector2[] ChipMax { get; }
    /// <summary>Where each block's outline was drawn last frame (screen px) — a rail measures its dashes.</summary>
    public Vector2[] BlockMin { get; }
    public Vector2[] BlockMax { get; }
    /// <summary>The block under the mouse last frame (−1 none) and its tooltip lines.</summary>
    public int HoveredBlock { get; private set; } = -1;
    public string[]? HoverTip => HoveredBlock >= 0 ? _tips[HoveredBlock] : null;
    public bool HasRelief => _n > 0;

    /// <summary>The tooltip of a block (3 lines).</summary>
    public string[] TipOf(int block) => _tips[block];

    /// <summary>The metres the plan shows: the whole terrain, or the zoomed group's box, padded and widened to the canvas's aspect.</summary>
    private (Vector2 Min, Vector2 Max) View(float aspect, bool cover = false)
    {
        var mn = _boundsMin; var mx = _boundsMax;
        if (Zoom >= 0 && Zoom < Groups.Length)
        {
            var g = Groups[Zoom];
            var size = g.Max - g.Min; float pad = Math.Max(30f, Math.Max(size.X, size.Y) * 0.25f);
            mn = g.Min - new Vector2(pad); mx = g.Max + new Vector2(pad);
        }
        var c = (mn + mx) * 0.5f; var half = (mx - mn) * 0.5f;
        bool widen = half.X / Math.Max(half.Y, 1e-3f) < aspect;
        if (widen != cover) half.X = half.Y * aspect; else half.Y = half.X / aspect;   // fit: show it all (letterboxed) · cover: fill the canvas (the edges cropped)
        return (c - half, c + half);
    }

    /// <summary>
    /// Draws the plan into the canvas <paramref name="min"/>…<paramref name="max"/> (<paramref name="thumbnail"/>: filling it, a preset tile's picture): ground, water, woods, scrub, roads, houses, then every block's dashed outline and each group's chip, then the caption. <paramref name="mouse"/> hit-tests the blocks
    /// (<see cref="HoveredBlock"/>) when <paramref name="hovered"/>. 0 B in a steady frame.
    /// </summary>
    public void Draw(ImDrawListPtr dl, Vector2 min, Vector2 max, float s, Vector2 mouse, bool hovered, bool thumbnail = false)
    {
        float cw = max.X - min.X, ch = max.Y - min.Y;
        var (vmin, vmax) = View(cw / ch, thumbnail);
        float sc = cw / (vmax.X - vmin.X);
        Vector2 P(Vector2 w) => new(min.X + (w.X - vmin.X) * sc, max.Y - (w.Y - vmin.Y) * sc);
        dl.PushClipRect(min, max, true);

        // the ground
        if (_n > 0)
        {
            float cell = (_boundsMax.X - _boundsMin.X) / _n;
            for (int j = 0; j < _n; j++)
            {
                float y0 = _boundsMin.Y + j * cell, y1 = y0 + cell;
                float sy1 = max.Y - (y0 - vmin.Y) * sc, sy0 = max.Y - (y1 - vmin.Y) * sc;
                if (sy1 < min.Y || sy0 > max.Y) continue;
                for (int i = 0; i < _n; i++)
                {
                    float x0 = _boundsMin.X + i * cell;
                    float sx0 = min.X + (x0 - vmin.X) * sc, sx1 = sx0 + cell * sc;
                    if (sx1 < min.X || sx0 > max.X) continue;
                    int a = j * (_n + 1) + i, b = (j + 1) * (_n + 1) + i;
                    dl.AddRectFilledMultiColor(new Vector2(sx0, sy0), new Vector2(sx1, sy1), _nodes[b], _nodes[b + 1], _nodes[a + 1], _nodes[a]);
                }
            }
        }
        else dl.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.2f, 0.27f, 0.17f, 1f)));

        if (!thumbnail) dl.AddRect(P(new Vector2(_boundsMin.X, _boundsMax.Y)), P(new Vector2(_boundsMax.X, _boundsMin.Y)), ImGui.GetColorU32(BORDER), 0f, ImDrawFlags.None, 1f);
        for (int k = 0; k < _areas.Length; k++)
        {
            ref readonly var a = ref _areas[k];
            if (_buf.Length < a.Ring.Length) _buf = new Vector2[Math.Max(a.Ring.Length, _buf.Length * 2)];
            for (int i = 0; i < a.Ring.Length; i++) _buf[i] = P(a.Ring[i]);
            uint col = ImGui.GetColorU32(a.Kind == 0 ? Water : a.Kind == 1 ? Forest : Scrub);
            for (int t = 0; t + 2 < a.Tris.Length; t += 3) dl.AddTriangleFilled(_buf[a.Tris[t]], _buf[a.Tris[t + 1]], _buf[a.Tris[t + 2]], col);
        }
        for (int k = 0; k < _roads.Length; k++)
        {
            ref readonly var r = ref _roads[k];
            uint col = ImGui.GetColorU32(r.Colour); float w = Math.Max(1f, r.Width * 0.5f * s);
            if (!Room(dl)) break;
            for (int i = 0; i + 1 < r.Pts.Length; i++) dl.AddLine(P(r.Pts[i]), P(r.Pts[i + 1]), col, w);
        }
        for (int k = 0; k < _houses.Length; k++)
        {
            if (!Room(dl)) break;
            ref readonly var h = ref _houses[k];
            var a = P(new Vector2(h.Min.X, h.Max.Y)); var b = P(new Vector2(h.Max.X, h.Min.Y));
            if (b.X < min.X || a.X > max.X || b.Y < min.Y || a.Y > max.Y) continue;
            if (b.X - a.X < 2.5f * s && b.Y - a.Y < 2.5f * s) { var c = (a + b) * 0.5f; dl.AddRectFilled(c - new Vector2(1.25f * s), c + new Vector2(1.25f * s), h.Colour); continue; }   // a house too small to see is still a mark
            if (_buf.Length < h.Ring.Length) _buf = new Vector2[Math.Max(h.Ring.Length, _buf.Length * 2)];
            for (int i = 0; i < h.Ring.Length; i++) _buf[i] = P(h.Ring[i]);
            dl.AddConvexPolyFilled(ref _buf[0], h.Ring.Length, h.Colour);
        }

        // the blocks: a dashed outline each, the hovered one lit (a preset's picture draws the ground only: no outlines, chips or caption)
        HoveredBlock = -1;
        if (thumbnail) { dl.PopClipRect(); return; }
        float margin = Hrot.ScenarioEditor.Gizmos.TerrainBlockGizmo.Margin;
        var outline = Of(Hrot.ScenarioEditor.Gizmos.TerrainBlockGizmo.OutlineColour);
        if (hovered)
            for (int i = _blocks.Length - 1; i >= 0; i--)
            {
                var a = P(new Vector2(_blocks[i].Min.X - margin, _blocks[i].Max.Y + margin)); var b = P(new Vector2(_blocks[i].Max.X + margin, _blocks[i].Min.Y - margin));
                if (mouse.X >= a.X && mouse.X <= b.X && mouse.Y >= a.Y && mouse.Y <= b.Y) { HoveredBlock = i; break; }
            }
        int hotGroup = HoveredBlock >= 0 ? GroupOf[HoveredBlock] : Zoom;
        for (int i = 0; i < _blocks.Length; i++)
        {
            bool hot = GroupOf[i] == hotGroup;
            var a = P(new Vector2(_blocks[i].Min.X - margin, _blocks[i].Max.Y + margin)); var b = P(new Vector2(_blocks[i].Max.X + margin, _blocks[i].Min.Y - margin));
            if (b.X < min.X || a.X > max.X || b.Y < min.Y || a.Y > max.Y) continue;
            BlockMin[i] = a; BlockMax[i] = b;
            var col = hot ? ACC : outline;
            if (hot) dl.AddRectFilled(a, b, ImGui.GetColorU32(ACC with { W = 0.10f }), 3f * s);
            Dashed(dl, new Vector2(a.X, a.Y), new Vector2(b.X, a.Y), col, s); Dashed(dl, new Vector2(b.X, a.Y), new Vector2(b.X, b.Y), col, s);
            Dashed(dl, new Vector2(b.X, b.Y), new Vector2(a.X, b.Y), col, s); Dashed(dl, new Vector2(a.X, b.Y), new Vector2(a.X, a.Y), col, s);
        }
        // one chip per group, on the corner of its box (the mockup's pill: the recipe's icon + its name)
        for (int g = 0; g < Groups.Length; g++)
        {
            var grp = Groups[g];
            var corner = P(new Vector2(grp.Min.X - margin, grp.Max.Y + margin));
            var ts = ImGui.CalcTextSize(grp.Chip);
            float w = ts.X + 26f * s, hgt = 16f * s;
            var p0 = new Vector2(corner.X, corner.Y - hgt - 1f * s); var p1 = new Vector2(p0.X + w, p0.Y + hgt);
            // a chip that would sit on an earlier one steps up above it (stacked, never over-printed)
            for (int guard = 0; guard < Groups.Length; guard++)
            {
                bool clash = false;
                for (int o = 0; o < g; o++)
                    if (!float.IsNaN(ChipMin[o].X) && p0.X < ChipMax[o].X && p1.X > ChipMin[o].X && p0.Y < ChipMax[o].Y && p1.Y > ChipMin[o].Y) { p0.Y = ChipMin[o].Y - hgt - 1f * s; p1.Y = p0.Y + hgt; clash = true; }
                if (!clash) break;
            }
            if (p0.Y < min.Y + 2f * s && p1.Y > min.Y) { p0.Y = min.Y + 2f * s; p1.Y = p0.Y + hgt; }   // never above the canvas: a crowded corner over-prints rather than vanishes
            if (p1.X < min.X || p0.X > max.X || p1.Y < min.Y || p0.Y > max.Y) { ChipMin[g] = ChipMax[g] = new Vector2(float.NaN); continue; }
            bool hot = g == hotGroup;
            var edge = hot ? ACC : outline;
            dl.AddRectFilled(p0, p1, ImGui.GetColorU32(POPUP with { W = 0.92f }), 8f * s);
            dl.AddRect(p0, p1, ImGui.GetColorU32(edge), 8f * s);
            dl.AddText(new Vector2(p0.X + 6f * s, p0.Y + (hgt - ImGui.GetFontSize()) * 0.5f), ImGui.GetColorU32(grp.IconColour), grp.IconGlyph);
            dl.AddText(new Vector2(p0.X + 21f * s, p0.Y + (hgt - ts.Y) * 0.5f), ImGui.GetColorU32(TXT), grp.Chip);
            ChipMin[g] = p0; ChipMax[g] = p1;
        }

        // the caption, bottom-left (the mockup's)
        var cs = ImGui.CalcTextSize(Caption);
        var c0 = new Vector2(min.X + 10f * s, max.Y - 28f * s); var c1 = new Vector2(c0.X + cs.X + 16f * s, c0.Y + 20f * s);
        dl.AddRectFilled(c0, c1, ImGui.GetColorU32(POPUP with { W = 0.85f }), 3f * s);
        dl.AddText(new Vector2(c0.X + 8f * s, c0.Y + (20f * s - cs.Y) * 0.5f), ImGui.GetColorU32(SUB), Caption);
        if (Zoom >= 0) { const string back = "click an empty spot to see the whole map"; var bs = ImGui.CalcTextSize(back); dl.AddText(new Vector2(max.X - bs.X - 10f * s, max.Y - 24f * s), ImGui.GetColorU32(NOTE), back); }
        dl.PopClipRect();
    }

    /// <summary>The click on the plan: a block zooms to its group, anywhere else zooms back. Returns true when it changed the zoom.</summary>
    public bool Click()
    {
        int want = HoveredBlock >= 0 ? GroupOf[HoveredBlock] : -1;
        if (want == Zoom) return false;
        Zoom = want; return true;
    }

    /// <summary>ImGui's draw lists index with 16 bits: past ~60 000 vertices a frame would trip its assert, so the small things (roads, houses) stop first — the shading, the water and the blocks are already down.</summary>
    private static bool Room(ImDrawListPtr dl) => dl.VtxBuffer.Size < 58000;

    private static void Dashed(ImDrawListPtr dl, Vector2 a, Vector2 b, Vector4 col, float s)
    {
        float len = Vector2.Distance(a, b); if (len < 1f) return;
        float dash = 6f * s, gap = 4f * s; var d = (b - a) / len; uint c = ImGui.GetColorU32(col);
        for (float t = 0; t < len; t += dash + gap) dl.AddLine(a + d * t, a + d * Math.Min(t + dash, len), c, 1.2f * s);
    }
}
