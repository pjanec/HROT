using Hrot.Terrain.InputSet;

namespace Hrot.Terrain.Osm;

/// <summary>
/// ⭐ The pipeline the editor's wizard and the CLI drive (docs/DESIGN_Terrain_From_OSM.md §14.3). PREVIEW needs no DEM and no netconvert — only the
/// (cached) OSM download and the rules. WRITE adds the DEM, the 1 m heights, the road net and the manifest, into a temp folder that is moved
/// into place only when everything succeeded and the result passes <see cref="InputSetChecks"/>.
/// </summary>
public sealed class OsmTerrainPipeline : IOsmTerrainPipeline
{
    public const double MarginMetres = 200;
    private readonly IOsmSource _osm;
    private readonly IDemSource _dem;
    private readonly IRoadBuilder _roads;
    private readonly DownloadCache _cache;
    private readonly OsmBudget _budget;

    public OsmTerrainPipeline(IOsmSource osm, IDemSource dem, IRoadBuilder roads, DownloadCache cache, OsmBudget? budget = null)
    {
        _osm = osm; _dem = dem; _roads = roads; _cache = cache; _budget = budget ?? OsmBudget.Default;
    }

    public double MaxSideMetres => AreaFrame.MaxSideMetres;

    public (double WidthM, double HeightM, string? Refusal) Measure(GeoBox area) => AreaFrame.Measure(area);

    public async Task<OsmPreview> PreviewAsync(GeoBox area, OsmImportOptions options, IProgress<OsmProgress>? progress, CancellationToken cancellation)
    {
        var a = await AnalyseAsync(area, options, progress, cancellation).ConfigureAwait(false);
        return PreviewBuilder.Build(a, _budget, _dem.Name);
    }

    private async Task<AreaAnalysis> AnalyseAsync(GeoBox area, OsmImportOptions options, IProgress<OsmProgress>? progress, CancellationToken ct)
    {
        var (_, _, refusal) = Measure(area);
        if (refusal != null) throw new ArgumentException(refusal, nameof(area));
        var bounds = AreaFrame.FromBox(area);
        var margin = AreaFrame.WithMargin(area, MarginMetres);

        ct.ThrowIfCancellationRequested();
        string file = await _cache.GetOrFetchAsync(margin, "area.osm", dest => _osm.FetchAsync(margin, dest, progress, ct)).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        progress?.Report(new OsmProgress(OsmStage.Parse, 0, "Reading the map data…"));
        var model = await Task.Run(() => OsmXmlReader.Read(file, bounds.Frame), ct).ConfigureAwait(false);
        progress?.Report(new OsmProgress(OsmStage.Parse, 1, "Map data read"));

        return await Task.Run(() =>
        {
            progress?.Report(new OsmProgress(OsmStage.Buildings, 0, "Building footprints, doors and windows…"));
            var buildings = options.Buildings ? BuildingRules.Apply(model, options, bounds) : new BuildingRulesResult();
            ct.ThrowIfCancellationRequested();
            progress?.Report(new OsmProgress(OsmStage.Vegetation, 0, "Woods, trees, walls and water…"));
            var veg = options.Trees ? VegetationRules.Apply(model, bounds) : new VegetationResult();
            var walls = options.Walls ? BarrierRules.Apply(model, bounds) : new List<WallDraft>();
            int filled = 0;
            var water = options.Water ? WaterRules.Apply(model, bounds, ref filled) : new List<AreaDraft>();
            var roads = RoadRules.PreviewLines(model, bounds);
            progress?.Report(new OsmProgress(OsmStage.Vegetation, 1, "Rules applied"));
            return new AreaAnalysis { Bounds = bounds, OsmFile = file, Model = model, Buildings = buildings, Vegetation = veg, Walls = walls, Water = water, Roads = roads, FilledHoles = filled };
        }, ct).ConfigureAwait(false);
    }

