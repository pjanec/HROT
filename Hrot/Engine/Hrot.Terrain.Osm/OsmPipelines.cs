using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Terrain.Osm.Dem;
using Hrot.Terrain.Osm.Roads;

namespace Hrot.Terrain.Osm;

/// <summary>
/// ⭐ CE-4375 (ui; OSM §6b) — the ONE place an <see cref="IOsmTerrainPipeline"/> is composed, for the CLI and the editor's wizard alike.
/// ⭐ Why it exists: <see cref="NetconvertRoadBuilder"/> is bound to ONE area (its local bounds keep the edges inside), while the wizard's pipeline
/// is composed before the user has picked a box and is asked about several boxes in a row. <see cref="ForAreas"/> builds the area's
/// <see cref="OsmTerrainPipeline"/> on demand (the last one is kept, so Preview then Write of the same box is one pipeline) and shares the
/// <see cref="DownloadCache"/>, so the download still happens once (§4a "downloads are cached per area").
/// </summary>
public static class OsmPipelines
{
    /// <summary>The online pipeline: Overpass + Copernicus GLO-30 (tiles cached under <paramref name="cacheRoot"/>) + netconvert.</summary>
    public static IOsmTerrainPipeline Online(string cacheRoot, HttpClient? http = null)
    {
        http ??= HttpRetry.CreateClient();
        return ForAreas(new OverpassSource(http), CopernicusDem.Online(http, Path.Combine(cacheRoot, "dem-tiles")), new DownloadCache(cacheRoot));
    }

    /// <summary>A pipeline over the given sources whose road builder is made for each area asked about.</summary>
    public static IOsmTerrainPipeline ForAreas(IOsmSource osm, IDemSource dem, DownloadCache cache, OsmBudget? budget = null,
                                               Func<AreaBounds, IRoadBuilder>? roads = null)
        => new AreaBound(osm, dem, cache, budget, roads ?? NetconvertFor);

    private static IRoadBuilder NetconvertFor(AreaBounds b) => new NetconvertRoadBuilder(b.MinX, b.MinY, b.MaxX, b.MaxY);

    private sealed class AreaBound : IOsmTerrainPipeline
    {
        private readonly IOsmSource _osm;
        private readonly IDemSource _dem;
        private readonly DownloadCache _cache;
        private readonly OsmBudget? _budget;
        private readonly Func<AreaBounds, IRoadBuilder> _roads;
        private readonly object _lock = new();
        private (GeoBox Area, OsmTerrainPipeline Pipeline)? _last;

        public AreaBound(IOsmSource osm, IDemSource dem, DownloadCache cache, OsmBudget? budget, Func<AreaBounds, IRoadBuilder> roads)
        {
            _osm = osm ?? throw new ArgumentNullException(nameof(osm));
            _dem = dem ?? throw new ArgumentNullException(nameof(dem));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _budget = budget;
            _roads = roads;
        }

        public double MaxSideMetres => AreaFrame.MaxSideMetres;

        public (double WidthM, double HeightM, string? Refusal) Measure(GeoBox area) => AreaFrame.Measure(area);

        public Task<OsmPreview> PreviewAsync(GeoBox area, OsmImportOptions options, IProgress<OsmProgress>? progress, CancellationToken cancellation)
            => For(area).PreviewAsync(area, options, progress, cancellation);

        public Task<string> WriteInputSetAsync(GeoBox area, OsmImportOptions options, string outputFolder, IProgress<OsmProgress>? progress, CancellationToken cancellation)
            => For(area).WriteInputSetAsync(area, options, outputFolder, progress, cancellation);

        /// <summary>The area's pipeline (the last one kept: Preview then Write of one box share it).</summary>
        internal OsmTerrainPipeline For(GeoBox area)
        {
            lock (_lock)
            {
                if (_last is { } l && l.Area == area) return l.Pipeline;
                var p = new OsmTerrainPipeline(_osm, _dem, _roads(AreaFrame.FromBox(area)), _cache, _budget);
                _last = (area, p);
                return p;
            }
        }
    }
}
