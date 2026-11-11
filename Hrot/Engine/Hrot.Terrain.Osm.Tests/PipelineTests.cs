using System.Text.Json;
using Hrot.Terrain.InputSet;
using Xunit;

namespace Hrot.Terrain.Osm.Tests;

public class PreviewTests
{
    private static OsmImportOptions Opt => Fx.Options;

    [Fact]
    public async Task Preview_NeedsNoDemAndNoRoads_AndWritesNoInputSet()
    {
        using var tmp = new TempDir();
        var dem = FuncDem.EastSlope(); var roads = new TestRoads();
        var p = Fx.Pipeline(dem, roads, new DownloadCache(tmp.Combine("cache")));
        var preview = await p.PreviewAsync(Fx.Box, Opt, null, CancellationToken.None);
        Assert.Equal(0, dem.PrepareCalls); Assert.Equal(0, dem.HeightCalls); Assert.Equal(0, roads.Calls);
        Assert.Equal(new[] { "cache" }, Directory.GetDirectories(tmp.Path).Select(Path.GetFileName).ToArray());   // only the download cache exists
        Assert.Equal("© OpenStreetMap contributors · Test DEM", preview.Attribution);
        Assert.Equal(BudgetVerdict.Ok, preview.Verdict);
        Assert.Equal(11, preview.Counts.Buildings);
        Assert.Equal(15, preview.Buildings.Count);
        Assert.Equal(4, preview.Buildings.Count(b => !b.Enterable));
        Assert.Equal(6, preview.Counts.LoneTrees);
        Assert.InRange(preview.Counts.EstimatedTrees, 180, 182);       // 6 + 15 200 m² × 115 / ha
        Assert.Equal(5, preview.Counts.Walls); Assert.Equal(2, preview.Counts.WaterAreas);
        Assert.InRange(preview.Counts.RoadKm, 1.4, 1.5);   // 598 + 360 + ~497 m of residential streets
        Assert.Contains(preview.Warnings, w => w.Contains("SOLID"));
        Assert.Contains(preview.Warnings, w => w.Contains("shared walls"));
        Assert.Contains(preview.Areas, a => a.Kind == "woods"); Assert.Contains(preview.Areas, a => a.Kind == "scrub"); Assert.Contains(preview.Areas, a => a.Kind == "water");
        Assert.Contains(preview.Lines, l => l.Kind == "tree_row");
        Assert.Equal(Fx.Bounds.MinX, preview.Bounds[0]);
    }

    [Fact]
    public async Task PreviewThenWrite_FetchTheMapOnce()
    {
        using var tmp = new TempDir();
        var src = new CountingSource(new FileOsmSource(Fx.FixturePath));
        var p = Fx.Pipeline(FuncDem.Flat(), new TestRoads(), new DownloadCache(tmp.Combine("cache")), source: src);
        await p.PreviewAsync(Fx.Box, Opt, null, default);
        await p.WriteInputSetAsync(Fx.Box, Opt, tmp.Combine("out"), null, default);
        Assert.Equal(1, src.Fetches);
    }

    [Fact]
    public async Task OptionsSwitchTheLayersOff()
    {
        using var tmp = new TempDir();
        var p = Fx.Pipeline(FuncDem.Flat(), new TestRoads(), new DownloadCache(tmp.Combine("cache")));
        var pv = await p.PreviewAsync(Fx.Box, new OsmImportOptions { Name = "x", Buildings = false, Trees = false, Walls = false, Water = false }, null, default);
        Assert.Empty(pv.Buildings); Assert.Empty(pv.Areas); Assert.Empty(pv.Lines);
        Assert.Equal(0, pv.Counts.Buildings);
    }

    // ---- §5.7 the budget
    [Theory]
    [InlineData(3600, BudgetVerdict.Ok)] [InlineData(3601, BudgetVerdict.Heavy)] [InlineData(5400, BudgetVerdict.Heavy)] [InlineData(5401, BudgetVerdict.Refused)]
    public void Budget_Buildings_Warn2x_Refuse3x(int buildings, BudgetVerdict expected) =>
        Assert.Equal(expected, OsmBudget.Default.Evaluate(new OsmCounts(buildings, 0, 0, 0, 0, 0, 0, 0, 0)).Verdict);

