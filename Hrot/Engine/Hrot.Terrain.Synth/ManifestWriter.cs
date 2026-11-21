using System.Globalization;

namespace Hrot.Tools.TerrainSynth;

/// <summary>Writes manifest.json through the shared <see cref="Manifest"/> writer, with the synth tool's "generator" section. No date.</summary>
public static class ManifestWriter
{
    public static void Write(string outDir, SynthProfile p, SynthOptions o)
    {
        var head = new ManifestHead(string.Create(CultureInfo.InvariantCulture, $"synth-{o.Preset}-{o.Seed}"),
                                    new UtmFrame(p.UtmZone, !p.NorthernHemisphere, p.E0, p.N0), p.MinX, p.MinY, p.MaxX, p.MaxY);
        Manifest.Write(outDir, head, "generator", w =>
        {
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
            if (o.Extras) w.WriteBoolean("extras", true);
            if (o.GroundOnly) w.WriteBoolean("groundOnly", true);
            if (o.ZRange is double zRange) w.WriteNumber("zRange", zRange);
            if (o.WaterMode != null) w.WriteString("water", o.WaterMode);
            if (o.RoadScale is double roadScale) w.WriteNumber("roadScale", roadScale);
            if (o.WoodsShare is double woodsShare) w.WriteNumber("woodsShare", woodsShare);
            if (o.Materials != null || o.MaterialsEnabled) w.WriteString("materials", o.MaterialsEnabled ? "on" : "off");
            w.WriteEndObject();
        });
    }
}
