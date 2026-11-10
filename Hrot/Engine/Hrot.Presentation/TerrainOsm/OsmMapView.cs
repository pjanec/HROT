using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Terrain;
using Hrot.Terrain.InputSet;
using Hrot.Terrain.Osm;
using ImGuiNET;
using static Hrot.UI.Common.TerrainOsm.OsmWizardStyle;

namespace Hrot.UI.Common.TerrainOsm;

/// <summary>The ten preview layers (C-11), in the mockup's order.</summary>
public enum OsmLayer { Houses1, Houses2to4, Houses5to9, Solid, Woods, Scrub, Water, Walls, Trees, Roads }

/// <summary>
/// ⭐⭐ CE-4375 (OSM §6a — the wizard's OWN map, drawn INSIDE its ImGui window; ⛔ not the scenario <c>MapCanvas</c>) — <b>the one map canvas of Area and Preview</b> (C-3):
/// OSM tiles laid out by web-Mercator zoom / pan, the zoom / Fit buttons (C-5), the attribution strip (dimmed in Preview), the drag box with 8 handles and its size chip (C-7),
/// and in Preview the fetched data drawn over the DIMMED tiles, clipped to the box (C-10), the Layers panel (C-11) and the building hover (C-12). Geometry is projected local
/// metres → UTM → lat/lon ONCE per preview (<c>Fdp.Modules.Geographic.Transforms.Utm.ToGeodetic</c>), then to the screen per frame into reused buffers (R-2084).
/// </summary>
public sealed class OsmMapView
{
    // the mockup's canvas: 830 × 476 at (10, 64) of a 1200 × 600 window (scaled by the font)
    public double Lat = 50.0, Lon = 15.0, Zoom = 13;
    public readonly bool[] Layers = { true, true, true, true, true, true, true, true, true, true };

    /// <summary>The canvas rectangle drawn last frame (screen pixels) — for the window's layout and the rails.</summary>
    public Vector2 CanvasMin { get; private set; }
    public Vector2 CanvasMax { get; private set; }
    /// <summary>The box's screen rectangle last frame (null = no box).</summary>
    public (Vector2 Min, Vector2 Max)? BoxRect { get; private set; }
    /// <summary>The size chip's rectangle and colour last frame (C-7).</summary>
    public (Vector2 Min, Vector2 Max, Vector4 Border)? SizeChip { get; private set; }
    /// <summary>The layers panel's rectangle last frame (C-11).</summary>
    public (Vector2 Min, Vector2 Max)? LayersRect { get; private set; }
    /// <summary>The zoom / Fit buttons' rectangles last frame (C-5).</summary>
    public readonly (Vector2 Min, Vector2 Max)[] ZoomButtons = new (Vector2, Vector2)[3];
    /// <summary>The attribution strip last frame (C-5).</summary>
    public (Vector2 Min, Vector2 Max)? AttributionRect { get; private set; }
    /// <summary>The building under the mouse in Preview (C-12), or null.</summary>
    public PreviewBuilding? Hovered { get; private set; }
    /// <summary>⭐ CE-4382 — the tree dots drawn last frame (a rail).</summary>
    public int TreesDrawn { get; private set; }
    /// <summary>Test hook: the mouse position the hover test uses (null = the real mouse).</summary>
    public Vector2? MouseOverride;

    private enum Drag { None, Draw, Move, Handle, Pan }
    private Drag _drag;
    private int _handle;
    private Vector2 _dragStart;
    private GeoBox _boxAtStart;
    private double _panLat, _panLon;

    // the preview, projected once
    private OsmPreview? _projectedFor;
    private readonly List<(double[] LatLon, int[] Tris, PreviewBuilding B)> _buildings = new();
    private readonly List<(double[] LatLon, int[] Tris, string Kind)> _areas = new();
    private readonly List<(double[] LatLon, string Kind)> _lines = new();
    private readonly List<(double[] LatLon, string Cls)> _roads = new();
    private double[] _trees = Array.Empty<double>();   // ⭐ CE-4382 — every tree point, lat/lon pairs
    private Vector2[] _buf = new Vector2[256];

    // ── the maths ──────────────────────────────────────────────────────────────────────────

