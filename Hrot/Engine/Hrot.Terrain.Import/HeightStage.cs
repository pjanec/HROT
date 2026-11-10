using Fdp.Toolkit.Terrain;

namespace Hrot.Terrain.Import;

/// <summary>
/// ⭐ I-a / I-k: the input set's heights → the terrain's 1 m height STORE (<c>.heights</c>) over the manifest bounds in TERRAIN-LOCAL metres
/// (input local − the bounds' south-west corner), through the existing <see cref="TerrainHeightGrid.Resample"/> / <c>WriteStore</c> — as
/// <c>hrot-heights</c> calls them. NODATA posts take the nearest valid height.
/// </summary>
internal static class HeightStage
{
    public static void Run(InputSetReader set, string storePath, ImportReport rep, CancellationToken ct, IProgress<ImportProgress>? progress)
    {
        var m = set.Manifest;
        var info = set.Heights;
        int srcCols = info.Cols, srcRows = info.Rows;
        var src = new float[(long)srcCols * srcRows];
        HeightsGrid.ReadRows(set.Folder, (r, row) =>
        {
            Array.Copy(row, 0, src, (long)r * srcCols, srcCols);
            if ((r & 255) == 0) { ct.ThrowIfCancellationRequested(); progress?.Report(new ImportProgress("heights", 0.5 * r / srcRows)); }
        });
        double ulx = info.OriginX, uly = info.OriginY + (srcRows - 1) * info.CellSize;
        int cols = (int)Math.Round(m.MaxX - m.MinX) + 1, rows = (int)Math.Round(m.MaxY - m.MinY) + 1;
        ct.ThrowIfCancellationRequested();
        var z = TerrainHeightGrid.Resample(src, srcCols, srcRows, ulx, uly, info.CellSize, info.CellSize, HeightsInfo.NoData,
                                           m.MinX, m.MinY, cols, rows, 1.0, out long filled);
        src = null!;
        ct.ThrowIfCancellationRequested();
        var grid = new TerrainHeightGrid(System.Numerics.Vector2.Zero, 1f, cols, rows, z);
        using (var o = File.Create(storePath)) grid.WriteStore(o);
        rep.HeightCols = cols; rep.HeightRows = rows; rep.HeightPostsFilled = filled;
        progress?.Report(new ImportProgress("heights", 1));
    }
}
