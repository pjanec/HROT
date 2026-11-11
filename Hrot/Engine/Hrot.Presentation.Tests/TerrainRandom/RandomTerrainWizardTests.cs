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
        public static OsmPreview Preview(int houses, BudgetVerdict verdict = BudgetVerdict.Ok) => new()
        {
            Frame = default!, Bounds = new[] { 0.0, 0.0, 3000.0, 3000.0 }, Verdict = verdict, VerdictReason = verdict == BudgetVerdict.Ok ? null : $"{houses} buildings - HROT is tuned for 1 800", Warnings = Array.Empty<string>(),
            Counts = new OsmCounts(houses, 0, 0, 0, 640000, 0, 37, 0, 2), Attribution = "", Roads = new[] { new PreviewRoad("primary", new[] { new Vector2(0, 1500), new Vector2(3000, 1500) }), new PreviewRoad("track", new[] { new Vector2(1500, 0), new Vector2(1500, 3000) }) },
            Buildings = Enumerable.Range(0, 20).Select(i => new PreviewBuilding("b" + i, new[] { new Vector2(200 + i * 60, 1300), new Vector2(230 + i * 60, 1300), new Vector2(230 + i * 60, 1330), new Vector2(200 + i * 60, 1330) }, 2, true)).ToArray(),
            Areas = new[] { new PreviewArea("woods", new[] { new Vector2(2000, 2000), new Vector2(2600, 2000), new Vector2(2600, 2600), new Vector2(2000, 2600) }), new PreviewArea("water", new[] { new Vector2(100, 200), new Vector2(900, 200), new Vector2(900, 500), new Vector2(100, 500) }), new PreviewArea("block", new[] { new Vector2(180, 1280), new Vector2(1420, 1280), new Vector2(1420, 1360), new Vector2(180, 1360) }) },
            Lines = Array.Empty<PreviewLine>(),
        };
        public RandomTerrainView Generate(RandomTerrainSettings s)
        {
            Calls.Add($"generate {s.Preset}/{s.SizeMetres}/{s.Relief}/{s.Vegetation}/{s.Water}/g{s.GroundSeed}");
            if (FailGenerate) throw new InvalidOperationException("the synth failed");
            Generations++;
            return new RandomTerrainView(Preview(112 + Generations), 23, new[] { ("hamlet", 4, 21), ("farmstead", 5, 20), ("copse", 3, 0) }, 2.1);
        }
        public RandomTerrainView? ShuffleLayout(ulong layoutSeed) { Calls.Add("shuffle-layout " + layoutSeed); return new RandomTerrainView(Preview(130), 25, new[] { ("hamlet", 6, 30) }, 0.4); }
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
        Assert.Equal(25, m.View!.Blocks);

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
        var fake = new Fake();
        var window = new RandomTerrainWizardWindow(() => fake);
        window.Open();
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
        Assert.True(window.Glyphs.ContainsKey("blocks") && window.Glyphs.ContainsKey("roads") && window.Glyphs.ContainsKey("woods"));
        var shot = Raylib.LoadImageFromScreen();
        try
        {
            Assert.True(Count(shot, c => c.G > c.R + 20 && c.G > c.B + 20 && c.G is > 60 and < 160) > 400, "woods / ground are painted");
            Assert.True(Count(shot, c => c.R > 200 && c.G > 170 && c.B < 90) > 20, "the primary road is painted");
        }
        finally { Raylib.UnloadImage(shot); }
    }
}