    public Vector2 ToScreen(double lat, double lon)
    {
        var (cx, cy) = WebMercator.ToPixel(Lat, Lon, Zoom);
        var (x, y) = WebMercator.ToPixel(lat, lon, Zoom);
        var mid = (CanvasMin + CanvasMax) * 0.5f;
        return new Vector2((float)(mid.X + (x - cx)), (float)(mid.Y + (y - cy)));
    }

    public (double Lat, double Lon) ToGeo(Vector2 screen)
    {
        var (cx, cy) = WebMercator.ToPixel(Lat, Lon, Zoom);
        var mid = (CanvasMin + CanvasMax) * 0.5f;
        return WebMercator.ToGeo(cx + (screen.X - mid.X), cy + (screen.Y - mid.Y), Zoom);
    }

    /// <summary>Fit: the box fills ~70 % of the canvas.</summary>
    public void Fit(GeoBox box)
    {
        Lat = (box.South + box.North) * 0.5; Lon = (box.West + box.East) * 0.5;
        var size = CanvasMax - CanvasMin;
        if (size.X < 10) size = new Vector2(830, 476);
        for (double z = 18; z >= 2; z -= 0.05)
        {
            var (x0, y0) = WebMercator.ToPixel(box.North, box.West, z);
            var (x1, y1) = WebMercator.ToPixel(box.South, box.East, z);
            if (x1 - x0 <= size.X * 0.7 && y1 - y0 <= size.Y * 0.7) { Zoom = z; return; }
        }
        Zoom = 2;
    }

