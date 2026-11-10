// hrot-heights — CE-4365 (L-3, docs/DESIGN_Terrain_Height.md §9): an input set's heights → a terrain's height grid.
//
//   hrot-heights --set DIR --spacing 1 --out FILE.heights      the binary height store (a large terrain)
//   hrot-heights --set DIR --spacing 2 --out FILE.height.asc   an ESRI ASCII grid (a small terrain)
//
// The grid covers the set's manifest bounds in the terrain's LOCAL metres (local = set frame − the bounds' south-west corner, the
// anchor terrain-from-sumo.py uses), every --spacing m. ⚠ Reads heights.f32 + heights.hdr directly until the coordinator's input-set reader
// (CE-4371) lands; then it reads through that (one reader).
using System.Globalization;
using System.Text.Json;
using Fdp.Toolkit.Terrain;

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
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(set, "manifest.json")));
var b = manifest.RootElement.GetProperty("bounds");
double minX = b[0].GetDouble(), minY = b[1].GetDouble(), maxX = b[2].GetDouble(), maxY = b[3].GetDouble();

var hdr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
foreach (var line in File.ReadAllLines(Path.Combine(set, "heights", "heights.hdr")))
{
    var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    if (p.Length >= 2) hdr[p[0]] = p[1];
}
double D(string k) => double.Parse(hdr[k], CultureInfo.InvariantCulture);
int srcCols = (int)D("NCOLS"), srcRows = (int)D("NROWS");
if (!BitConverter.IsLittleEndian || !hdr.TryGetValue("BYTEORDER", out var bo) || bo != "I") throw new InvalidDataException("heights.f32 must be little-endian (BYTEORDER I).");
var src = new float[(long)srcCols * srcRows];
using (var fs = File.OpenRead(Path.Combine(set, "heights", "heights.f32")))
{
    var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(src.AsSpan());
    int read = 0;
    while (read < bytes.Length) { int n = fs.Read(bytes[read..]); if (n <= 0) throw new InvalidDataException("heights.f32 is truncated."); read += n; }
}
int cols = (int)Math.Round((maxX - minX) / spacing) + 1, rows = (int)Math.Round((maxY - minY) / spacing) + 1;
var z = TerrainHeightGrid.Resample(src, srcCols, srcRows, D("ULXMAP"), D("ULYMAP"), D("XDIM"), D("YDIM"), (float)D("NODATA"),
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
