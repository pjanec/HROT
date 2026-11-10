using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Fdp.Presentation.Fonts;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Windows;
using Hrot.Terrain.Osm;
using Hrot.UI.Common.TerrainOsm;
using ImGuiNET;
using Xunit;
using static Hrot.UI.Common.TerrainOsm.OsmWizardStyle;

namespace Hrot.Presentation.Tests.TerrainOsm;

/// <summary>
/// ⭐⭐ CE-4375 W-4 ② (R-4138: a mockup is an acceptance spec) — <b>the conformance frame rails</b>: the REAL wizard window drawn in a REAL ImGui frame (Xvfb, the editor's fonts with
/// FontAwesome) over the W-2 fakes, one rail per step, MEASURING the handoff's conformance rows C-1 … C-14 against the approved mockup
/// (<c>docs/UX/terrain-osm-wizard-visual.svg</c> · <c>terrain-osm-preview-visual.svg</c>, from <c>scripts/gen-terrain-osm-wizard-mockup.py</c>): regions, column alignment, the footer order, every
/// named glyph AND its colour, the named fills, the step-bar states. Coordinates are in MOCKUP units — (screen − window origin) ÷ s, s = font size ÷ 14.
/// ⛔ Skipped without a display — a skip is not a gate row (run under <c>xvfb-run -a</c>). Each draws its frame to <c>HROT_RAIL_SHOTS</c> for the side-by-side (W-5).
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheOsmWizardMatchesItsMockupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "osm-wizard-frame-" + Guid.NewGuid().ToString("N"));
    private readonly FakeOsmPipeline _pipeline = new();
    private readonly FakeTerrainImporter _importer = new();

    public TheOsmWizardMatchesItsMockupTests() { Directory.CreateDirectory(_root); _importer.Root = Path.Combine(_root, "terrain"); }
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private OsmWizardModel Model(bool importer = true) => new(new OsmWizardServices(_pipeline, new FakeTileSource(), new FakePlaceSearch(), importer ? _importer : null,
        Path.Combine(_root, "terrain"), Path.Combine(_root, "input")) { ShowFolder = _ => { }, OpenNewScenario = _ => { } });

    private static void PumpUntil(OsmWizardModel m, Func<bool> done)
    {
        var sw = Stopwatch.StartNew();
        while (!done()) { if (sw.ElapsedMilliseconds > 5000) throw new TimeoutException(); m.Pump(); Thread.Sleep(5); }
    }

    /// <summary>Draws the window over <paramref name="m"/> for enough frames that the tiles have landed, then screenshots it.</summary>
    private static (OsmTerrainWizardWindow W, float S) Draw(UiFrameSession frame, OsmWizardModel m, string shot, Func<OsmTerrainWizardWindow, Vector2?>? mouseAt = null)
    {
        var w = new OsmTerrainWizardWindow(() => m.Services);
        w.OpenWith(m);
        float s = 1;
        for (int i = 0; i < 40; i++)
        {
            if (i == 24 && mouseAt != null) w.Map.MouseOverride = mouseAt(w);   // the map has settled: hover
            frame.Step(() => { s = ImGui.GetFontSize() / 14f; w.Draw(); });
            if (i > 6 && i % 8 == 0) Thread.Sleep(60);   // the tile workers
        }
        var dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-frames");
        Directory.CreateDirectory(dir);
        frame.Screenshot(Path.Combine(dir, shot));
        Assert.True(w.IsOpen, "the modal is open");
        return (w, s);
    }

    // ── measurement helpers (mockup units) ──
    private static Vector2 U(OsmTerrainWizardWindow w, Vector2 screen, float s) => (screen - w.Rects["window"].Min) / s;
    private static (Vector2 Min, Vector2 Max) R(OsmTerrainWizardWindow w, string key, float s) { var r = w.Rects[key]; return (U(w, r.Min, s), U(w, r.Max, s)); }
    private static void Near(float expected, float actual, float tol, string what) => Assert.True(Math.Abs(expected - actual) <= tol, $"{what}: expected ≈{expected}, measured {actual}");
    private static void Glyph(OsmTerrainWizardWindow w, string key, string glyph, Vector4 colour)
    {
        Assert.True(w.Glyphs.TryGetValue(key, out var g), $"glyph '{key}' is drawn");
        Assert.True(g.Glyph == glyph, $"glyph '{key}': expected U+{(int)glyph[0]:X4}, drawn U+{(int)g.Glyph[0]:X4}");
        Assert.True(g.Colour == colour, $"glyph '{key}': expected colour {colour}, drawn {g.Colour}");
    }

    /// <summary>C-1 · C-2 · C-3 · C-4: the one modal, its size, the step bar's three states, the regions, the footer order (right-aligned, Back absent in Area).</summary>
    private static void Frame(OsmTerrainWizardWindow w, float s, int step, bool back, string? primary)
    {
        var win = w.Rects["window"];
        Near(1200, (win.Max.X - win.Min.X) / s, 1, "C-1 window width"); Near(600, (win.Max.Y - win.Min.Y) / s, 1, "C-1 window height");
        for (int i = 0; i < 4; i++)
        {
            var (fill, border, state) = w.Chips[i];
            string expected = i < step ? "done" : i == step ? "current" : "ahead";
            Assert.Equal(expected, state);
            Assert.Equal(i < step ? FRAME : i == step ? BTNSEL : NEUTRAL, fill);
            Assert.Equal(i == step ? ACC : BORDER, border);
            var c = R(w, "chip:" + i, s);
            Near(30, c.Min.Y, 1, "C-2 chip top"); Near(24, c.Max.Y - c.Min.Y, 1, "C-2 chip height");
            if (i > 0) Assert.True(c.Min.X > R(w, "chip:" + (i - 1), s).Max.X + 15, "C-2 chips joined by a short line, left to right");
            if (i < step) Glyph(w, "chip:" + i, CHECK, OK);
        }
        Near(12, R(w, "chip:0", s).Min.X, 1, "C-2 the bar starts top-left");
        var cancel = R(w, "btn:cancel", s);
        Near(1180, cancel.Max.X, 2, "C-4 Cancel is right-aligned"); Near(566, cancel.Min.Y, 2, "C-4 footer row");
        Near(558, U(w, w.Rects["footer.sep"].Min, s).Y, 1, "C-3 the separator above the footer");
        if (primary != null)
        {
            var p = R(w, "btn:primary", s);
            Assert.True(p.Max.X < cancel.Min.X, "C-4 primary before Cancel");
            Near(p.Min.Y, cancel.Min.Y, 0.5f, "C-4 one footer row");
        }
        Assert.Equal(back, w.Rects.ContainsKey("btn:back"));
        if (back) Assert.True(R(w, "btn:back", s).Max.X < R(w, "btn:primary", s).Min.X, "C-4 Back before the primary");
    }

    /// <summary>C-3 (the map left ≈ 70 %, the right panel ≈ 28 %) and C-5 (the zoom / fit buttons top-left, the attribution bottom-right).</summary>
    private static void Canvas(OsmTerrainWizardWindow w, float s)
    {
        var c = R(w, "canvas", s);
        Near(10, c.Min.X, 1, "C-3 canvas x"); Near(64, c.Min.Y, 1, "C-3 canvas y");
        Near(830, c.Max.X - c.Min.X, 1, "C-3 canvas ≈ 70 % of 1200"); Near(476, c.Max.Y - c.Min.Y, 1, "C-3 canvas height");
        for (int i = 0; i < 3; i++)
        {
            var b = (U(w, w.Map.ZoomButtons[i].Min, s), U(w, w.Map.ZoomButtons[i].Max, s));
            Assert.True(b.Item1.X < c.Min.X + 40 && b.Item1.Y < c.Min.Y + 120, $"C-5 zoom button {i} top-left of the canvas");
            Near(b.Item2.X - b.Item1.X, b.Item2.Y - b.Item1.Y, 1, $"C-5 zoom button {i} is square");
        }
        var a = w.Map.AttributionRect!.Value;
        Near(c.Max.X, U(w, a.Max, s).X, 1, "C-5 attribution right edge"); Near(c.Max.Y, U(w, a.Max, s).Y, 1, "C-5 attribution bottom edge");
    }

    [SkippableFact]
    public void Area_MatchesTheMockup()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(1240, 640);
        new EditorFontService().Initialize(1f, 1f);
        var m = Model();
        m.SetBox(OsmFakeWorld.Box);
        m.SearchText = "Kolín"; m.RunSearch(); PumpUntil(m, () => m.SearchResults.Count > 0);
        var (w, s) = Draw(frame, m, "osm-wizard-1-area.png");

        Frame(w, s, 0, back: false, primary: "Next");
        Canvas(w, s);
        // C-6 the search field right of the zoom buttons, its glyph; the results' red pin, name TXT, kind NOTE
        var search = R(w, "search", s);
        Near(56, search.Min.X, 2, "C-6 search x (canvas + 46)"); Near(74, search.Min.Y, 2, "C-6 search y (canvas + 10)"); Near(330, search.Max.X - search.Min.X, 1, "C-6 search width"); Near(24, search.Max.Y - search.Min.Y, 1, "C-6 search height");
        Assert.True(search.Min.X > U(w, w.Map.ZoomButtons[0].Max, s).X, "C-6 right of the zoom buttons");
        Glyph(w, "search", SEARCH, SUB);
        for (int i = 0; i < 3; i++) { Assert.Equal((LOCATION, RED), w.Glyphs["result:" + i]); Near(23, R(w, "result:" + i, s).Max.Y - R(w, "result:" + i, s).Min.Y, 0.5f, "C-6 result row"); }
        // C-7 the box, 8 handles, the chip above it, not red within the limit
        Assert.Equal(8, w.Map.HandleCount);
        var box = w.Map.BoxRect!.Value; var chip = w.Map.SizeChip!.Value;
        Assert.True(chip.Max.Y <= box.Min.Y, "C-7 the size chip sits above the box's top edge");
        Assert.NotEqual(RED, chip.Border);
        // C-8 the right panel: four read-only rows in ONE field column, the info row, its glyph
        float fx = R(w, "field:width", s).Min.X;
        foreach (var k in new[] { "width", "height", "centre", "utm" }) Near(fx, R(w, "field:" + k, s).Min.X, 0.5f, $"C-8 '{k}' field column");
        Near(852 + 80, fx, 1, "C-8 field column = label column + 80");
        Near(852, R(w, "info.largest", s).Min.X, 1, "C-8 info row x");
        Glyph(w, "largest", INFO_G, INFO);
    }

    [SkippableFact]
    public void Area_ABoxOverTheLimit_TurnsTheChipRed_AndGreysNext()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(1240, 640);
        new EditorFontService().Initialize(1f, 1f);
        var m = Model();
        m.SetBox(WebMercator.BoxAround(OsmFakeWorld.Lat, OsmFakeWorld.Lon, 7000, 3000));
        var (w, _) = Draw(frame, m, "osm-wizard-1b-area-too-large.png");
        Assert.Equal(RED, w.Map.SizeChip!.Value.Border);
        Assert.Equal(DIS, w.Glyphs["btn:primary"].Colour);   // greyed: its glyph drawn disabled
    }

    [SkippableFact]
    public void Options_MatchesTheMockup()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(1240, 640);
        new EditorFontService().Initialize(1f, 1f);
        var m = Model();
        m.SetBox(OsmFakeWorld.Box); m.Primary(); m.Name = "kolin";
        var (w, s) = Draw(frame, m, "osm-wizard-2-options.png");

        Frame(w, s, 1, back: true, primary: "Preview");
        Glyph(w, "btn:primary", EYE, TEAL);
        // C-9 the left column: the name field, its green check; the four Include rows, each glyph in its colour, in one column
        var name = R(w, "field:name", s);
        Near(20 + 150, name.Min.X, 1, "C-9 name field x"); Near(390, name.Max.X - name.Min.X, 1, "C-9 name field width");
        Glyph(w, "name.valid", CIRCLE_CHECK, OK);
        var colours = new[] { ORANGE, GREEN, GREY, BLUE }; var glyphs = new[] { HOUSE, TREE, FENCE, WATER_G };
        for (int i = 0; i < 4; i++)
        {
            Glyph(w, "include:" + i, glyphs[i], colours[i]);
            Near(20 + 30, R(w, "glyph:include:" + i, s).Min.X, 1, $"C-9 include glyph {i} column");
            Near(74 + 112 + i * 28, R(w, "include:" + i, s).Min.Y, 2, $"C-9 include row {i} y");
        }
        // the right column: two fields in one column
        Near(R(w, "field:storey", s).Min.X, R(w, "field:storeys", s).Min.X, 0.5f, "C-9 right column fields aligned");
        Near(620 + 150, R(w, "field:storey", s).Min.X, 1, "C-9 right column x");
        Assert.False(w.Rects.ContainsKey("exists"), "C-9 no exists row while the name is free");

        // the name exists ⇒ the amber row, its glyph, Replace listing the scenarios
        Directory.CreateDirectory(Path.Combine(_root, "terrain", "kolin"));
        _importer.Users["kolin"] = new[] { "patrol-1" };
        var (w2, s2) = Draw(frame, m, "osm-wizard-2b-options-exists.png");
        var ex = R(w2, "exists", s2);
        Near(620, ex.Min.X, 1, "C-9 exists row x"); Near(74 + 150, ex.Min.Y, 1, "C-9 exists row y"); Near(540, ex.Max.X - ex.Min.X, 1, "C-9 exists row width");
        Glyph(w2, "exists", WARNING, PEND);
        Assert.True(w2.Rects.ContainsKey("replace"));
        Assert.Equal(DIS, w2.Glyphs["btn:primary"].Colour);   // Preview greyed until Replace
    }

    [SkippableFact]
    public void Preview_MatchesTheMockup()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(1240, 640);
        new EditorFontService().Initialize(1f, 1f);
        var m = Model();
        m.SetBox(OsmFakeWorld.Box); m.Primary(); m.Name = "kolin"; m.Primary();
        PumpUntil(m, () => m.PreviewState == OsmRunState.Done);
        // C-12: hover the biggest enterable building (its centroid, through the preview's own frame)
        var target = m.Preview!.Buildings.Where(b => b.Enterable).OrderByDescending(b => b.Footprint.Max(p => p.X) - b.Footprint.Min(p => p.X)).First();
        var (w, s) = Draw(frame, m, "osm-wizard-3-preview.png", win =>
        {
            var f = m.Preview!.Frame;
            float cx = target.Footprint.Average(p => p.X), cy = target.Footprint.Average(p => p.Y);
            var (lat, lon) = Fdp.Modules.Geographic.Transforms.Utm.ToGeodetic(f.E0 + cx, f.N0 + cy, f.Zone, f.South);
            return win.Map.ToScreen(lat, lon);
        });

        Frame(w, s, 2, back: true, primary: "Import");
        Assert.True(ReferenceEquals(target, w.Map.Hovered), "C-12 hovering a building picks it");
        // ⭐ CE-4382 — the mockup's tooltip lines, from the preview's details: id · type / storeys (source) · roof (shape) / doors (rule) · windows / shared walls
        var tip = OsmMapView.TooltipLines(target);
        Assert.Equal(OsmMapView.OsmName(target.Id) + " · " + target.Type, tip[0]);
        Assert.StartsWith("way ", tip[0]);
        Assert.Equal($"{target.Storeys} storeys ({target.StoreysSource}) · {target.Roof} roof ({target.RoofShape ?? target.RoofSource})", tip[1]);
        Assert.Equal($"{target.Doors} doors ({target.DoorRule} rule) · {target.Windows} windows", tip[2]);
        Assert.Equal(target.SharedWallsWith.Count > 0 ? 4 : 3, tip.Count);
        var terraced = m.Preview!.Buildings.First(b => b.SharedWallsWith.Count == 1);
        Assert.Equal("shares 1 wall with " + OsmMapView.OsmName(terraced.SharedWallsWith[0]), OsmMapView.TooltipLines(terraced)[^1]);
        Assert.Equal("way 112233445 · house", OsmMapView.TooltipLines(new Hrot.Terrain.Osm.PreviewBuilding("w112233445", target.Footprint, 2, true) { Type = "house" })[0]);
        Assert.Equal("relation 9001 (part 2)", OsmMapView.OsmName("r9001-2"));
        var gabled = m.Preview!.Buildings.First(b => b.RoofShape == "gabled");   // a tagged roof shape is named, not its source
        Assert.EndsWith("pitched roof (gabled)", OsmMapView.TooltipLines(gabled)[1]);
        // C-10 the lone trees, one dot each (the preview's Trees)
        Assert.Equal(m.Preview!.Trees.Count, w.Map.TreesDrawn);
        Glyph(w, "btn:primary", CHECK, GREEN);
        Canvas(w, s);
        // C-11 the Layers panel top-right of the canvas, ten swatches
        var layers = R(w, "layers", s); var canvas = R(w, "canvas", s);
        Assert.True(layers.Max.X <= canvas.Max.X && layers.Max.X > canvas.Max.X - 20 && layers.Min.Y < canvas.Min.Y + 20, "C-11 Layers top-right of the canvas");
        Near(204, layers.Max.X - layers.Min.X, 2, "C-11 Layers width");
        for (int i = 0; i < 10; i++) Assert.True(w.Map.SwatchRects[i].Max.X > w.Map.SwatchRects[i].Min.X, $"C-11 swatch {i} drawn");
        // C-13 the six rows: glyph in colour, value right-aligned at the panel's right edge
        var glyphs = new[] { (HOUSE, ORANGE), (SQUARE, GREY), (DOOR, AMBER), (TREE, GREEN), (ROUTE, BLUE), (FENCE, GREY) };
        for (int i = 0; i < 6; i++)
        {
            Glyph(w, "built:" + i, glyphs[i].Item1, glyphs[i].Item2);
            Near(852 + 338, R(w, "built.value:" + i, s).Max.X, 1, $"C-13 value {i} right-aligned");
            Near(64 + 26 + i * 22 + 2, R(w, "glyph:built:" + i, s).Min.Y, 1, $"C-13 row {i} y");
        }
        // the three bars in one column, the marks under the last
        for (int i = 0; i < 3; i++)
        {
            var b = R(w, "bar:" + i, s);
            Near(852 + 74, b.Min.X, 1, $"C-13 bar {i} x"); Near(852 + 338, b.Max.X, 1, $"C-13 bar {i} right"); Near(14, b.Max.Y - b.Min.Y, 0.5f, $"C-13 bar {i} height");
        }
        for (int k = 0; k < 3; k++) Assert.True(w.Rects.ContainsKey("mark:" + k), $"C-13 mark {k}");
        // the verdict box: Ok ⇒ green
        var v = R(w, "verdict", s);
        Near(64 + 168 + 122, v.Min.Y, 1, "C-13 verdict y"); Near(28, v.Max.Y - v.Min.Y, 0.5f, "C-13 verdict height");
        Glyph(w, "verdict", CIRCLE_CHECK, OK);
        // the notes, glyph per severity
        Glyph(w, "note:0", INFO_G, INFO);
        Glyph(w, "note:2", WARNING, PEND);   // ⭐ CE-4382 — from the note's Severity (the fake's third note is a Warning)
        // ⭐ CE-4382 — the data timestamp line above the attribution
        Assert.Equal("OSM data of 2026-11-10 08:12 UTC", w.Texts["data.time"]);
        Assert.Equal("Data: " + m.Preview!.Attribution, w.Texts["data"]);
        // ⭐ CE-4382 — clicking a note zooms the map to its place: the note's box centre is mid-canvas and the box is on screen
        var note = m.Preview!.Notes[2];
        w.ClickNote(m.Preview!, 2);
        var f = m.Preview!.Frame;
        var (nlat, nlon) = Fdp.Modules.Geographic.Transforms.Utm.ToGeodetic(f.E0 + (note.Box![0] + note.Box[2]) * 0.5, f.N0 + (note.Box[1] + note.Box[3]) * 0.5, f.Zone, f.South);
        Assert.Equal(nlat, w.Map.Lat, 6); Assert.Equal(nlon, w.Map.Lon, 6);
        Assert.True(w.Map.Zoom > 15, $"zoomed in to a 20 m note (zoom {w.Map.Zoom:F1})");
        double z = w.Map.Zoom;
        w.ClickNote(m.Preview!, 3);   // a note with no place: nothing moves
        Assert.Equal(z, w.Map.Zoom);
    }

    [SkippableFact]
    public void Preview_TheVerdictBox_ReadsHeavyAndRefused()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(1240, 640);
        new EditorFontService().Initialize(1f, 1f);
        foreach (var (verdict, glyph, colour, shot) in new[] { (BudgetVerdict.Heavy, WARNING, PEND, "osm-wizard-3b-heavy.png"), (BudgetVerdict.Refused, XMARK, ERR, "osm-wizard-3c-refused.png") })
        {
            _pipeline.Verdict = verdict; _pipeline.VerdictReason = "2.4× the tuned buildings";
            var m = Model();
            m.SetBox(OsmFakeWorld.Box); m.Primary(); m.Name = "kolin"; m.Primary();
            PumpUntil(m, () => m.PreviewState == OsmRunState.Done);
            var (w, _) = Draw(frame, m, shot);
            Glyph(w, "verdict", glyph, colour);
            Assert.Equal(verdict == BudgetVerdict.Refused ? DIS : GREEN, w.Glyphs["btn:primary"].Colour);   // Refused ⇒ Import greyed
        }
    }

    [SkippableFact]
    public void Import_MatchesTheMockup()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(1240, 640);
        new EditorFontService().Initialize(1f, 1f);
        var m = Model();
        m.SetBox(OsmFakeWorld.Box); m.Primary(); m.Name = "kolin"; m.Primary();
        PumpUntil(m, () => m.PreviewState == OsmRunState.Done);
        _pipeline.Hold = true;
        m.Primary();
        PumpUntil(m, () => m.Stages[4].State == OsmRunState.Running);
        var (w, s) = Draw(frame, m, "osm-wizard-4-import.png");

        Frame(w, s, 3, back: false, primary: null);
        // C-14 the stage glyphs by state, the bars in one column
        for (int i = 0; i < 8; i++)
        {
            var (g, c) = i < 4 ? (CIRCLE_CHECK, OK) : i == 4 ? (HOURGLASS, AMBER) : (CIRCLE, DIS);
            Glyph(w, "stage:" + i, g, c);
            var b = R(w, "stage.bar:" + i, s);
            Near(20 + 250, b.Min.X, 1, $"C-14 stage bar {i} x"); Near(300, b.Max.X - b.Min.X, 1, $"C-14 stage bar {i} width");
            Near(76 + 20 + i * 30 + 4, b.Min.Y, 1, $"C-14 stage bar {i} y");
        }
        var done = R(w, "done", s);
        Near(780, done.Min.X, 1, "C-14 done box x"); Near(400, done.Max.X - done.Min.X, 1, "C-14 done box width");
        // the footer: Run in background, then Cancel with a red XMARK
        var bg = R(w, "btn:background", s);
        Near(910, bg.Min.X, 1, "C-14 Run in background x");
        Glyph(w, "btn:cancel", XMARK, RED);
        _pipeline.Release();

        // finished, no importer ⇒ the "not built yet" line
        _pipeline.Hold = false;
        var m2 = Model(importer: false);
        m2.SetBox(OsmFakeWorld.Box); m2.Primary(); m2.Name = "kolin"; m2.Primary();
        PumpUntil(m2, () => m2.PreviewState == OsmRunState.Done);
        m2.Primary();
        PumpUntil(m2, () => m2.ImportState == OsmRunState.Done);
        var (w2, _) = Draw(frame, m2, "osm-wizard-4b-import-no-importer.png");
        Glyph(w2, "inputset", INFO_G, INFO);

        // finished with the importer ⇒ the done box with its two buttons
        var m3 = Model();
        m3.SetBox(OsmFakeWorld.Box); m3.Primary(); m3.Name = "kolin2"; m3.Primary();
        PumpUntil(m3, () => m3.PreviewState == OsmRunState.Done);
        m3.Primary();
        PumpUntil(m3, () => m3.ImportState == OsmRunState.Done);
        var (w3, _) = Draw(frame, m3, "osm-wizard-4c-import-done.png");
        Glyph(w3, "done", CIRCLE_CHECK, OK);
        Glyph(w3, "btn:open", FILE, BLUE);
        Glyph(w3, "btn:folder", FOLDER_OPEN, AMBER);
    }
}
