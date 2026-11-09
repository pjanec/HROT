using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hrot.Tools.TerrainSynth;

/// <summary>Writes manifest.json: frame, bounds, generator settings and the SHA-256, size and counts of every other file. No date.</summary>
public static class ManifestWriter
{
    public static void Write(string outDir, SynthProfile p, SynthOptions o)
    {
        var files = Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Rel: Path.GetRelativePath(outDir, f).Replace('\\', '/')))
            .Where(f => f.Rel != "manifest.json")
            .OrderBy(f => f.Rel, StringComparer.Ordinal)
            .ToList();

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("schemaVersion", 1);
            w.WriteString("name", string.Create(CultureInfo.InvariantCulture, $"synth-{o.Preset}-{o.Seed}"));
            w.WriteStartObject("frame");
            w.WriteStartObject("utm");
            w.WriteNumber("zone", p.UtmZone);
            w.WriteString("hemisphere", p.NorthernHemisphere ? "N" : "S");
            w.WriteNumber("e0", p.E0);
            w.WriteNumber("n0", p.N0);
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteString("axes", "x east, y north, z up (absolute); headings compass degrees, 0 = north, clockwise");
            w.WriteStartArray("bounds");
            w.WriteNumberValue(p.MinX); w.WriteNumberValue(p.MinY); w.WriteNumberValue(p.MaxX); w.WriteNumberValue(p.MaxY);
            w.WriteEndArray();
            w.WriteStartArray("sumoAnchor");
            w.WriteNumberValue(0); w.WriteNumberValue(0);
            w.WriteEndArray();
            w.WriteStartObject("generator");
            w.WriteString("name", "hrot-terrain-synth");
            w.WriteString("preset", o.Preset);
            w.WriteNumber("seed", o.Seed);
            w.WriteStartObject("overrides");
            if (o.Size is double size) w.WriteNumber("size", size);
            if (o.HeightSpacing is double spacing) w.WriteNumber("heightSpacing", spacing);
            if (o.Buildings is int buildings) w.WriteNumber("buildings", buildings);
            if (o.Trees is int trees) w.WriteNumber("trees", trees);
            // options are recorded when they are on (also by default), so a default-off set keeps its manifest
            if (o.Noise != null || o.NoiseEnabled) w.WriteString("noise", o.NoiseEnabled ? "real" : "none");
            if (o.Terraces != null || o.TerracesEnabled) w.WriteString("terraces", o.TerracesEnabled ? "on" : "off");
            if (o.Materials != null || o.MaterialsEnabled) w.WriteString("materials", o.MaterialsEnabled ? "on" : "off");
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteStartObject("files");
            foreach (var (full, rel) in files)
            {
                w.WriteStartObject(rel);
                w.WriteString("sha256", Convert.ToHexString(Hash(full)).ToLowerInvariant());
                w.WriteNumber("bytes", new FileInfo(full).Length);
                WriteCounts(w, full, rel, p);
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        string text = Encoding.UTF8.GetString(ms.ToArray()).Replace("\r\n", "\n") + "\n";
        File.WriteAllText(Path.Combine(outDir, "manifest.json"), text, new UTF8Encoding(false));
    }

    private static byte[] Hash(string path)
    {
        using var fs = File.OpenRead(path);
        return SHA256.HashData(fs);
    }

    private static void WriteCounts(Utf8JsonWriter w, string full, string rel, SynthProfile p)
    {
        if (rel == "heights/heights.f32")
        {
            w.WriteNumber("rows", (long)Math.Round(p.Height / p.HeightSpacing) + 1);
            w.WriteNumber("cols", (long)Math.Round(p.Width / p.HeightSpacing) + 1);
        }
        else if (rel.EndsWith(".geojson", StringComparison.Ordinal))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(full));
            w.WriteNumber("features", doc.RootElement.GetProperty("features").GetArrayLength());
        }
        else if (rel.EndsWith(".csv", StringComparison.Ordinal))
        {
            long lines = 0;
            foreach (var _ in File.ReadLines(full)) lines++;
            w.WriteNumber("rows", Math.Max(0, lines - 1));
        }
    }
}
