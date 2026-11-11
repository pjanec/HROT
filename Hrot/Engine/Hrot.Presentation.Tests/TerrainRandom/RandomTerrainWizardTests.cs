using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using Hrot.Terrain.Osm;
using Hrot.UI.Common.TerrainOsm;
using Hrot.UI.Common.TerrainRandom;
using ImGuiNET;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.TerrainRandom;

/// <summary>
/// ⭐ Terrain authoring U-8 (docs/DESIGN_Terrain_Authoring.md §12–§13, §18f; mockup <c>terrain-authoring-1-wizard.svg</c>) — the random-terrain wizard: the logic (generate → preview, shuffle layout keeps the ground and shuffle ground makes a new one,
/// open writes through the engine, a failure is a message) and its real frame (Xvfb; a skip is not a pass). ✅ Red-proofs (compiling mutations, one command): <c>ShuffleLayout</c> calling <c>Generate</c> ⇒ the "same ground" rail red; the frame's Open button
/// left enabled while busy ⇒ the layout rail red is not claimed — the model rail <c>Pump</c> dropping a fault ⇒ the failure rail red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class RandomTerrainWizardTests
{
    private sealed class Fake : IRandomTerrainService
    {
        public readonly List<string> Calls = new();
        public bool FailGenerate;
        public int Generations;
        /// <summary>A layout of 23 blocks on a 3 km terrain: four town blocks that touch (one group), three village streets, four hamlets, five farmsteads, three copses, four more farmsteads far apart — with houses inside each, the way the engine's preview records them.</summary>
        public static (List<PreviewBuilding> Houses, List<PreviewArea> Areas, List<PlanBlock> Marks) Blocks()
        {
            var houses = new List<PreviewBuilding>(); var areas = new List<PreviewArea>(); var marks = new List<PlanBlock>();
            void Block(string recipe, uint seed, float x, float y, float w, float h, int cols, int rows, int storeys)
            {
                int n = 0;
                for (int j = 0; j < rows; j++)
                    for (int i = 0; i < cols; i++, n++)
                    {
                        float bx = x + 8 + i * (w - 16) / cols, by = y + 8 + j * (h - 16) / rows;
                        houses.Add(new PreviewBuilding($"{recipe}{seed}/{n}", new[] { new Vector2(bx, by), new Vector2(bx + 14, by), new Vector2(bx + 14, by + 12), new Vector2(bx, by + 12) }, storeys + (n % 4 == 0 ? 3 : 0), true));
                    }
                areas.Add(new PreviewArea("block", new[] { new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h) }));
                marks.Add(new PlanBlock(recipe, seed, new Vector2(x, y), new Vector2(x + w, y + h), n));
            }
            for (int k = 0; k < 4; k++) Block("town-block", 4471u + (uint)k, 1100 + (k % 2) * 112, 1900 + (k / 2) * 92, 110, 90, 5, 4, 3);   // touching: one chip, "Town block ×4"
            for (int k = 0; k < 3; k++) Block("village-street", 900u + (uint)k, 2000 + k * 300, 1200, 140, 70, 6, 2, 1);
            for (int k = 0; k < 4; k++) Block("hamlet", 300u + (uint)k, 200 + k * 380, 1000, 90, 60, 3, 2, 1);
            for (int k = 0; k < 9; k++) Block("farmstead", 60u + (uint)k, 250 + k * 280, 600 + (k % 2) * 1800, 70, 50, 2, 1, 1);
            for (int k = 0; k < 3; k++) Block("copse", 70u + (uint)k, 2200 + k * 200, 2400, 80, 80, 0, 0, 1);
            return (houses, areas, marks);
        }

        public static OsmPreview Preview(int houses, BudgetVerdict verdict = BudgetVerdict.Ok)
        {
            var (hs, blockAreas, _) = Blocks();
            var areas = new List<PreviewArea> { new("woods", new[] { new Vector2(2000, 2000), new Vector2(2600, 2000), new Vector2(2600, 2600), new Vector2(2000, 2600) }), new("water", new[] { new Vector2(0, 300), new Vector2(3000, 500), new Vector2(3000, 640), new Vector2(0, 460) }) };
            areas.AddRange(blockAreas);
            return new OsmPreview
            {
                Frame = default!, Bounds = new[] { 0.0, 0.0, 3000.0, 3000.0 }, Verdict = verdict, VerdictReason = verdict == BudgetVerdict.Ok ? null : $"{houses} buildings - HROT is tuned for 1 800", Warnings = Array.Empty<string>(),
                Counts = new OsmCounts(houses, 0, 0, 0, 640000, 0, 37, 0, 1), Attribution = "", Roads = new[] { new PreviewRoad("primary", new[] { new Vector2(0, 1500), new Vector2(3000, 1500) }), new PreviewRoad("track", new[] { new Vector2(1500, 0), new Vector2(1500, 3000) }) },
                Buildings = hs, Areas = areas, Lines = Array.Empty<PreviewLine>(),
            };
        }

        public static PlanRelief Relief()
        {
            const int n = 64; var h = new float[n * n];
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) h[j * n + i] = 60f + 50f * MathF.Sin(i * 0.19f) * MathF.Cos(j * 0.13f) + 10f * MathF.Sin((i + j) * 0.4f);
            return new PlanRelief(n, h, h.Min(), h.Max());
        }

        public static RandomTerrainView View(int houses, double seconds) { var (_, _, marks) = Blocks(); return new RandomTerrainView(Preview(houses), marks.Count, marks.GroupBy(m => m.Recipe).Select(g => (g.Key, g.Count(), g.Sum(m => m.Houses))).ToList(), seconds, marks, Relief()); }

        /// <summary>A preset's tile picture: the generated layout (a coarse relief), the plain preset with the ground, the roads and the water only.</summary>
        public RandomTerrainView? PresetPicture(int preset)
        {
            var v = View(100 + preset, 0.1);
            var coarse = new PlanRelief(12, v.Relief!.Heights.Where((_, i) => i % 64 < 12 && i / 64 < 12).ToArray(), v.Relief.Min, v.Relief.Max);
            if (preset != 0) return v with { Relief = coarse };
            var plain = v.Preview with { Buildings = Array.Empty<PreviewBuilding>(), Areas = v.Preview.Areas.Where(a => a.Kind == "water").ToArray() };
            return new RandomTerrainView(plain, 0, Array.Empty<(string, int, int)>(), 0.1, null, coarse);
        }

        public RandomTerrainView Generate(RandomTerrainSettings s)
        {
            Calls.Add($"generate {s.Preset}/{s.SizeMetres}/{s.Relief}/{s.Vegetation}/{s.Water}/g{s.GroundSeed}");
            if (FailGenerate) throw new InvalidOperationException("the synth failed");
            Generations++;
            return View(112 + Generations, 2.1);
        }
        public RandomTerrainView? ShuffleLayout(ulong layoutSeed) { Calls.Add("shuffle-layout " + layoutSeed); return View(130, 0.4); }
        public bool NameExists(string name) => name == "taken";
        public Task<string> OpenInEditorAsync(string name, CancellationToken ct) { Calls.Add("open " + name); return Task.FromResult(name); }
    }

    private static void Settle(RandomTerrainWizardModel m) { for (int i = 0; i < 400 && m.Busy; i++) { Thread.Sleep(5); m.Pump(); } m.Pump(); }

    [Fact]
    public void U8_GenerateMakesThePreview_ShuffleLayoutKeepsTheGround_ShuffleGroundChangesIt_OpenWritesThroughTheEngine()
    {
        var fake = new Fake();
        var m = new RandomTerrainWizardModel(fake, seed: 48213) { Preset = 4, Size = 2, Relief = 1, Vegetation = 1, Water = 2 };
        Assert.Equal(RandomTerrainStep.Preset, m.Step);
        m.Generate(); Assert.Equal(RandomTerrainStep.Preview, m.Step); Settle(m);
        Assert.Equal("generate 4/3000/1/1/2/g48213", Assert.Single(fake.Calls));   // the settings as the rows say them
        Assert.Equal(23, m.View!.Blocks);

        m.ShuffleLayout(); Settle(m);
        Assert.StartsWith("shuffle-layout ", fake.Calls[1]);
        Assert.Equal(1, fake.Generations);                   // the ground was NOT made again
        Assert.Equal(23, m.View!.Blocks);
        Assert.Equal(130, m.View!.Preview.Counts.Buildings);

        ulong seed = m.Seed;
        m.ShuffleGround(); Settle(m);
        Assert.NotEqual(seed, m.Seed);
        Assert.Equal(2, fake.Generations);                   // a new ground

        m.Name = "riverside-3"; m.OpenInEditor(); Assert.Equal(RandomTerrainStep.Open, m.Step); Settle(m);
        Assert.Equal("open riverside-3", fake.Calls[^1]);
        Assert.Equal("riverside-3", m.OpenedScenario);

        var taken = new RandomTerrainWizardModel(fake) { Name = "taken" };
        Assert.True(taken.NameExists);
        var bad = new RandomTerrainWizardModel(fake) { Name = "bad name" };
        Assert.False(bad.NameOk);
    }

    [Fact]
    public void U8_AFailedGenerateIsAMessageAndBackToThePreset_NotAnExceptionInTheFrame()
    {
        var m = new RandomTerrainWizardModel(new Fake { FailGenerate = true });
        m.Generate(); Settle(m);
        Assert.Equal("the synth failed", m.Error);
        Assert.Equal(RandomTerrainStep.Preset, m.Step);
        Assert.Null(m.View);
    }

    private static int Count(Image image, Func<Color, bool> what)
    {
        int n = 0;
        for (int y = 0; y < image.Height; y += 2)
            for (int x = 0; x < image.Width; x += 2)
                if (what(Raylib.GetImageColor(image, x, y))) n++;
        return n;
    }

    [SkippableFact]
    public void U8_TheWizardsFrames_HaveTheMockupsPresetsAndRows_ThePlanAndTheVerdict_AlignedAndInColour()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(1100, 640);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);   // the real editor fonts: the harness default is ASCII-only
        var fake = new Fake();
        var window = new RandomTerrainWizardWindow(() => fake);
        window.Open();
        for (int i = 0; i < 400 && window.Model!.Pictures.Any(p => p is null); i++) { Thread.Sleep(5); frame.Step(window.Draw); }   // the six tile pictures are generated on a worker
        for (int i = 0; i < 6; i++) frame.Step(window.Draw);
        string? shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS");
        if (shots is { Length: > 0 }) { Directory.CreateDirectory(shots); frame.Screenshot(Path.Combine(shots, "u8-wizard-1-preset.png")); }

        var r = window.Rects;
        // ① six presets in a 3 x 2 grid (one size, aligned), the four option rows share their left edge, the footer's Generate is the filled blue one
        Assert.Equal(r["preset:0"].Min.X, r["preset:3"].Min.X, 1f);
        Assert.Equal(r["preset:0"].Min.Y, r["preset:2"].Min.Y, 1f);
        Assert.True(r["preset:3"].Min.Y > r["preset:0"].Max.Y);
        Assert.Equal(r["chip:size:0"].Min.X, r["chip:relief:0"].Min.X, 1f);
        Assert.Equal(r["chip:relief:0"].Min.X, r["chip:water:0"].Min.X, 1f);
        Assert.True(r["chip:size:0"].Min.Y < r["chip:relief:0"].Min.Y && r["chip:relief:0"].Min.Y < r["chip:vegetation:0"].Min.Y && r["chip:vegetation:0"].Min.Y < r["chip:water:0"].Min.Y);
        Assert.Equal(r["btn:generate"].Min.Y, r["btn:cancel"].Min.Y, 1f);
        Assert.True(window.Glyphs.ContainsKey("preset.sel"), "the chosen preset carries its check mark");

        // gap B — the pills are joined by a dash (between one pill and the next, on their row), and every preset tile shows a GENERATED picture (relief-shaded ground, not a flat stand-in)
        Assert.True(r["step-link:0"].Min.X >= r["step:0"].Max.X - 8 && r["step-link:0"].Max.X <= r["step:1"].Min.X + 8 && Math.Abs(r["step-link:0"].Min.Y - (r["step:0"].Min.Y + r["step:0"].Max.Y) / 2) < 3f, "a dash joins pill 1 to pill 2");
        var preset = Raylib.LoadImageFromScreen();
        try
        {
            for (int t = 0; t < 6; t++)
            {
                Assert.True(r.ContainsKey("picture:" + t), $"preset {t}'s picture is drawn from the generated terrain");
                var pic = r["picture:" + t]; var colours = new HashSet<int>();
                for (int y = (int)pic.Min.Y + 2; y < (int)pic.Max.Y - 2; y += 2)
                    for (int x = (int)pic.Min.X + 2; x < (int)pic.Max.X - 2; x += 2) { var c = Raylib.GetImageColor(preset, x, y); colours.Add((c.R >> 2) << 16 | (c.G >> 2) << 8 | (c.B >> 2)); }
                Assert.True(colours.Count >= 10, $"preset {t}'s picture is shaded ground with its features ({colours.Count} colours; a stand-in has ~3)");
            }
        }
        finally { Raylib.UnloadImage(preset); }

        window.Model!.Generate();
        for (int i = 0; i < 400 && window.Model!.Busy; i++) { Thread.Sleep(5); frame.Step(window.Draw); }
        for (int i = 0; i < 6; i++) frame.Step(window.Draw);
        if (shots is { Length: > 0 }) frame.Screenshot(Path.Combine(shots, "u8-wizard-2-preview.png"));
        r = window.Rects;
        // ② the plan fills the left, the verdict is green, the two shuffles and Open in editor are there; the plan is painted (woods green, houses by height, roads yellow)
        Assert.True(r["map"].Max.X < r["btn:shuffle-layout"].Min.X);
        Assert.Equal(r["btn:shuffle-layout"].Min.X, r["btn:shuffle-ground"].Min.X, 1f);
        Assert.True(r["btn:shuffle-layout"].Min.Y < r["btn:shuffle-ground"].Min.Y);
        Assert.Equal(OsmWizardStyle.OK, window.Glyphs["verdict"].Colour);
        Assert.True(window.Glyphs.ContainsKey("recipe:town-block") && window.Glyphs.ContainsKey("roads") && window.Glyphs.ContainsKey("woods"));
        var shot = Raylib.LoadImageFromScreen();
        try
        {
            Assert.True(Count(shot, c => c.G > c.R + 20 && c.G > c.B + 20 && c.G is > 60 and < 160) > 400, "woods / ground are painted");
            Assert.True(Count(shot, c => c.R > 200 && c.G > 170 && c.B < 90) > 20, "the primary road is painted");
        }
        finally { Raylib.UnloadImage(shot); }
    }

    // ── the plan (U-8 follow-up 2, mockup 1 bottom): the blocks as chips and outlines, the relief, the tooltip ─────────────────────────────────

    /// <summary>
    /// ⭐ Gap A, the key rail — the 23 blocks the layout lists are DRAWN as 20 groups: the four touching town blocks are ONE chip <i>Town block ×4</i> (the nine far-apart farmsteads nine), the tooltip is the mockup's three lines, the caption its caption and the rows its plural names.
    /// 🔴 Mutation: <see cref="RandomTerrainPlan.GroupGap"/> made −1000 ⇒ no two blocks are ever one group ⇒ 23 chips, no <i>×4</i> ⇒ red.
    /// </summary>
    [Fact]
    public void U8_ThePlanGroupsTouchingBlocksIntoOneChip_TheTooltipAndTheCaptionAreTheMockups_TheRowsNameTheRecipes()
    {
        var view = Fake.View(113, 2.1);
        var plan = new RandomTerrainPlan(view, waterSetting: 2);
        Assert.Equal(23, plan.BlockCount);
        Assert.Equal(20, plan.Groups.Length);                                   // 1 town group + 3 village streets + 4 hamlets + 9 farmsteads + 3 copses
        var town = Assert.Single(plan.Groups, g => g.Recipe == "town-block");
        Assert.Equal(("Town block ×4", 4, 80), (town.Chip, town.Count, town.Houses));
        Assert.Equal(9, plan.Groups.Count(g => g.Recipe == "farmstead" && g.Chip == "Farmstead"));
        Assert.Equal(new[] { "Town block ×4 — 80 houses", "seed 4471 · 3–6 storeys", "click a block to see it larger" }, plan.TipOf(0));
        var rel = Fake.Relief();
        Assert.Equal($"3.0 × 3.0 km · relief {Math.Round(rel.Max - rel.Min):0} m · 1 river", plan.Caption);
        Assert.Equal(("town-block", 4, 80), (plan.Rows[0].Recipe, plan.Rows[0].Blocks, plan.Rows[0].Houses));
        Assert.Equal("Village streets", Hrot.UI.Common.AddEntity.TerrainPickCatalog.Plural("village-street"));
        Assert.Equal("Hamlets", Hrot.UI.Common.AddEntity.TerrainPickCatalog.Plural("hamlet"));
        Assert.Equal("Woods and copses", Hrot.UI.Common.AddEntity.TerrainPickCatalog.Plural("copse"));
        // a click on a block zooms to its group, a click elsewhere zooms back
        Assert.Equal(-1, plan.Zoom);
    }

    /// <summary>
    /// ⭐ Gap A in a REAL frame: every group has its chip on the canvas (the icon's glyph and colour recorded), the ground is SHADED by the relief (many distinct ground colours, not one fill), the dashed outlines are in the editor's colour, hovering the town block shows its
    /// tooltip and lights its group, a click zooms to it (the chip grows) and a click on empty ground zooms back, the right column has the coloured recipe rows, and <see cref="RandomTerrainPlan.Draw"/> allocates 0 B in a steady frame.
    /// Renders <c>u8-wizard-2-preview.png</c> and <c>u8-wizard-2b-zoom.png</c>. 🔴 Mutation: the relief's nodes left unfilled (<c>_n = 0</c>) ⇒ one flat green ⇒ the shading assertion red.
    /// </summary>
    [SkippableFact]
    public void U8_ThePlanFrame_ChipsOnEveryGroup_ReliefShading_HoverTooltip_ClickToZoom_ZeroBytesSteady()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(1100, 640);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        var fake = new Fake();
        var window = new RandomTerrainWizardWindow(() => fake);
        window.Open();
        for (int i = 0; i < 4; i++) frame.Step(window.Draw);
        window.Model!.Generate();
        for (int i = 0; i < 400 && window.Model!.Busy; i++) { Thread.Sleep(5); frame.Step(window.Draw); }
        for (int i = 0; i < 6; i++) frame.Step(window.Draw);
        var plan = window.Plan!;
        string? shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS");

        // every group has a chip on the canvas, carrying the recipe's icon in its colour; the right column carries one coloured row per recipe and the Back-to-the-preset button
        int chips = window.Rects.Keys.Count(k => k.StartsWith("chip:", StringComparison.Ordinal));
        Assert.Equal(plan.Groups.Length, chips);
        var map = window.Rects["map"];
        foreach (var g in plan.Groups) { var c = window.Rects[g.Key]; Assert.True(c.Min.X >= map.Min.X - 1 && c.Max.X <= map.Max.X + 1 && c.Min.Y >= map.Min.Y - 1 && c.Max.Y <= map.Max.Y + 1, $"{g.Chip} chip is on the canvas"); }
        Assert.NotEqual(plan.Groups.First(g => g.Recipe == "copse").IconColour, plan.Groups.First(g => g.Recipe == "town-block").IconColour);   // woods green, blocks orange
        Assert.True(window.Glyphs.ContainsKey("recipe:town-block") && window.Glyphs.ContainsKey("recipe:farmstead") && window.Glyphs.ContainsKey("recipe:copse"));
        Assert.NotEqual(window.Glyphs["recipe:farmstead"].Glyph, window.Glyphs["recipe:town-block"].Glyph);
        Assert.NotEqual(window.Glyphs["recipe:farmstead"].Colour, window.Glyphs["recipe:copse"].Colour);
        Assert.True(window.Rects.ContainsKey("btn:back-preset"));
        Assert.True(window.Rects["btn:back-preset"].Min.Y > window.Rects["btn:shuffle-ground"].Max.Y);
        Assert.Equal(window.Rects["btn:shuffle-ground"].Min.X, window.Rects["btn:back-preset"].Min.X, 1f);

        if (shots is { Length: > 0 }) { Directory.CreateDirectory(shots); frame.Screenshot(Path.Combine(shots, "u8-wizard-2-preview.png")); }
        var shot = Raylib.LoadImageFromScreen();
        try
        {
            int outline = 0;
            for (int y = (int)map.Min.Y + 4; y < (int)map.Max.Y - 4; y += 3)
                for (int x = (int)map.Min.X + 4; x < (int)map.Max.X - 4; x += 3)
                {
                    var c = Raylib.GetImageColor(shot, x, y);
                    if (Math.Abs(c.R - 205) < 12 && Math.Abs(c.G - 214) < 12 && Math.Abs(c.B - 224) < 12) outline++;
                }
            // the ground's own pixels (a strip along the terrain's empty north-west corner: no road, house or chip): shaded by the relief ⇒ many shades; a flat fill ⇒ one
            float side = Math.Min(map.Max.X - map.Min.X, map.Max.Y - map.Min.Y), left = map.Min.X + ((map.Max.X - map.Min.X) - side) / 2f;
            var distinct = new HashSet<int>();
            for (int y = (int)map.Min.Y + 3; y < (int)map.Min.Y + 12; y++)
                for (int x = (int)left + 4; x < (int)left + 120; x++) { var c = Raylib.GetImageColor(shot, x, y); distinct.Add((c.R >> 2) << 16 | (c.G >> 2) << 8 | (c.B >> 2)); }
            Assert.True(distinct.Count >= 8, $"the ground is shaded by the relief ({distinct.Count} distinct shades along the empty corner; a flat fill has 1)");
            Assert.True(outline > 8, $"the outlines and chips are drawn in the editor's outline colour ({outline} px)");
        }
        finally { Raylib.UnloadImage(shot); }

        // hover the town block: its tooltip, its group lit
        var town = plan.Groups.First(g => g.Recipe == "town-block"); var chip = window.Rects[town.Key];
        var at = new Vector2(chip.Min.X + 14, chip.Max.Y + 14);
        for (int i = 0; i < 3; i++) frame.Step(() => { Pos(at.X, at.Y); window.Draw(); });
        Assert.True(plan.HoveredBlock >= 0 && plan.Groups[plan.GroupOf[plan.HoveredBlock]] == town);
        Assert.Equal("click a block to see it larger", plan.HoverTip![2]);
        Assert.StartsWith("Town block ×4 — ", plan.HoverTip![0]);

        // click: zoom to the group (its chip is where the group's box corner is, and the plan shows it larger); a click on empty ground zooms back
        float w0 = chip.Max.X - chip.Min.X;
        frame.Step(() => { Pos(at.X, at.Y); ImGui.GetIO().AddMouseButtonEvent(0, true); window.Draw(); });
        frame.Step(() => { Pos(at.X, at.Y); ImGui.GetIO().AddMouseButtonEvent(0, false); window.Draw(); });
        for (int i = 0; i < 3; i++) frame.Step(() => { Pos(at.X, at.Y); window.Draw(); });
        Assert.Equal(plan.GroupOf[0], plan.Zoom);
        Assert.True(plan.Groups.Count(g => !float.IsNaN(plan.ChipMin[Array.IndexOf(plan.Groups, g)].X)) < plan.Groups.Length, "zoomed in, the far groups' chips are off the canvas");
        if (shots is { Length: > 0 }) frame.Screenshot(Path.Combine(shots, "u8-wizard-2b-zoom.png"));
        // the zoomed outline is DASHED: along the first town block's top edge the light (selected-blue) and the ground alternate
        var zoomShot = Raylib.LoadImageFromScreen();
        try
        {
            var bmin = plan.BlockMin[0]; var bmax = plan.BlockMax[0];
            int lit = 0, total = 0;
            for (int x = (int)bmin.X + 2; x < (int)bmax.X - 2; x++)
            {
                bool any = false;
                for (int dy = -1; dy <= 1; dy++) { var c = Raylib.GetImageColor(zoomShot, x, (int)Math.Round(bmin.Y) + dy); if (c.B > c.G + 25) any = true; }
                total++; if (any) lit++;
            }
            double frac = total == 0 ? 0 : (double)lit / total;
            Assert.True(total > 60 && frac is > 0.3 and < 0.8, $"the outline is dashed: {lit}/{total} of the top edge is lit");
        }
        finally { Raylib.UnloadImage(zoomShot); }
        var empty = new Vector2(map.Min.X + 20, map.Min.Y + 60);
        frame.Step(() => { Pos(empty.X, empty.Y); window.Draw(); });
        frame.Step(() => { Pos(empty.X, empty.Y); ImGui.GetIO().AddMouseButtonEvent(0, true); window.Draw(); });
        frame.Step(() => { Pos(empty.X, empty.Y); ImGui.GetIO().AddMouseButtonEvent(0, false); window.Draw(); });
        for (int i = 0; i < 2; i++) frame.Step(() => { Pos(empty.X, empty.Y); window.Draw(); });
        Assert.Equal(-1, plan.Zoom);
        Assert.True(w0 > 40);

        // 0 B: the plan's own draw, steady (a few warm-up frames, then 20 measured one by one — a draw list holds one plan, 16-bit indices)
        long bytes = 0; int measured = 0;
        for (int f = 0; f < 24; f++)
            frame.Step(() =>
            {
                var dlist = ImGui.GetForegroundDrawList();
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                plan.Draw(dlist, map.Min, map.Max, 1f, new Vector2(-1000, -1000), false);
                long b1 = GC.GetAllocatedBytesForCurrentThread();
                if (f >= 4) { bytes += b1 - b0; measured++; }
            });
        Assert.Equal(20, measured); Assert.Equal(0, bytes);

    }

    private static void Pos(float x, float y) { ImGui.GetIO().AddMousePosEvent(x, y); Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }
}
