using System.Globalization;
using System.Text.Json;

namespace Hrot.Terrain.InputSet;

/// <summary>What the manifest says.</summary>
public sealed record ManifestInfo(int SchemaVersion, string Name, UtmFrame Frame, double MinX, double MinY, double MaxX, double MaxY,
                                  double AnchorX, double AnchorY, IReadOnlyDictionary<string, (string Sha256, long Bytes)> Files);

/// <summary>
/// ⭐ THE reader of an input set (docs/DESIGN_Terrain_Import_UTM.md §10) — the importer and every tool read through it, never the files directly.
/// The optional files (§10.9) read as empty when absent.
/// </summary>
public sealed class InputSetReader
{
    public string Folder { get; }
    public ManifestInfo Manifest { get; }

    private InputSetReader(string folder, ManifestInfo m) { Folder = folder; Manifest = m; }

    public static InputSetReader Open(string folder)
    {
        string path = Path.Combine(folder, InputSetLayout.Manifest);
        if (!File.Exists(path)) throw new FileNotFoundException($"not an input set: {path} is missing", path);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var r = doc.RootElement;
        int schema = r.GetProperty("schemaVersion").GetInt32();
        if (schema != 1) throw new InvalidDataException($"{path}: schemaVersion {schema} — this reader knows 1");
        var b = r.GetProperty("bounds");
        var anchor = r.TryGetProperty("sumoAnchor", out var a) ? (a[0].GetDouble(), a[1].GetDouble()) : (0.0, 0.0);
        var files = new Dictionary<string, (string, long)>(StringComparer.Ordinal);
        if (r.TryGetProperty("files", out var fl))
            foreach (var f in fl.EnumerateObject())
                files[f.Name] = (f.Value.GetProperty("sha256").GetString() ?? "", f.Value.GetProperty("bytes").GetInt64());
        var m = new ManifestInfo(schema, r.GetProperty("name").GetString() ?? "", ReadFrame(r, path),
                                 b[0].GetDouble(), b[1].GetDouble(), b[2].GetDouble(), b[3].GetDouble(), anchor.Item1, anchor.Item2, files);
        return new InputSetReader(folder, m);
    }

    internal static UtmFrame ReadFrame(JsonElement root, string where)
    {
        if (!root.TryGetProperty("frame", out var f) || !f.TryGetProperty("utm", out var u))
            throw new InvalidDataException($"{where}: no frame.utm");
        string hemi = u.GetProperty("hemisphere").GetString() ?? "N";
        return new UtmFrame(u.GetProperty("zone").GetInt32(), hemi == "S", u.GetProperty("e0").GetDouble(), u.GetProperty("n0").GetDouble());
    }

    public string PathOf(string relative) => Path.Combine(Folder, relative);
    public bool Has(string relative) => File.Exists(PathOf(relative));

    public HeightsInfo Heights => HeightsGrid.ReadInfo(Folder);
    /// <summary>Null when the set has no <c>roads.net.xml</c> (written only with netconvert).</summary>
    public string? RoadsNet => Has(InputSetLayout.RoadsNet) ? PathOf(InputSetLayout.RoadsNet) : null;

    public List<Building> ReadBuildings()
    {
        var list = new List<Building>();
        foreach (var f in Features(InputSetLayout.Buildings))
        {
            var p = f.GetProperty("properties");
            var ring = Ring(f.GetProperty("geometry").GetProperty("coordinates")[0]);
            var storeys = p.GetProperty("storeyZ").EnumerateArray().Select(e => e.GetDouble()).ToList();
            var openings = new List<Opening>();
            if (p.TryGetProperty("openings", out var os))
                foreach (var o in os.EnumerateArray())
                    openings.Add(new Opening(o.GetProperty("type").GetString() ?? "", o.GetProperty("x").GetDouble(), o.GetProperty("y").GetDouble(),
                                             o.GetProperty("z").GetDouble(), o.GetProperty("heading").GetDouble(), o.GetProperty("storey").GetInt32()));
            list.Add(new Building(Str(p, "id") ?? "", ring, p.GetProperty("baseZ").GetDouble(), p.GetProperty("height").GetDouble(),
                                  Str(p, "roof") ?? "flat", p.TryGetProperty("ridgeZ", out var rz) ? rz.GetDouble() : null, storeys, openings,
                                  Str(p, "material"), !(p.TryGetProperty("enterable", out var en) && en.ValueKind == JsonValueKind.False)));
        }
        return list;
    }

    public List<Opening> ReadUnassigned()
    {
        var list = new List<Opening>();
        foreach (var f in Features(InputSetLayout.Unassigned))
        {
            var c = f.GetProperty("geometry").GetProperty("coordinates");
            var p = f.GetProperty("properties");
            list.Add(new Opening(Str(p, "type") ?? "", c[0].GetDouble(), c[1].GetDouble(), c.GetArrayLength() > 2 ? c[2].GetDouble() : 0,
                                 p.GetProperty("heading").GetDouble(), p.TryGetProperty("storey", out var s) ? s.GetInt32() : 0));
        }
        return list;
    }

    public List<(double X, double Y, double Z)> ReadTrees() => TreesCsv.Read(PathOf(InputSetLayout.Trees));

    public List<WallFeature> ReadWalls()
    {
        var list = new List<WallFeature>();
        foreach (var f in Features(InputSetLayout.Walls))
        {
            var p = f.GetProperty("properties");
            list.Add(new WallFeature(Ring(f.GetProperty("geometry").GetProperty("coordinates")), p.GetProperty("height").GetDouble(),
                                     p.TryGetProperty("thickness", out var t) ? t.GetDouble() : 0.3, Str(p, "kind") ?? "wall"));
        }
        return list;
    }

    /// <summary>Water, scrub or woods (§10.9) — empty when the optional file is absent.</summary>
    public List<AreaFeature> ReadAreas(string relative)
    {
        var list = new List<AreaFeature>();
        if (!Has(relative)) return list;
        foreach (var f in Features(relative))
        {
            var p = f.GetProperty("properties");
            list.Add(new AreaFeature(Ring(f.GetProperty("geometry").GetProperty("coordinates")[0]), Str(p, "kind"),
                                     p.TryGetProperty("density", out var d) ? d.GetDouble() : null));
        }
        return list;
    }

    private IEnumerable<JsonElement> Features(string relative)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(PathOf(relative)));
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray()) yield return f.Clone();
    }

    private static string? Str(JsonElement p, string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<(double X, double Y)> Ring(JsonElement coords)
    {
        var ring = new List<(double, double)>(coords.GetArrayLength());
        foreach (var c in coords.EnumerateArray()) ring.Add((c[0].GetDouble(), c[1].GetDouble()));
        return ring;
    }

    internal static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
