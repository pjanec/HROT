using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-11052</c> — a terrain's wall panels and their openings as JSON, IN THE ORDER HROT's own damage code indexes them: per WALL GROUP
    /// (<see cref="WallGroups"/>: a building, then each free-wall line) keyed by <see cref="BuildingDamages.KeyOf"/> — <c>"&lt;terrain&gt;/&lt;label&gt;"</c>, the key
    /// <see cref="BuildingDamage"/> uses on the wire — its panels in <see cref="BuildingPanelIndex"/> rank order (the wire's <c>BuildingDamage.Panel</c> /
    /// <c>WindowDamage.Panel</c>, within the group), and on each panel its <see cref="TerrainOpening"/>s in list order (the wire's <c>Opening</c>).
    /// <para>⛔ Nothing here re-derives the split: the panels are those the <see cref="TerrainWorld"/> the loader built holds, the rank is <see cref="BuildingPanelIndex.ToGlobal"/>
    /// (the one the save, the load and the wire use). The tool <c>hrot-building-panels</c> only reads a terrain folder through <see cref="TerrainFolderWorld"/> and calls this.
    /// 📄 docs/designs/IG/DRAFT_Unreal_Image_Generator.md "Building panels for Unreal".</para>
    /// <para>Culture-invariant, deterministic (no timestamps, no hash ordering): the same world writes the same bytes.</para>
    /// </summary>
    public static class BuildingPanelDump
    {
        /// <summary>The file's schema version (bump when a field's meaning changes).</summary>
        public const int Format = 1;

        /// <summary>The dump of <paramref name="world"/> as UTF-8 JSON (indented), keyed with the world's own name.</summary>
        public static byte[] ToJson(TerrainWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            using var ms = new MemoryStream();
            Write(world, ms);
            return ms.ToArray();
        }

        /// <summary>Writes the dump of <paramref name="world"/> to <paramref name="stream"/>.</summary>
        public static void Write(TerrainWorld world, Stream stream)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            using var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            w.WriteStartObject();
            w.WriteNumber("format", Format);
            w.WriteString("terrain", world.Name ?? "");
            w.WriteStartArray("buildings");
            int groups = WallGroups.Count(world);
            for (int g = 0; g < groups; g++)
            {
                bool building = WallGroups.IsBuilding(world, g);
                w.WriteStartObject();
                w.WriteString("key", BuildingDamages.KeyOf(world, g));
                w.WriteString("kind", building ? "building" : "wallLine");
                w.WriteNumber("group", g);   // the index BuildingDamage.Building carries (TerrainWorld.Buildings, then the lines)
                if (building)
                {
                    var b = world.Buildings[g];
                    w.WriteString("template", b.Template);
                    w.WriteNumber("baseZ", b.BaseZ);
                    w.WriteNumber("storeys", b.Storeys);
                }
                w.WriteStartArray("panels");
                for (int rank = 0; ; rank++)
                {
                    int gp = BuildingPanelIndex.ToGlobal(world, g, rank);
                    if (gp < 0) break;
                    var p = world.Panels[gp];
                    w.WriteStartObject();
                    w.WriteNumber("panel", rank);   // BuildingDamage.Panel / WindowDamage.Panel on the wire
                    w.WriteStartArray("a"); w.WriteNumberValue(p.A.X); w.WriteNumberValue(p.A.Y); w.WriteEndArray();
                    w.WriteStartArray("b"); w.WriteNumberValue(p.B.X); w.WriteNumberValue(p.B.Y); w.WriteEndArray();
                    w.WriteNumber("base", p.BaseZ);
                    w.WriteNumber("top", p.TopZ);
                    w.WriteNumber("thickness", p.Thickness);
                    w.WriteNumber("storey", p.Storey);
                    w.WriteStartArray("openings");
                    for (int o = 0; o < p.Openings.Count; o++)
                    {
                        var op = p.Openings[o];
                        w.WriteStartObject();
                        w.WriteNumber("opening", o);   // WindowDamage.Opening on the wire
                        w.WriteString("kind", op.Kind switch { TerrainOpeningKind.Door => "door", TerrainOpeningKind.Window => "window", _ => "gap" });
                        w.WriteNumber("at", op.At);
                        w.WriteNumber("width", op.Width);
                        w.WriteNumber("sill", op.SillZ);
                        w.WriteNumber("head", op.HeadZ);
                        w.WriteBoolean("glazed", op.Glazed);
                        if (op.DoorKey != null)
                        {
                            w.WriteString("doorKey", op.DoorKey);
                            int slash = op.DoorKey.LastIndexOf('/');
                            w.WriteString("doorId", slash >= 0 ? op.DoorKey.Substring(slash + 1) : op.DoorKey);
                        }
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
            w.Flush();
        }

        /// <summary>A number as the file writes it (invariant, shortest round-trip) — for a rail comparing a dump's value with a world's.</summary>
        public static string Num(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    }
}