    public async Task<string> WriteInputSetAsync(GeoBox area, OsmImportOptions options, string outputFolder, IProgress<OsmProgress>? progress, CancellationToken cancellation)
    {
        string full = Path.GetFullPath(outputFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
            throw new IOException($"the output folder {full} already exists and is not empty — choose another name or remove it");

        var a = await AnalyseAsync(area, options, progress, cancellation).ConfigureAwait(false);
        var (verdict, reason) = _budget.Evaluate(PreviewBuilder.CountsOf(a));
        if (verdict == BudgetVerdict.Refused) throw new InvalidOperationException("the area is refused: " + reason);

        string parent = Path.GetDirectoryName(full) ?? throw new IOException("no parent folder for " + full);
        Directory.CreateDirectory(parent);
        string tmp = full + ".osm-tmp-" + Guid.NewGuid().ToString("N")[..8];
        Directory.CreateDirectory(tmp);
        try
        {
            await WriteAllAsync(a, area, options, tmp, progress, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (Directory.Exists(full)) Directory.Delete(full);   // empty (checked above)
            Directory.Move(tmp, full);
            return full;
        }
        catch
        {
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (IOException) { /* best effort */ } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private async Task WriteAllAsync(AreaAnalysis a, GeoBox area, OsmImportOptions options, string dir, IProgress<OsmProgress>? progress, CancellationToken ct)
    {
        var bounds = a.Bounds;

        progress?.Report(new OsmProgress(OsmStage.Heights, 0, "Fetching elevation data…"));
        await _dem.PrepareAsync(area, Path.Combine(_cache.FolderFor(area), "dem"), progress, ct).ConfigureAwait(false);
        var hb = new HeightBuilder(_dem, bounds, a.Model);
        var heightProgress = new Progress<double>(f => progress?.Report(new OsmProgress(OsmStage.Heights, f, "Resampling the heights to 1 m…")));
        await Task.Run(() => hb.Write(Path.Combine(dir, "heights"), heightProgress, ct), ct).ConfigureAwait(false);

        progress?.Report(new OsmProgress(OsmStage.Roads, 0, "Building the road net…"));
        // ⭐ CE-4409 — roads are OPTIONAL: a failing netconvert imports the terrain WITHOUT roads and says so (a warning, never a stop). 🔒 User, 2026-11-10: "if sumo roadnet fails, it shouldn't fail the whole import."
        string roadsDir = Path.Combine(dir, "roads");
        try
        {
            await _roads.BuildAsync(a.OsmFile, bounds.Frame, roadsDir, ct).ConfigureAwait(false);
            // ⭐ AQ93 E2b — netconvert writes a FLAT net: give it OUR heights (the grid just written), so the roads lie on the ground in SUMO's own sim and in the 3-D map
            string net = Path.Combine(roadsDir, "roads.net.xml");
            if (File.Exists(net)) await Task.Run(() => NetElevation.Apply(net, dir), ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            try { if (Directory.Exists(roadsDir)) Directory.Delete(roadsDir, true); } catch (IOException) { /* best effort */ } catch (UnauthorizedAccessException) { }
            progress?.Report(new OsmProgress(OsmStage.Roads, 1, "No roads — the road net could not be built") { Warning = "No roads were imported — " + e.Message });
        }

        await Task.Run(() =>
        {
            progress?.Report(new OsmProgress(OsmStage.Buildings, 0, "Standing the buildings on the ground…"));
            var buildings = a.Buildings.Buildings.Select(d => BuildingRules.Finish(d, hb.GroundAt)).ToList();
            GeoJsonWriter.WriteBuildings(Path.Combine(dir, InputSetLayout.Buildings), buildings);
            GeoJsonWriter.WriteUnassigned(Path.Combine(dir, InputSetLayout.Unassigned), new List<Opening>());
            ct.ThrowIfCancellationRequested();

            progress?.Report(new OsmProgress(OsmStage.Vegetation, 0, "Trees, walls, water…"));
            var trees = a.Vegetation.Trees.Select(t => (t.X, t.Y, Math.Round(hb.GroundAt(t.X, t.Y), 2))).ToList();
            TreesCsv.Write(Path.Combine(dir, InputSetLayout.Trees), trees);
            GeoJsonWriter.WriteWalls(Path.Combine(dir, InputSetLayout.Walls), a.Walls.Select(w => w.ToFeature()).ToList(), "no walls or fences in this area");
            if (a.Water.Count > 0) GeoJsonWriter.WriteAreas(Path.Combine(dir, InputSetLayout.Water), a.Water.Select(w => w.ToFeature()).ToList());
            if (a.Vegetation.Scrub.Count > 0) GeoJsonWriter.WriteAreas(Path.Combine(dir, InputSetLayout.Scrub), a.Vegetation.Scrub.Select(w => w.ToFeature()).ToList());
            if (a.Vegetation.Woods.Count > 0) GeoJsonWriter.WriteAreas(Path.Combine(dir, InputSetLayout.Woods), a.Vegetation.Woods.Select(w => w.ToFeature()).ToList());
            ct.ThrowIfCancellationRequested();

            progress?.Report(new OsmProgress(OsmStage.Write, 0, "Writing the manifest…"));
            var head = new ManifestHead(options.Name, bounds.Frame, bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY);
            Manifest.Write(dir, head, "source", w =>
            {
                w.WriteString("osm", a.Model.DataTimestamp ?? "unknown");
                w.WriteString("dem", _dem.Name);
                w.WriteString("attribution", PreviewBuilder.Attribution(_dem.Name));
                w.WriteString("licence", "OpenStreetMap data: ODbL 1.0 (https://www.openstreetmap.org/copyright)");
                w.WriteStartArray("box");
                w.WriteNumberValue(area.South); w.WriteNumberValue(area.West); w.WriteNumberValue(area.North); w.WriteNumberValue(area.East);
                w.WriteEndArray();
                w.WriteString("generator", "hrot-terrain-osm");
            });
            var problems = InputSetChecks.Check(dir);
            if (problems.Count > 0) throw new InvalidDataException("the written input set fails its format checks: " + string.Join("; ", problems.Take(5)));
            progress?.Report(new OsmProgress(OsmStage.Write, 1, "Input set written"));
        }, ct).ConfigureAwait(false);
    }
}