    [Theory]
    [InlineData(140000, BudgetVerdict.Ok)] [InlineData(140001, BudgetVerdict.Heavy)] [InlineData(210000, BudgetVerdict.Heavy)] [InlineData(210001, BudgetVerdict.Refused)]
    public void Budget_Openings_DoorsPlusWindows(int openings, BudgetVerdict expected) =>
        Assert.Equal(expected, OsmBudget.Default.Evaluate(new OsmCounts(0, openings / 2, openings - openings / 2, 0, 0, 0, 0, 0, 0)).Verdict);

    [Theory]
    [InlineData(352000, BudgetVerdict.Ok)] [InlineData(352001, BudgetVerdict.Heavy)] [InlineData(528000, BudgetVerdict.Heavy)] [InlineData(528001, BudgetVerdict.Refused)]
    public void Budget_Trees_LonePlusWoodsByArea(int trees, BudgetVerdict expected) =>
        Assert.Equal(expected, OsmBudget.Default.Evaluate(new OsmCounts(0, 0, 0, 0, 0, trees, 0, 0, 0)).Verdict);

    [Fact]
    public void Budget_ReasonSaysHowMuchAndHowMuchSmaller()
    {
        var (v, reason) = OsmBudget.Default.Evaluate(new OsmCounts(5900, 0, 0, 0, 0, 0, 0, 0, 0));
        Assert.Equal(BudgetVerdict.Refused, v);
        Assert.Contains("5 900 enterable buildings", reason); Assert.Contains("1 800", reason); Assert.Contains("smaller box", reason);
        Assert.Null(OsmBudget.Default.Evaluate(new OsmCounts(10, 0, 0, 0, 0, 0, 0, 0, 0)).Reason);
    }

    [Fact]
    public void Budget_ThresholdsAreSettings()
    {
        var tight = new OsmBudget { TunedBuildings = 4 };   // fixture: 11 enterable buildings ⇒ 2.75×
        Assert.Equal(BudgetVerdict.Heavy, tight.Evaluate(new OsmCounts(11, 0, 0, 0, 0, 0, 0, 0, 0)).Verdict);
        Assert.Equal(BudgetVerdict.Refused, new OsmBudget { TunedBuildings = 3 }.Evaluate(new OsmCounts(11, 0, 0, 0, 0, 0, 0, 0, 0)).Verdict);
    }
}

public class WriteTests
{
    private static async Task<(string Out, FuncDem Dem, TestRoads Roads, TempDir Tmp)> WriteFixture()
    {
        var tmp = new TempDir();
        var dem = FuncDem.EastSlope(); var roads = new TestRoads();
        var p = Fx.Pipeline(dem, roads, new DownloadCache(tmp.Combine("cache")));
        string outDir = await p.WriteInputSetAsync(Fx.Box, Fx.Options, tmp.Combine("terrain-input/fixture-town"), null, default);
        return (outDir, dem, roads, tmp);
    }

    /// <summary>⭐ <c>CE-4409</c> — roads are OPTIONAL: a road builder that throws (netconvert's exit 1) still produces the terrain — buildings, trees, heights, a manifest that passes the format checks — WITHOUT a
    /// roads folder, and the import reports a WARNING naming the failure. 🔒 User, 2026-11-10: <i>"if sumo roadnet fails, it shouldn't fail the whole import."</i>
    /// 🔴 Red-proof (compiles): the roads call outside the <c>try</c> ⇒ the import throws ⇒ red.</summary>
    [Fact]
    public async Task ARoadFailure_ImportsTheTerrainWithoutRoads_AndWarns()
    {
        using var tmp = new TempDir();
        var p = Fx.Pipeline(FuncDem.Flat(), new ThrowingRoads("netconvert failed (exit 1): Warning: environment variable SUMO_HOME is not set properly"), new DownloadCache(tmp.Combine("cache")));
        var seen = new List<OsmProgress>();
        string outDir = await p.WriteInputSetAsync(Fx.Box, Fx.Options, tmp.Combine("terrain-input/no-roads"), new SyncProgress<OsmProgress>(seen.Add), default);
        Assert.Empty(InputSetChecks.Check(outDir));
        Assert.False(Directory.Exists(Path.Combine(outDir, "roads")), "the half-written roads folder is gone");
        Assert.Null(InputSetReader.Open(outDir).RoadsNet);
        Assert.Equal(15, InputSetReader.Open(outDir).ReadBuildings().Count);
        var warning = Assert.Single(seen, s => s.Warning != null).Warning!;
        Assert.Contains("No roads were imported", warning);
        Assert.Contains("SUMO_HOME is not set properly", warning);
    }