    // ── drawing ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws the canvas at the cursor (<paramref name="size"/>), and handles its input. <paramref name="editBox"/> = the Area step (the box can be drawn / moved / resized);
    /// <paramref name="preview"/> = the Preview step's data (tiles dimmed, data clipped to the box, layers, hover).
    /// </summary>
    public void Draw(OsmWizardModel model, OsmTileTextures? tiles, Vector2 size, bool editBox, OsmPreview? preview, float s, Action? drawOverlay = null)
    {
        var dl = ImGui.GetWindowDrawList();
        CanvasMin = ImGui.GetCursorScreenPos();
        CanvasMax = CanvasMin + size;
        ImGui.InvisibleButton("##osm_map", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle);
        bool hovered = ImGui.IsItemHovered(), active = ImGui.IsItemActive();
        var afterCanvas = ImGui.GetCursorScreenPos();

        dl.PushClipRect(CanvasMin, CanvasMax, true);
        dl.AddRectFilled(CanvasMin, CanvasMax, ImGui.GetColorU32(MapGround));
        DrawTiles(dl, tiles);
        if (preview != null) dl.AddRectFilled(CanvasMin, CanvasMax, ImGui.GetColorU32(new Vector4(0.06f, 0.06f, 0.06f, 0.72f)));   // dimmed under the data (C-10)
        BoxRect = null; SizeChip = null; Hovered = null;
        if (model.Box is { } box)
        {
            var a = ToScreen(box.North, box.West); var b = ToScreen(box.South, box.East);
            BoxRect = (a, b);
            if (preview != null)
            {
                dl.AddRectFilled(a, b, ImGui.GetColorU32(new Vector4(0.06f, 0.06f, 0.06f, 0.35f)));
                DrawPreview(dl, preview, a, b, s);
                DashedRect(dl, a, b, ImGui.GetColorU32(ACC), 1.5f * s, 5 * s, 4 * s);
            }
            else
            {
                dl.AddRectFilled(a, b, ImGui.GetColorU32(ACC with { W = 0.10f }));
                dl.AddRect(a, b, ImGui.GetColorU32(ACC), 0, ImDrawFlags.None, 2f * s);
                HandleCount = 0;
                foreach (var h in Handles(a, b))
                {
                    HandleCount++;
                    dl.AddRectFilled(h - new Vector2(4 * s), h + new Vector2(4 * s), ImGui.GetColorU32(WHITE));
                    dl.AddRect(h - new Vector2(4 * s), h + new Vector2(4 * s), ImGui.GetColorU32(ACC), 0, ImDrawFlags.None, 1.5f * s);
                }
                // the size chip above the top edge: RED above the limit (C-7)
                string text = WebMercator.SizeText(model.BoxWidthM, model.BoxHeightM);
                var border = model.BoxTooLarge ? RED : ACC;
                var ts = ImGui.CalcTextSize(text);
                var chipMin = new Vector2((a.X + b.X) * 0.5f - Math.Max(62 * s, ts.X * 0.5f + 12 * s), a.Y - 26 * s);
                var chipMax = new Vector2((a.X + b.X) * 0.5f + Math.Max(62 * s, ts.X * 0.5f + 12 * s), a.Y - 6 * s);
                dl.AddRectFilled(chipMin, chipMax, ImGui.GetColorU32(POPUP), 10 * s);
                dl.AddRect(chipMin, chipMax, ImGui.GetColorU32(border), 10 * s, ImDrawFlags.None, model.BoxTooLarge ? 2f : 1f);
                var tp = new Vector2((chipMin.X + chipMax.X - ts.X) * 0.5f, (chipMin.Y + chipMax.Y - ts.Y) * 0.5f);
                dl.AddText(tp, ImGui.GetColorU32(model.BoxTooLarge ? RED : TXT), text);
                dl.AddText(tp + new Vector2(0.6f, 0), ImGui.GetColorU32(model.BoxTooLarge ? RED : TXT), text);   // bold, as the mockup
                SizeChip = (chipMin, chipMax, border);
            }
        }
        dl.PopClipRect();

        // the attribution strip, bottom-right of the canvas (dimmed in Preview — C-5)
        string attribution = tiles?.Attribution ?? "© OpenStreetMap contributors";
        var at = ImGui.CalcTextSize(attribution);
        var aMin = new Vector2(CanvasMax.X - at.X - 12 * s, CanvasMax.Y - 18 * s);
        dl.AddRectFilled(aMin, CanvasMax, ImGui.GetColorU32(WHITE with { W = preview == null ? 0.75f : 0.15f }));
        dl.AddText(new Vector2(aMin.X + 6 * s, aMin.Y + (18 * s - at.Y) * 0.5f), ImGui.GetColorU32(preview == null ? new Vector4(0.2f, 0.2f, 0.2f, 1) : NOTE), attribution);
        AttributionRect = (aMin, CanvasMax);

        // the zoom buttons + Fit, top-left (C-5)
        var save = ImGui.GetCursorScreenPos();
        string[] glyphs = { PLUS, MINUS, CROSSHAIRS };
        for (int i = 0; i < 3; i++)
        {
            var bmin = CanvasMin + new Vector2(10 * s, (10 + i * 28) * s);
            ImGui.SetCursorScreenPos(bmin);
            ImGui.PushStyleColor(ImGuiCol.Button, POPUP with { W = 0.9f });
            ImGui.PushStyleColor(ImGuiCol.Border, BORDER);
            ImGui.PushStyleColor(ImGuiCol.Text, SUB);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3 * s);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            bool clicked = ImGui.Button(glyphs[i] + "##osm_zoom" + i, new Vector2(24 * s, 24 * s));
            ImGui.PopStyleVar(2); ImGui.PopStyleColor(3);
            ZoomButtons[i] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            if (clicked)
            {
                if (i == 0) Zoom = Math.Min(19, Zoom + 1);
                else if (i == 1) Zoom = Math.Max(2, Zoom - 1);
                else if (model.Box is { } fb) Fit(fb);
            }
        }

        drawOverlay?.Invoke();   // the search field (Area) / the layers panel (Preview) — drawn over the canvas
        if (preview != null) DrawLayersPanel(dl, s);
        ImGui.SetCursorScreenPos(afterCanvas);

