using System.Text;
using System.Text.Json;

namespace Hrot.Tools.TerrainSynth;

/// <summary>Writes buildings, loose openings and an empty collection in the GeoJSON layout of the input set (compact JSON).</summary>
public static class GeoJsonWriter
{
    private const string FrameNote = "local metres of the terrain frame (see manifest)";
    private const string ZNote = "absolute";
    private const string HeadingNote = "compass degrees, 0 = north (+y), clockwise, outward";

    public static void WriteBuildings(string path, IReadOnlyList<Building> b)
    {
        WriteCollection(path, null, w =>
        {
            foreach (var building in b)
            {
                w.WriteStartObject();
                w.WriteString("type", "Feature");
                w.WriteStartObject("geometry");
                w.WriteString("type", "Polygon");
                w.WriteStartArray("coordinates");
                w.WriteStartArray();
                foreach (var (x, y) in building.Footprint)
                {
                    w.WriteStartArray();
                    w.WriteNumberValue(x);
                    w.WriteNumberValue(y);
                    w.WriteEndArray();
                }
                w.WriteEndArray();
                w.WriteEndArray();
                w.WriteEndObject();
                w.WriteStartObject("properties");
                w.WriteString("id", building.Id);
                w.WriteNumber("baseZ", building.BaseZ);
                w.WriteNumber("height", building.Height);
                w.WriteString("roof", building.Roof);
                if (building.Roof == "pitched" && building.RidgeZ is double ridge) w.WriteNumber("ridgeZ", ridge);
                w.WriteStartArray("storeyZ");
                foreach (var z in building.StoreyZ) w.WriteNumberValue(z);
                w.WriteEndArray();
                w.WriteStartArray("openings");
                foreach (var o in building.Openings)
                {
                    w.WriteStartObject();
                    w.WriteString("type", o.Type);
                    w.WriteNumber("x", o.X);
                    w.WriteNumber("y", o.Y);
                    w.WriteNumber("z", o.Z);
                    w.WriteNumber("heading", o.Heading);
                    w.WriteNumber("storey", o.Storey);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
                w.WriteEndObject();
            }
        });
    }

    public static void WriteUnassigned(string path, IReadOnlyList<Opening> u)
    {
        WriteCollection(path, null, w =>
        {
            foreach (var o in u)
            {
                w.WriteStartObject();
                w.WriteString("type", "Feature");
                w.WriteStartObject("geometry");
                w.WriteString("type", "Point");
                w.WriteStartArray("coordinates");
                w.WriteNumberValue(o.X);
                w.WriteNumberValue(o.Y);
                w.WriteNumberValue(o.Z);
                w.WriteEndArray();
                w.WriteEndObject();
                w.WriteStartObject("properties");
                w.WriteString("type", o.Type);
                w.WriteNumber("heading", o.Heading);
                w.WriteEndObject();
                w.WriteEndObject();
            }
        });
    }

    public static void WriteEmpty(string path, string note) => WriteCollection(path, note, _ => { });

    private static void WriteCollection(string path, string? note, Action<Utf8JsonWriter> features)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        using var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = false });
        w.WriteStartObject();
        w.WriteString("type", "FeatureCollection");
        w.WriteStartObject("hrotInput");
        w.WriteString("frame", FrameNote);
        w.WriteString("z", ZNote);
        w.WriteString("heading", HeadingNote);
        w.WriteEndObject();
        if (note != null) w.WriteString("note", note);
        w.WriteStartArray("features");
        features(w);
        w.WriteEndArray();
        w.WriteEndObject();
    }
}
