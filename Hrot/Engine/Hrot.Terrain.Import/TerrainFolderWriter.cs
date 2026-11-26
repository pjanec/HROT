using System.Text;
using System.Text.Json;
using Fdp.Modules.Geographic.Transforms;

namespace Hrot.Terrain.Import;

/// <summary>A wall, fence or hedge for the world file (terrain-local).</summary>
internal sealed record WorldWall(IReadOnlyList<(double X, double Y)> Line, double Height, double Thickness, string Kind, string Material);

/// <summary>
/// ⭐ The terrain folder as <c>scripts/terrain-from-sumo.py</c> writes it, in C#: <c>terrain.json</c> (name, origin, world, sumoNet + anchor),
/// <c>&lt;name&gt;.world.geojson</c> (the <c>hrot</c> block, features), <c>buildings/&lt;id&gt;.building.json</c>, the net copied unchanged, the height store.
/// I-b: <c>origin</c> = the lat / lon of the terrain's (0, 0) AND the exact <c>frame</c> (UTM zone, hemisphere, e0, n0 and the corner) beside it —
/// the terrain definition parser reads only the keys it knows, so the extra key is tolerated.
/// </summary>
internal static class TerrainFolderWriter
{
    public static void WriteTemplate(string folder, string templateName, string json)
    {
        string dir = Path.Combine(folder, "buildings");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, templateName + ".building.json"), json, new UTF8Encoding(false));
    }

    public static void WriteWorld(string path, string description, double width, double height, string? heightGrid,
                                  IReadOnlyList<(IReadOnlyList<(double X, double Y)> Ring, string? Kind)> water, IReadOnlyList<Wood> woods,
                                  IReadOnlyList<BuiltBuilding> buildings, IReadOnlyList<WorldWall> walls, IReadOnlyList<(double X, double Y)> trees,
                                  IReadOnlyList<EntranceSteps.Ramp>? entrances = null,
                                  IReadOnlyList<(string Structure, IReadOnlyList<Fdp.Toolkit.Terrain.DeckGeometry.Part> Parts)>? bridges = null)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        using var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = false });
        w.WriteStartObject();
        w.WriteString("type", "FeatureCollection");
        w.WriteStartObject("hrot");
        w.WriteNumber("schemaVersion", 1);
        w.WriteStartArray("bounds"); w.WriteNumberValue(0); w.WriteNumberValue(0); w.WriteNumberValue(width); w.WriteNumberValue(height); w.WriteEndArray();
        w.WriteNumber("groundZ", 0);
        w.WriteString("description", description);
        if (heightGrid != null) w.WriteString("heightGrid", heightGrid);
        w.WriteEndObject();
        w.WriteStartArray("features");

        void Ring(IReadOnlyList<(double X, double Y)> ring)
        {
            w.WriteStartArray("coordinates"); w.WriteStartArray();
            foreach (var (x, y) in ring) { w.WriteStartArray(); w.WriteNumberValue(x); w.WriteNumberValue(y); w.WriteEndArray(); }
            w.WriteStartArray(); w.WriteNumberValue(ring[0].X); w.WriteNumberValue(ring[0].Y); w.WriteEndArray();
            w.WriteEndArray(); w.WriteEndArray();
        }
        void Feature(Action props, string geomType, Action geom)
        {
            w.WriteStartObject();
            w.WriteString("type", "Feature");
            w.WriteStartObject("properties"); props(); w.WriteEndObject();
            w.WriteStartObject("geometry"); w.WriteString("type", geomType); geom(); w.WriteEndObject();
            w.WriteEndObject();
        }

        int i = 0;
        foreach (var (ring, kind) in water)
            Feature(() => { w.WriteString("kind", "surface"); w.WriteString("surface", "water"); w.WriteString("label", (kind ?? "water") + " " + i++); }, "Polygon", () => Ring(ring));
        i = 0;
        foreach (var wd in woods)
            Feature(() => { w.WriteString("kind", "surface"); w.WriteString("surface", "forest"); w.WriteString("label", "wood " + i++); w.WriteNumber("density", wd.Density); },
                    "Polygon", () => Ring(wd.Ring));
        foreach (var b in buildings)
        {
            if (b.Solid)
                Feature(() => { w.WriteString("kind", "building"); w.WriteNumber("height", b.SolidHeight); w.WriteNumber("baseZ", b.BaseZ); w.WriteNumber("floors", b.Floors);
                                w.WriteString("material", b.Material); w.WriteString("label", b.Id); }, "Polygon", () => Ring(b.Ring));
            else
                Feature(() => { w.WriteString("kind", "building"); w.WriteString("template", b.TemplateName); w.WriteString("label", b.Id);
                                w.WriteNumber("rotation", 0); w.WriteNumber("baseZ", b.BaseZ); }, "Point",
                        () => { w.WriteStartArray("coordinates"); w.WriteNumberValue(b.Origin.X); w.WriteNumberValue(b.Origin.Y); w.WriteEndArray(); });
            if (b.CapHeight > 0)   // I-h: a pitched roof = a flat roof + a SOLID cap from the eaves to the ridge — a template's and (ui, 2026-11-10) a solid building's alike
                Feature(() => { w.WriteString("kind", "building"); w.WriteNumber("height", b.CapHeight); w.WriteNumber("baseZ", b.CapBaseZ); w.WriteNumber("floors", 0);
                                w.WriteString("material", b.Material); w.WriteString("label", b.Id + "-roof"); }, "Polygon", () => Ring(b.Ring));
        }
        // ⭐ CE-5056 ④ — the entrance ramps: ground-relative (each vertex's z is its height above the ground under it; the foot's 0 is buried by the parser)
        foreach (var r in entrances ?? Array.Empty<EntranceSteps.Ramp>())
            Feature(() => { w.WriteString("kind", "ramp"); w.WriteString("zRef", "ground"); w.WriteBoolean("steps", true); w.WriteString("label", r.Building + "-steps"); }, "Polygon", () =>
            {
                w.WriteStartArray("coordinates"); w.WriteStartArray();
                void V((double X, double Y) p, double z) { w.WriteStartArray(); w.WriteNumberValue(Math.Round(p.X, 3)); w.WriteNumberValue(Math.Round(p.Y, 3)); w.WriteNumberValue(z); w.WriteEndArray(); }
                V(r.TopA, r.TopAboveA); V(r.TopB, r.TopAboveB); V(r.FootB, 0); V(r.FootA, 0); V(r.TopA, r.TopAboveA);
                w.WriteEndArray(); w.WriteEndArray();
            });
        // ⭐ CE-5057 / CE-11063 (DESIGN_Terrain_OSM_Bridges D-6) — the OSM bridges: the authored bridge's own parts (slabs, ramps) under one collapsible structure,
        //   ground-relative (each corner's z is its height above the ground under it — the deck runs straight between the banks over the valley)
        foreach (var (structure, parts) in bridges ?? Array.Empty<(string, IReadOnlyList<Fdp.Toolkit.Terrain.DeckGeometry.Part>)>())
            for (int pi = 0; pi < parts.Count; pi++)
            {
                var q = parts[pi];
                string kind = q.Kind == Fdp.Toolkit.Terrain.TerrainWalkableKind.Ramp ? "ramp" : "slab";
                string label = structure + "/" + pi.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Feature(() => { w.WriteString("kind", kind); w.WriteString("zRef", "ground"); w.WriteString("structure", structure); w.WriteString("label", label); }, "Polygon", () =>
                {
                    w.WriteStartArray("coordinates"); w.WriteStartArray();
                    for (int c = 0; c <= 4; c++)
                    {
                        var (at, above) = q[c % 4];
                        w.WriteStartArray(); w.WriteNumberValue(Math.Round(at.X, 3)); w.WriteNumberValue(Math.Round(at.Y, 3)); w.WriteNumberValue(Math.Round(above, 3)); w.WriteEndArray();
                    }
                    w.WriteEndArray(); w.WriteEndArray();
                });
            }
        foreach (var wl in walls)
            Feature(() => { w.WriteString("kind", wl.Kind); w.WriteNumber("height", wl.Height); w.WriteNumber("thickness", wl.Thickness); w.WriteString("material", wl.Material); },
                    "LineString", () =>
                    {
                        w.WriteStartArray("coordinates");
                        foreach (var (x, y) in wl.Line) { w.WriteStartArray(); w.WriteNumberValue(x); w.WriteNumberValue(y); w.WriteEndArray(); }
                        w.WriteEndArray();
                    });
        foreach (var (x, y) in trees)
            Feature(() => w.WriteString("kind", "tree"), "Point", () => { w.WriteStartArray("coordinates"); w.WriteNumberValue(x); w.WriteNumberValue(y); w.WriteEndArray(); });
        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// <summary>⭐ <c>CE-5060</c> — THE one rule for an imported terrain's origin: the geodetic point of its (0, 0) — the input set's south-west corner
    /// (<paramref name="swX"/>, <paramref name="swY"/>) in <paramref name="frame"/>. Written by <see cref="WriteTerrainJson"/> and read ahead of an
    /// import by <c>SourceImport.TryReadInputDefinition</c>, so both answer the same origin by construction.</summary>
    public static (double Lat, double Lon) OriginOf(UtmFrame frame, double swX, double swY) => Utm.ToGeodetic(frame.E0 + swX, frame.N0 + swY, frame.Zone, frame.South);

    public static void WriteTerrainJson(string path, string name, string worldFile, UtmFrame frame, double swX, double swY, string? netFile, double anchorX, double anchorY, string? roadsFile = null, string? inputManifestSha256 = null, int importerRevision = 0)
    {
        var (lat, lon) = OriginOf(frame, swX, swY);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("$comment", "Generated by hrot-terrain-import (CE-4368) from an input set. origin = the lat / lon of the terrain's (0, 0) (the south-west corner of the set's bounds); " +
                                      "frame = the exact UTM frame of that corner (for the UtmTransform); the SUMO net is placed by an anchor (E1b).");
            w.WriteNumber("schemaVersion", 2);
            w.WriteStartObject("origin"); w.WriteNumber("lat", lat); w.WriteNumber("lon", lon); w.WriteNumber("alt", 0.0); w.WriteEndObject();
            w.WriteStartObject("frame");
            w.WriteStartObject("utm"); w.WriteNumber("zone", frame.Zone); w.WriteString("hemisphere", frame.South ? "S" : "N");
            w.WriteNumber("e0", frame.E0); w.WriteNumber("n0", frame.N0); w.WriteEndObject();
            w.WriteStartArray("corner"); w.WriteNumberValue(swX); w.WriteNumberValue(swY); w.WriteEndArray();
            w.WriteEndObject();
            w.WriteString("name", name);
            w.WriteString("world", worldFile);
            // ⭐ CE-11037 (UTM §13 F-4) — the sha256 of the input set's manifest.json this folder was imported from: a changed input is told by it (the manifest hashes every file)
            if (inputManifestSha256 != null) w.WriteString("inputManifestSha256", inputManifestSha256);
            if (importerRevision > 0) w.WriteNumber(SourceImport.RecordedRevisionKey, importerRevision);   // ⭐ CE-5056 — what generated it (a changed generator re-imports)
            if (roadsFile != null) { w.WriteStartArray("roadNetworks"); w.WriteStringValue(roadsFile); w.WriteEndArray(); }   // ⭐ CE-6001 — the vehicles' road graph, from the same net
            if (netFile != null)
            {
                w.WriteStartObject("sumoNet");
                w.WriteString("file", netFile);
                w.WriteStartArray("anchor"); w.WriteNumberValue(anchorX); w.WriteNumberValue(anchorY); w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        File.WriteAllText(path, Encoding.UTF8.GetString(ms.ToArray()).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
    }
}
