// hrot-heights — CE-4365 (L-3, docs/DESIGN_Terrain_Height.md §9): an input set's heights → a terrain's height grid.
//
//   hrot-heights --set DIR --spacing 1 --out FILE.heights      the binary height store (a large terrain)
//   hrot-heights --set DIR --spacing 2 --out FILE.height.asc   an ESRI ASCII grid (a small terrain)
//
// The grid covers the set's manifest bounds in the terrain's LOCAL metres (local = set frame − the bounds' south-west corner, the
// anchor terrain-from-sumo.py uses), every --spacing m. Reads the set through the shared input-set reader (CE-4371).
using System.Globalization;
using Fdp.Toolkit.Terrain;
using Hrot.Terrain.InputSet;

string? set = null, outPath = null; double spacing = 1.0;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--set": set = args[++i]; break;
        case "--out": outPath = args[++i]; break;
        case "--spacing": spacing = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
        default: Console.Error.WriteLine($"unknown argument {args[i]}\nusage: hrot-heights --set DIR --spacing M --out FILE(.heights|.asc)"); return 2;
    }
}
if (set == null || outPath == null || !(spacing > 0)) { Console.Error.WriteLine("usage: hrot-heights --set DIR --spacing M --out FILE(.heights|.asc)"); return 2; }

var sw = System.Diagnostics.Stopwatch.StartNew();
// ⭐ CE-4371: through the ONE input-set reader (Hrot.Terrain.InputSet) — the bounds from the manifest, the floats NORTH row first
var input = InputSetReader.Open(set);
double minX = input.Manifest.MinX, minY = input.Manifest.MinY, maxX = input.Manifest.MaxX, maxY = input.Manifest.MaxY;
var info = input.Heights;
int srcCols = info.Cols, srcRows = info.Rows;
var src = new float[(long)srcCols * srcRows];
HeightsGrid.ReadRows(set, (r, row) => Array.Copy(row, 0, src, (long)r * srcCols, srcCols));
double ulx = info.OriginX, uly = info.OriginY + (srcRows - 1) * info.CellSize;
int cols = (int)Math.Round((maxX - minX) / spacing) + 1, rows = (int)Math.Round((maxY - minY) / spacing) + 1;
var z = TerrainHeightGrid.Resample(src, srcCols, srcRows, ulx, uly, info.CellSize, info.CellSize, HeightsInfo.NoData,
                                   minX, minY, cols, rows, spacing, out long filled);
src = null!;
var grid = new TerrainHeightGrid(System.Numerics.Vector2.Zero, (float)spacing, cols, rows, z);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
if (outPath.EndsWith(TerrainHeightGrid.StoreSuffix, StringComparison.OrdinalIgnoreCase))
    using (var o = File.Create(outPath)) grid.WriteStore(o);
else
    using (var o = new StreamWriter(outPath, false, new System.Text.UTF8Encoding(false))) grid.WriteAsciiGrid(o);
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"heights: {cols} x {rows} posts at {spacing:g} m, z {grid.MinZ:0.0}..{grid.MaxZ:0.0}, {filled} NODATA posts filled -> {outPath} ({new FileInfo(outPath).Length / 1e6:0.0} MB, {sw.Elapsed.TotalSeconds:0.0} s)"));
return 0;