    /// <summary>⭐ CE-4409 — a cancelled import is still a cancel, never a "roads failed" warning.</summary>
    [Fact]
    public async Task ACancelledRoadBuild_IsStillACancel_NotAWarning()
    {
        using var tmp = new TempDir();
        var p = Fx.Pipeline(FuncDem.Flat(), new ThrowingRoads(null), new DownloadCache(tmp.Combine("cache")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => p.WriteInputSetAsync(Fx.Box, Fx.Options, tmp.Combine("terrain-input/cancelled"), null, default));
    }

    private sealed class SyncProgress<T> : IProgress<T> { private readonly Action<T> _a; public SyncProgress(Action<T> a) => _a = a; public void Report(T v) => _a(v); }

    private sealed class ThrowingRoads : IRoadBuilder
    {
        private readonly string? _message;   // null = cancel
        public ThrowingRoads(string? message) => _message = message;
        public Task BuildAsync(string osmFile, UtmFrame frame, string roadsFolder, CancellationToken cancellation)
        {
            Directory.CreateDirectory(roadsFolder);
            File.WriteAllText(Path.Combine(roadsFolder, "partial.txt"), "x");
            if (_message == null) throw new OperationCanceledException();
            throw new InvalidOperationException(_message);
        }
    }

    [Fact]
    public async Task TheWrittenFolder_PassesTheInputSetFormatChecks_WithZeroProblems()
    {
        var (outDir, dem, roads, tmp) = await WriteFixture();
        using (tmp)
        {
            Assert.Empty(InputSetChecks.Check(outDir));
            Assert.Equal(1, dem.PrepareCalls); Assert.Equal(1, roads.Calls);
            Assert.True(File.Exists(Path.Combine(outDir, InputSetLayout.RoadsNet)));
            var set = InputSetReader.Open(outDir);
            Assert.Equal("fixture-town", set.Manifest.Name);
            Assert.Equal(new UtmFrame(33, false, 500000, 5430000), set.Manifest.Frame);
            Assert.Equal((0.0, 0.0), (set.Manifest.AnchorX, set.Manifest.AnchorY));
            Assert.Equal(15, set.ReadBuildings().Count);
            Assert.Equal(2, set.ReadAreas(InputSetLayout.Woods).Count());
            Assert.Single(set.ReadAreas(InputSetLayout.Scrub));
            Assert.Equal(2, set.ReadAreas(InputSetLayout.Water).Count());
            Assert.Equal(5, set.ReadWalls().Count());
            Assert.Equal(6, set.ReadTrees().Count);
            Assert.Equal(4, set.ReadBuildings().Count(b => !b.Enterable));
            Assert.Equal(30.0, set.Heights.SourceResolution);   // F-9
        }
    }

    [Fact]
    public async Task TheManifestCarriesTheAttribution_AndTheSourceSection()
    {
        var (outDir, _, _, tmp) = await WriteFixture();
        using (tmp)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "manifest.json")));
            var src = doc.RootElement.GetProperty("source");
            Assert.Equal("© OpenStreetMap contributors · Test DEM", src.GetProperty("attribution").GetString());
            Assert.Equal("Test DEM", src.GetProperty("dem").GetString());
            Assert.Equal("2026-01-01T00:00:00Z", src.GetProperty("osm").GetString());
        }
    }

    [Fact]
    public async Task BuildingsStandOnTheLowestGround_OnTheWrittenGrid()
    {
        var (outDir, _, _, tmp) = await WriteFixture();
        using (tmp)
        {
            var set = InputSetReader.Open(outDir);
            var church = set.ReadBuildings().Single(b => b.Id == Fx.Church);
            var (info, z) = HeightsGrid.ReadAll(outDir);
            double Ground(double x, double y) => z[(int)Math.Round((y - info.OriginY) / info.CellSize) * info.Cols + (int)Math.Round((x - info.OriginX) / info.CellSize)];
            double lowest = church.Footprint.Min(p => Ground(p.X, p.Y));
            Assert.Equal(lowest, church.BaseZ, 1);
            Assert.True(church.Footprint.Max(p => Ground(p.X, p.Y)) - church.BaseZ > 2);
            Assert.Equal(church.BaseZ, church.StoreyZ[0], 6);
        }
    }

    [Fact]
    public async Task RefusedBudget_ThrowsAndLeavesNothing()
    {
        using var tmp = new TempDir();
        var dem = FuncDem.Flat(); var roads = new TestRoads();
        var p = Fx.Pipeline(dem, roads, new DownloadCache(tmp.Combine("cache")), new OsmBudget { TunedBuildings = 3 });   // 11 enterable = 3.7×
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => p.WriteInputSetAsync(Fx.Box, Fx.Options, tmp.Combine("out"), null, default));
        Assert.Contains("refused", e.Message);
        Assert.Equal(0, dem.PrepareCalls); Assert.Equal(0, roads.Calls);
        Assert.False(Directory.Exists(tmp.Combine("out")));
    }

    [Fact]
    public async Task CancelBeforeTheDem_LeavesNothing()
    {
        using var tmp = new TempDir();
        var cts = new CancellationTokenSource();
        var dem = FuncDem.Flat();
        dem.OnPrepare = ct => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };
        var p = Fx.Pipeline(dem, new TestRoads(), new DownloadCache(tmp.Combine("cache")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => p.WriteInputSetAsync(Fx.Box, Fx.Options, tmp.Combine("terrain/out"), null, cts.Token));
        AssertNothingLeft(tmp);
    }

    [Fact]
    public async Task CancelMidHeights_LeavesNothing_NoTempFolderEither()
    {
        using var tmp = new TempDir();
        var cts = new CancellationTokenSource();
        var dem = FuncDem.Flat();
        dem.OnHeight = n => { if (n == 50_000) cts.Cancel(); };   // deep inside the 600 × 500 grid
        var p = Fx.Pipeline(dem, new TestRoads(), new DownloadCache(tmp.Combine("cache")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => p.WriteInputSetAsync(Fx.Box, Fx.Options, tmp.Combine("terrain/out"), null, cts.Token));
        Assert.True(dem.HeightCalls >= 50_000);
        AssertNothingLeft(tmp);
    }

    private static void AssertNothingLeft(TempDir tmp)
    {
        Assert.False(Directory.Exists(tmp.Combine("terrain/out")));
        Assert.Empty(Directory.Exists(tmp.Combine("terrain")) ? Directory.GetFileSystemEntries(tmp.Combine("terrain")) : Array.Empty<string>());
    }

    [Fact]
    public async Task ANonEmptyOutputFolder_IsRefusedBeforeAnyWork()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.Combine("out")); File.WriteAllText(tmp.Combine("out/keep.txt"), "x");
        var src = new CountingSource(new FileOsmSource(Fx.FixturePath));
        var p = Fx.Pipeline(FuncDem.Flat(), new TestRoads(), new DownloadCache(tmp.Combine("cache")), source: src);
        await Assert.ThrowsAsync<IOException>(() => p.WriteInputSetAsync(Fx.Box, Fx.Options, tmp.Combine("out"), null, default));
        Assert.Equal(0, src.Fetches);
        Assert.True(File.Exists(tmp.Combine("out/keep.txt")));
    }

    [Fact]
    public void Measure_RefusesInvertedTooLargeAndTinyBoxes()
    {
        var p = Fx.Pipeline(FuncDem.Flat(), new TestRoads(), new DownloadCache("x"));
        Assert.Equal(6000.0, p.MaxSideMetres);
        var (w, h, ok) = p.Measure(Fx.Box);
        Assert.Null(ok); Assert.InRange(w, 598, 603); Assert.InRange(h, 498, 503);
        Assert.NotNull(p.Measure(new GeoBox(49.1, 15, 49.0, 15.1)).Refusal);                  // inverted
        Assert.Contains("6000", p.Measure(new GeoBox(49.0, 15.0, 49.1, 15.1)).Refusal!);       // ≈ 11 km tall
        Assert.NotNull(p.Measure(new GeoBox(49.0, 15.0, 49.0001, 15.0001)).Refusal);          // a few metres
    }
}
