using System.Globalization;
using Hrot.Terrain.Import;
using Hrot.Terrain.Osm;
using Hrot.Terrain.Osm.Dem;
using Hrot.Terrain.Osm.Roads;

namespace Hrot.Tools.TerrainOsm;

/// <summary>
/// ⭐ E-12: <c>hrot-terrain-osm --bbox s,w,n,e --name N [--out DIR] [--preview-only] [--osm-file F] [--dem-dir D]</c>.
/// Composes the REAL sources: Overpass (or <c>--osm-file</c>, offline), the Copernicus DEM (or <c>--dem-dir</c>, offline tiles) and netconvert.
/// </summary>
public static class OsmCli
{
    public const string Usage = "usage: hrot-terrain-osm --bbox south,west,north,east --name NAME [--out DIR] [--preview-only] [--osm-file FILE] [--dem-dir DIR] [--import [--replace] [--terrains DIR]]";

    public sealed record Args(GeoBox Box, string Name, string Out, bool PreviewOnly, string? OsmFile, string? DemDir, bool Import = false, bool Replace = false, string? Terrains = null);

    public static Args Parse(string[] args)
    {
        string? bbox = null, name = null, outDir = null, osm = null, dem = null;
        bool previewOnly = false, import = false, replace = false;
        string? terrains = null;
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException(args[i] + " needs a value");
            switch (args[i])
            {
                case "--bbox": bbox = Next(); break;
                case "--name": name = Next(); break;
                case "--out": outDir = Next(); break;
                case "--preview-only": previewOnly = true; break;
                case "--osm-file": osm = Next(); break;
                case "--dem-dir": dem = Next(); break;
                case "--import": import = true; break;
                case "--replace": replace = true; break;
                case "--terrains": terrains = Next(); break;
                default: throw new ArgumentException("unknown argument " + args[i]);
            }
        }
        if (import && previewOnly) throw new ArgumentException("--import needs a full write, not --preview-only");
        if (bbox == null) throw new ArgumentException("--bbox is required");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("--name is required");
        var p = bbox.Split(',');
        if (p.Length != 4 || p.Any(v => !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            throw new ArgumentException("--bbox must be four numbers: south,west,north,east (degrees)");
        double[] v4 = p.Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        return new Args(new GeoBox(v4[0], v4[1], v4[2], v4[3]), name, outDir ?? Path.Combine("terrain-input", name), previewOnly, osm, dem, import, replace, terrains);
    }

    public static async Task<int> RunAsync(string[] argv, TextWriter output, TextWriter error, CancellationToken ct)
    {
        Args a;
        try { a = Parse(argv); }
        catch (ArgumentException e) { error.WriteLine(e.Message); error.WriteLine(Usage); return 2; }

        var (w, h, refusal) = AreaFrame.Measure(a.Box);
        if (refusal != null) { error.WriteLine(refusal); return 2; }

        string cacheRoot = a.OsmFile != null ? Path.Combine(Path.GetTempPath(), "hrot-terrain-osm-" + Guid.NewGuid().ToString("N")[..8])
                                             : Path.Combine(Path.GetTempPath(), "hrot-terrain-osm-cache");
        try
        {
            var http = HttpRetry.CreateClient();
            IOsmSource osm = a.OsmFile != null ? new FileOsmSource(a.OsmFile) : new OverpassSource(http);
            IDemSource dem = a.DemDir != null ? new CopernicusDem(new FolderRangeSource(a.DemDir)) : CopernicusDem.Online(http, Path.Combine(cacheRoot, "dem-tiles"));
            var pipeline = OsmPipelines.ForAreas(osm, dem, new DownloadCache(cacheRoot));   // ⭐ the ONE composition (CLI + editor wizard)
            var progress = new SyncProgress(m => output.WriteLine($"[{m.Stage}] {m.Message}"));
            var options = new OsmImportOptions { Name = a.Name };
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"area {w:0} × {h:0} m"));

            var preview = await pipeline.PreviewAsync(a.Box, options, progress, ct).ConfigureAwait(false);
            var c = preview.Counts;
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"enterable buildings {c.Buildings}, doors {c.Doors}, windows {c.Windows}, trees ≈ {c.EstimatedTrees:0} (lone {c.LoneTrees}, woods {c.WoodsAreaM2 / 10000:0.0} ha), roads {c.RoadKm:0.0} km, walls {c.Walls}, water {c.WaterAreas}"));
            foreach (var wn in preview.Warnings) output.WriteLine("  - " + wn);
            output.WriteLine($"budget: {preview.Verdict}" + (preview.VerdictReason != null ? " — " + preview.VerdictReason : ""));
            output.WriteLine(preview.Attribution);
            if (preview.Verdict == BudgetVerdict.Refused) return 2;
            if (a.PreviewOnly) return 0;

            string folder = await pipeline.WriteInputSetAsync(a.Box, options, a.Out, progress, ct).ConfigureAwait(false);
            output.WriteLine("input set written to " + Path.GetFullPath(folder));
            if (a.Import)
            {
                var importer = new TerrainImporter(a.Terrains);
                string terrain = await importer.ImportAsync(folder, a.Name, a.Replace, null, ct).ConfigureAwait(false);
                importer.LastReport!.WriteTo(output);
                output.WriteLine("terrain written to " + Path.GetFullPath(terrain));
            }
            return 0;
        }
        catch (OperationCanceledException) { error.WriteLine("cancelled — nothing was written"); return 130; }
        catch (Exception e) when (e is OsmServiceException or IOException or InvalidOperationException or ArgumentException or InvalidDataException)
        {
            error.WriteLine("failed: " + e.Message);
            return 1;
        }
        finally
        {
            if (a.OsmFile != null) { try { Directory.Delete(cacheRoot, true); } catch (IOException) { } }
        }
    }

    private sealed class SyncProgress : IProgress<OsmProgress>
    {
        private readonly Action<OsmProgress> _f;
        private OsmStage? _last;
        public SyncProgress(Action<OsmProgress> f) => _f = f;
        public void Report(OsmProgress value) { if (_last != value.Stage || value.Fraction >= 1) { _last = value.Stage; _f(value); } }
    }
}