        if (preview != null) HoverBuilding(dl, preview, s);
        HandleInput(model, hovered, active, editBox);
    }

    private void DrawTiles(ImDrawListPtr dl, OsmTileTextures? tiles)
    {
        int z = Math.Clamp((int)Math.Floor(Zoom), 0, 19);
        double scale = Math.Pow(2, Zoom - z);   // a fractional zoom stretches the integer tiles
        float tile = (float)(WebMercator.TileSize * scale);
        var (cx, cy) = WebMercator.ToPixel(Lat, Lon, Zoom);
        var mid = (CanvasMin + CanvasMax) * 0.5f;
        double left = cx - (mid.X - CanvasMin.X), top = cy - (mid.Y - CanvasMin.Y);
        int tx0 = (int)Math.Floor(left / tile), ty0 = (int)Math.Floor(top / tile);
        int tx1 = (int)Math.Floor((left + (CanvasMax.X - CanvasMin.X)) / tile), ty1 = (int)Math.Floor((top + (CanvasMax.Y - CanvasMin.Y)) / tile);
        int n = 1 << z;
        uint grid = ImGui.GetColorU32(MapGrid);
        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++)
            {
                var p0 = new Vector2((float)(CanvasMin.X + tx * tile - left), (float)(CanvasMin.Y + ty * tile - top));
                var p1 = p0 + new Vector2(tile, tile);
                if (ty >= 0 && ty < n && tiles != null && tiles.TryGet(z, ((tx % n) + n) % n, ty, out var tex))
                    dl.AddImage((IntPtr)tex.Id, p0, p1);
                dl.AddRect(p0, p1, grid, 0, ImDrawFlags.None, 0.6f);
            }
    }

    private readonly Vector2[] _handles = new Vector2[8];

    /// <summary>The 8 handles of the box a..b (corners, then edge middles) into a reused buffer (R-2084: no allocation per frame).</summary>
    private Vector2[] Handles(Vector2 a, Vector2 b)
    {
        _handles[0] = a; _handles[1] = new Vector2(b.X, a.Y); _handles[2] = new Vector2(a.X, b.Y); _handles[3] = b;
        _handles[4] = new Vector2((a.X + b.X) * 0.5f, a.Y); _handles[5] = new Vector2((a.X + b.X) * 0.5f, b.Y);
        _handles[6] = new Vector2(a.X, (a.Y + b.Y) * 0.5f); _handles[7] = new Vector2(b.X, (a.Y + b.Y) * 0.5f);
        return _handles;
    }

    /// <summary>The handles drawn last frame (a rail: there are 8, white).</summary>
    public int HandleCount { get; private set; }

    private void HandleInput(OsmWizardModel model, bool hovered, bool active, bool editBox)
    {
        var io = ImGui.GetIO();
        var mouse = io.MousePos;
        if (hovered && io.MouseWheel != 0)
        {
            var before = ToGeo(mouse);
            Zoom = Math.Clamp(Zoom + io.MouseWheel * 0.5, 2, 19);
            var after = ToGeo(mouse);   // keep the point under the mouse still
            Lat += before.Lat - after.Lat; Lon += before.Lon - after.Lon;
        }
        // pan: the right or middle button
        if (hovered && (ImGui.IsMouseClicked(ImGuiMouseButton.Right) || ImGui.IsMouseClicked(ImGuiMouseButton.Middle)))
        { _drag = Drag.Pan; _dragStart = mouse; _panLat = Lat; _panLon = Lon; }
        if (_drag == Drag.Pan)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Right) && !ImGui.IsMouseDown(ImGuiMouseButton.Middle)) { _drag = Drag.None; return; }
            var (px, py) = WebMercator.ToPixel(_panLat, _panLon, Zoom);
            var d = mouse - _dragStart;
            (Lat, Lon) = WebMercator.ToGeo(px - d.X, py - d.Y, Zoom);
            return;
        }
        if (!editBox) return;
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _dragStart = mouse; _drag = Drag.Draw;
            if (BoxRect is { } r && model.Box is { } box)
            {
                _boxAtStart = box;
                int i = 0;
                foreach (var h in Handles(r.Min, r.Max)) { if (Vector2.Distance(h, mouse) <= 7) { _drag = Drag.Handle; _handle = i; break; } i++; }
                if (_drag == Drag.Draw && mouse.X > r.Min.X && mouse.X < r.Max.X && mouse.Y > r.Min.Y && mouse.Y < r.Max.Y) _drag = Drag.Move;
            }
        }
        if (_drag == Drag.None) return;
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) { _drag = Drag.None; return; }
        if (!active && _drag == Drag.Draw) return;
        var g0 = ToGeo(_dragStart); var g1 = ToGeo(mouse);
        switch (_drag)
        {
            case Drag.Draw:
                if (Vector2.Distance(_dragStart, mouse) > 4) model.SetBox(new GeoBox(g1.Lat, g0.Lon, g0.Lat, g1.Lon));
                break;
            case Drag.Move:
                double dLat = g1.Lat - g0.Lat, dLon = g1.Lon - g0.Lon;
                model.SetBox(new GeoBox(_boxAtStart.South + dLat, _boxAtStart.West + dLon, _boxAtStart.North + dLat, _boxAtStart.East + dLon));
                break;
            case Drag.Handle:
                var b = _boxAtStart;
                double n = b.North, sth = b.South, w = b.West, e = b.East;
                switch (_handle)
                {
                    case 0: n = g1.Lat; w = g1.Lon; break;
                    case 1: n = g1.Lat; e = g1.Lon; break;
                    case 2: sth = g1.Lat; w = g1.Lon; break;
                    case 3: sth = g1.Lat; e = g1.Lon; break;
                    case 4: n = g1.Lat; break;
                    case 5: sth = g1.Lat; break;
                    case 6: w = g1.Lon; break;
                    case 7: e = g1.Lon; break;
                }
                model.SetBox(new GeoBox(sth, w, n, e));
                break;
        }
    }

    // ── the preview (C-10 … C-12) ─────────────────────────────────────────────────────────

    private void Project(OsmPreview p)
    {
        if (ReferenceEquals(p, _projectedFor)) return;
        _projectedFor = p;
        _buildings.Clear(); _areas.Clear(); _lines.Clear(); _roads.Clear();
        foreach (var b in p.Buildings) _buildings.Add((LatLon(p.Frame, b.Footprint), Tris(b.Footprint), b));
        foreach (var a in p.Areas) _areas.Add((LatLon(p.Frame, a.Ring), Tris(a.Ring), a.Kind));
        foreach (var l in p.Lines) _lines.Add((LatLon(p.Frame, l.Points), l.Kind));
        foreach (var r in p.Roads) _roads.Add((LatLon(p.Frame, r.Points), r.Class));
        _trees = p.Trees.Count > 0 ? LatLon(p.Frame, System.Linq.Enumerable.ToArray(p.Trees)) : Array.Empty<double>();
    }

    private static double[] LatLon(UtmFrame f, Vector2[] pts)
    {
        var o = new double[pts.Length * 2];
        for (int i = 0; i < pts.Length; i++)
        {
            var (lat, lon) = Fdp.Modules.Geographic.Transforms.Utm.ToGeodetic(f.E0 + pts[i].X, f.N0 + pts[i].Y, f.Zone, f.South);
            o[i * 2] = lat; o[i * 2 + 1] = lon;
        }
        return o;
    }

    private static int[] Tris(Vector2[] pts) => PolygonMath.Triangulate(pts);

    private int ToBuffer(double[] ll)
    {
        int n = ll.Length / 2;
        if (_buf.Length < n) _buf = new Vector2[Math.Max(n, _buf.Length * 2)];
        for (int i = 0; i < n; i++) _buf[i] = ToScreen(ll[i * 2], ll[i * 2 + 1]);
        return n;
    }

    private void Fill(ImDrawListPtr dl, double[] ll, int[] tris, uint fill, uint outline, float width)
    {
        int n = ToBuffer(ll);
        for (int t = 0; t + 2 < tris.Length; t += 3) dl.AddTriangleFilled(_buf[tris[t]], _buf[tris[t + 1]], _buf[tris[t + 2]], fill);
        if (width > 0) for (int i = 0; i < n; i++) dl.AddLine(_buf[i], _buf[(i + 1) % n], outline, width);
    }

    private void Polyline(ImDrawListPtr dl, double[] ll, uint col, float width, float dash = 0, float gap = 0)
    {
        int n = ToBuffer(ll);
        for (int i = 0; i + 1 < n; i++)
        {
            if (dash <= 0) { dl.AddLine(_buf[i], _buf[i + 1], col, width); continue; }
            var a = _buf[i]; var d = _buf[i + 1] - a; float len = d.Length(); if (len < 0.01f) continue; var u = d / len;
            for (float t = 0; t < len; t += dash + gap) dl.AddLine(a + u * t, a + u * Math.Min(t + dash, len), col, width);
        }
    }

    public static OsmLayer LayerOf(PreviewBuilding b) => !b.Enterable ? OsmLayer.Solid : b.Storeys <= 1 ? OsmLayer.Houses1 : b.Storeys <= 4 ? OsmLayer.Houses2to4 : OsmLayer.Houses5to9;

    private void DrawPreview(ImDrawListPtr dl, OsmPreview p, Vector2 a, Vector2 b, float s)
    {
        Project(p);
        dl.PushClipRect(Vector2.Max(a, CanvasMin), Vector2.Min(b, CanvasMax), true);   // only inside the box (C-10)
        foreach (var (ll, tris, kind) in _areas)
        {
            var layer = kind == "water" ? OsmLayer.Water : kind == "scrub" ? OsmLayer.Scrub : OsmLayer.Woods;
            if (!Layers[(int)layer]) continue;
            var col = layer == OsmLayer.Water ? Water : layer == OsmLayer.Scrub ? Scrub with { W = 0.45f } : Forest;
            Fill(dl, ll, tris, ImGui.GetColorU32(col), 0, 0);
        }
        if (Layers[(int)OsmLayer.Roads])
            foreach (var (ll, cls) in _roads)
            {
                var (col, w) = Road(cls);
                Polyline(dl, ll, ImGui.GetColorU32(col), w * s, cls == "rail" ? 6 * s : 0, cls == "rail" ? 4 * s : 0);
            }
        foreach (var (ll, kind) in _lines)
        {
            if (kind == "tree_row")
            {
                if (!Layers[(int)OsmLayer.Trees] || _trees.Length > 0) continue;   // the row's trees are in the preview's Trees (drawn below)
                int n = ToBuffer(ll);
                for (int i = 0; i < n; i++) { dl.AddCircleFilled(_buf[i], 2.6f * s, ImGui.GetColorU32(Forest with { W = 1 })); dl.AddCircle(_buf[i], 2.6f * s, ImGui.GetColorU32(new Vector4(0.5f, 0.82f, 0.54f, 1)), 0, 0.6f); }
                continue;
            }
            if (!Layers[(int)OsmLayer.Walls]) continue;
            Polyline(dl, ll, ImGui.GetColorU32(Wall), 1.6f * s);
        }
        TreesDrawn = 0;
        if (Layers[(int)OsmLayer.Trees] && _trees.Length > 0)   // ⭐ CE-4382 — lone trees and the rows' trees, one dot each (the mockup's dots)
        {
            uint fill = ImGui.GetColorU32(Forest with { W = 1 }), rim = ImGui.GetColorU32(new Vector4(0.5f, 0.82f, 0.54f, 1));
            int n = ToBuffer(_trees);
            for (int i = 0; i < n; i++) { dl.AddCircleFilled(_buf[i], 2.6f * s, fill); dl.AddCircle(_buf[i], 2.6f * s, rim, 0, 0.6f); }
            TreesDrawn = n;
        }
        uint outline = ImGui.GetColorU32(Outline);
        foreach (var (ll, tris, bld) in _buildings)
        {
            var layer = LayerOf(bld);
            if (!Layers[(int)layer]) continue;
            Fill(dl, ll, tris, ImGui.GetColorU32(layer == OsmLayer.Solid ? Solid with { W = 0.9f } : Building(bld.Storeys)), outline, 0.8f);
        }
        dl.PopClipRect();
    }

    private static readonly (OsmLayer Layer, string Label)[] LayerRows =
    {
        (OsmLayer.Houses1, "Houses · 1 storey"), (OsmLayer.Houses2to4, "Houses · 2–4"), (OsmLayer.Houses5to9, "Houses · 5–9"), (OsmLayer.Solid, "Garages, sheds (solid)"),
        (OsmLayer.Woods, "Woods"), (OsmLayer.Scrub, "Scrub"), (OsmLayer.Water, "Water"), (OsmLayer.Walls, "Walls, fences"), (OsmLayer.Trees, "Lone trees, tree rows"),
        (OsmLayer.Roads, "Roads and rail"),
    };

    /// <summary>The swatch colour of a layer row (C-11): the colour the layer is drawn in.</summary>
    public static Vector4 SwatchOf(OsmLayer l) => l switch
    {
        OsmLayer.Houses1 => Building(1), OsmLayer.Houses2to4 => Building(3), OsmLayer.Houses5to9 => Building(6), OsmLayer.Solid => Solid,
        OsmLayer.Woods => Forest with { W = 1 }, OsmLayer.Scrub => Scrub, OsmLayer.Water => Water with { W = 1 }, OsmLayer.Walls => Wall,
        OsmLayer.Trees => Forest with { W = 1 }, _ => Road("primary").Colour,
    };

    /// <summary>The swatch rectangles drawn last frame, per layer (a rail).</summary>
    public readonly (Vector2 Min, Vector2 Max)[] SwatchRects = new (Vector2, Vector2)[10];

    private void DrawLayersPanel(ImDrawListPtr dl, float s)
    {
        var min = new Vector2(CanvasMax.X - 214 * s, CanvasMin.Y + 10 * s);
        var max = min + new Vector2(204 * s, 238 * s);
        dl.AddRectFilled(min, max, ImGui.GetColorU32(POPUP with { W = 0.94f }), 4 * s);
        dl.AddRect(min, max, ImGui.GetColorU32(BORDER), 4 * s);
        LayersRect = (min, max);
        ImGui.SetCursorScreenPos(min + new Vector2(10 * s, 6 * s));
        ImGui.TextUnformatted("Layers");
        dl.AddText(ImGui.GetItemRectMin() + new Vector2(0.6f, 0), ImGui.GetColorU32(TXT), "Layers");
        for (int i = 0; i < LayerRows.Length; i++)
        {
            var (layer, label) = LayerRows[i];
            var rowMin = min + new Vector2(8 * s, (28 + i * 20.5f) * s - 3 * s);
            ImGui.SetCursorScreenPos(rowMin);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2 * s, 1 * s));
            ImGui.Checkbox("##osm_layer" + i, ref Layers[(int)layer]);
            ImGui.PopStyleVar();
            var cb = ImGui.GetItemRectMax();
            var sw0 = new Vector2(cb.X + 6 * s, rowMin.Y + 4 * s);
            uint col = ImGui.GetColorU32(SwatchOf(layer));
            if (layer is OsmLayer.Walls or OsmLayer.Roads) { dl.AddLine(sw0 + new Vector2(0, 5 * s), sw0 + new Vector2(20 * s, 5 * s), col, 3 * s); SwatchRects[i] = (sw0, sw0 + new Vector2(20 * s, 10 * s)); }
            else if (layer == OsmLayer.Trees) { dl.AddCircleFilled(sw0 + new Vector2(10 * s, 5 * s), 3.5f * s, col); SwatchRects[i] = (sw0 + new Vector2(6.5f * s, 1.5f * s), sw0 + new Vector2(13.5f * s, 8.5f * s)); }
            else { dl.AddRectFilled(sw0, sw0 + new Vector2(16 * s, 11 * s), col); dl.AddRect(sw0, sw0 + new Vector2(16 * s, 11 * s), ImGui.GetColorU32(Outline)); SwatchRects[i] = (sw0, sw0 + new Vector2(16 * s, 11 * s)); }
            ImGui.SetCursorScreenPos(new Vector2(sw0.X + 26 * s, rowMin.Y + 1 * s));
            ImGui.PushStyleColor(ImGuiCol.Text, SUB);
            ImGui.TextUnformatted(label);
            ImGui.PopStyleColor();
        }
    }

    private PreviewBuilding? _tipFor;
    private IReadOnlyList<string> _tipLines = Array.Empty<string>();

    private void HoverBuilding(ImDrawListPtr dl, OsmPreview p, float s)
    {
        var m = MouseOverride ?? ImGui.GetIO().MousePos;
        if (m.X < CanvasMin.X || m.Y < CanvasMin.Y || m.X > CanvasMax.X || m.Y > CanvasMax.Y) return;
        if (LayersRect is { } lr && m.X >= lr.Min.X && m.Y >= lr.Min.Y && m.X <= lr.Max.X && m.Y <= lr.Max.Y) return;
        if (BoxRect is { } br && (m.X < br.Min.X || m.Y < br.Min.Y || m.X > br.Max.X || m.Y > br.Max.Y)) return;
        for (int k = _buildings.Count - 1; k >= 0; k--)
        {
            var (ll, _, bld) = _buildings[k];
            if (!Layers[(int)LayerOf(bld)]) continue;
            int n = ToBuffer(ll);
            if (!Inside(_buf, n, m)) continue;
            Hovered = bld;
            for (int i = 0; i < n; i++) dl.AddLine(_buf[i], _buf[(i + 1) % n], ImGui.GetColorU32(WHITE), 2f * s);
            if (!ReferenceEquals(bld, _tipFor)) { _tipFor = bld; _tipLines = TooltipLines(bld); }   // built once per building hovered, not per frame
            ImGui.SetNextWindowPos(m + new Vector2(16, 12) * s);   // beside the hover point (the mockup's tooltip; also right when the point is not the OS mouse)
            ImGui.BeginTooltip();
            for (int i = 0; i < _tipLines.Count; i++) ImGui.TextUnformatted(_tipLines[i]);
            ImGui.EndTooltip();
            return;
        }
    }

    /// <summary>
    /// What a hovered building becomes (C-12, ⭐ CE-4382 — the mockup's four lines): <i>way 112233445 · house</i> / <i>2 storeys (tagged) · pitched roof (gabled)</i> /
    /// <i>1 door (road-facing rule) · 9 windows</i> / <i>shares 1 wall with way …</i> (only when it shares one). A solid building says so on the first line and has no openings line.
    /// </summary>
    public static IReadOnlyList<string> TooltipLines(PreviewBuilding b)
    {
        var l = new List<string>(4);
        l.Add(OsmName(b.Id) + (b.Type.Length > 0 ? " · " + b.Type : "") + (b.Enterable ? "" : " · solid (not enterable)"));
        string storeys = b.Storeys + (b.Storeys == 1 ? " storey" : " storeys") + (b.StoreysSource.Length > 0 ? $" ({b.StoreysSource})" : "");
        string roof = b.Roof + " roof" + (b.RoofShape is { Length: > 0 } shape ? $" ({shape})" : b.RoofSource.Length > 0 ? $" ({b.RoofSource})" : "");
        l.Add(storeys + " · " + roof);
        if (b.Enterable)
        {
            string doors = b.Doors == 0 ? "no door" : b.Doors + (b.Doors == 1 ? " door" : " doors") + (b.DoorRule.Length > 0 && b.DoorRule != "none" ? $" ({b.DoorRule} rule)" : "");
            l.Add(doors + " · " + b.Windows + (b.Windows == 1 ? " window" : " windows"));
        }
        if (b.SharedWallsWith.Count > 0)
            l.Add($"shares {b.SharedWallsWith.Count} {(b.SharedWallsWith.Count == 1 ? "wall" : "walls")} with {string.Join(", ", System.Linq.Enumerable.Select(b.SharedWallsWith, OsmName))}");
        return l;
    }

    /// <summary>"w112233445" → "way 112233445", "r9001-2" → "relation 9001 (part 2)", "n…" → "node …".</summary>
    public static string OsmName(string id)
    {
        if (id.Length < 2) return id;
        string kind = id[0] switch { 'w' => "way", 'r' => "relation", 'n' => "node", _ => "" };
        if (kind.Length == 0) return id;
        string rest = id[1..];
        int dash = rest.IndexOf('-');
        return dash > 0 ? $"{kind} {rest[..dash]} (part {rest[(dash + 1)..]})" : $"{kind} {rest}";
    }

    /// <summary>⭐ CE-4382 — zooms the map to a note's box (local metres, through the preview's frame); a small or point box gets a 120 m view round it.</summary>
    public void ZoomTo(UtmFrame f, double[] box)
    {
        double cx = (box[0] + box[2]) * 0.5, cy = (box[1] + box[3]) * 0.5;
        double hw = Math.Max(60, (box[2] - box[0]) * 0.5), hh = Math.Max(60, (box[3] - box[1]) * 0.5);
        var (s0, w0) = Fdp.Modules.Geographic.Transforms.Utm.ToGeodetic(f.E0 + cx - hw, f.N0 + cy - hh, f.Zone, f.South);
        var (n1, e1) = Fdp.Modules.Geographic.Transforms.Utm.ToGeodetic(f.E0 + cx + hw, f.N0 + cy + hh, f.Zone, f.South);
        Fit(new GeoBox(Math.Min(s0, n1), Math.Min(w0, e1), Math.Max(s0, n1), Math.Max(w0, e1)));
    }

    private static bool Inside(Vector2[] poly, int n, Vector2 p)
    {
        bool c = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) c = !c;
        return c;
    }

    private static void DashedRect(ImDrawListPtr dl, Vector2 a, Vector2 b, uint col, float w, float dash, float gap)
    {
        void Seg(Vector2 p, Vector2 q)
        {
            var d = q - p; float len = d.Length(); if (len < 0.01f) return; var u = d / len;
            for (float t = 0; t < len; t += dash + gap) dl.AddLine(p + u * t, p + u * Math.Min(t + dash, len), col, w);
        }
        Seg(a, new Vector2(b.X, a.Y)); Seg(new Vector2(b.X, a.Y), b); Seg(b, new Vector2(a.X, b.Y)); Seg(new Vector2(a.X, b.Y), a);
    }
}
