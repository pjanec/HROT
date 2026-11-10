using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hrot.Terrain.InputSet;

/// <summary>Writes <c>manifest.json</c>: the fixed head, the producer's own section, and the SHA-256, size and counts of every other file. No date.</summary>
public static class Manifest
{
    /// <param name="sectionName">the producer's section ("generator" for the synth tool, "source" for the OSM pipeline)</param>
    /// <param name="section">writes the section's members (the object is opened and closed here)</param>
    public static void Write(string outDir, ManifestHead head, string sectionName, Action<Utf8JsonWriter> section)
    {
        var files = Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Rel: Path.GetRelativePath(outDir, f).Replace('\\', '/')))
            .Where(f => f.Rel != InputSetLayout.Manifest)
            .OrderBy(f => f.Rel, StringComparer.Ordinal)
            .ToList();

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("schemaVersion", 1);
            w.WriteString("name", head.Name);
            WriteFrame(w, head.Frame);
            w.WriteString("axes", "x east, y north, z up (absolute); headings compass degrees, 0 = north, clockwise");
            w.WriteStartArray("bounds");
            w.WriteNumberValue(head.MinX); w.WriteNumberValue(head.MinY); w.WriteNumberValue(head.MaxX); w.WriteNumberValue(head.MaxY);
            w.WriteEndArray();
            w.WriteStartArray("sumoAnchor");
            WriteNum(w, head.AnchorX); WriteNum(w, head.AnchorY);
            w.WriteEndArray();
            w.WriteStartObject(sectionName);
            section(w);
            w.WriteEndObject();

            w.WriteStartObject("files");
            foreach (var (full, rel) in files)
            {
                w.WriteStartObject(rel);
                w.WriteString("sha256", Sha256(full));
                w.WriteNumber("bytes", new FileInfo(full).Length);
                WriteCounts(w, outDir, full, rel);
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        string text = Encoding.UTF8.GetString(ms.ToArray()).Replace("\r\n", "\n") + "\n";
        File.WriteAllText(Path.Combine(outDir, InputSetLayout.Manifest), text, new UTF8Encoding(false));
    }

    internal static void WriteFrame(Utf8JsonWriter w, UtmFrame f)
    {
        w.WriteStartObject("frame");
        w.WriteStartObject("utm");
        w.WriteNumber("zone", f.Zone);
        w.WriteString("hemisphere", f.South ? "S" : "N");
        w.WriteNumber("e0", f.E0);
        w.WriteNumber("n0", f.N0);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    // an integral anchor is written as an integer (the synth set's "[0, 0]")
    private static void WriteNum(Utf8JsonWriter w, double v)
    {
        if (v == Math.Floor(v) && Math.Abs(v) < 1e15) w.WriteNumberValue((long)v); else w.WriteNumberValue(v);
    }

    public static string Sha256(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    private static void WriteCounts(Utf8JsonWriter w, string outDir, string full, string rel)
    {
        if (rel == InputSetLayout.HeightsF32)
        {
            var info = HeightsGrid.ReadInfo(outDir);
            w.WriteNumber("rows", (long)info.Rows);
            w.WriteNumber("cols", (long)info.Cols);
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
