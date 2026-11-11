using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// The PARSED terrain definition asset — an ECS singleton describing what the scenario's terrain
    /// provides.
    ///
    /// <para><b>⭐ The scenario names a terrain; THIS says what that terrain contains.</b> The scenario
    /// header carries only a name (with an optional subfolder path). Road networks — and later the
    /// terrain DB, heightmap, navmesh and built-in buildings — are internal data of the terrain asset and
    /// are declared here, in the terrain's own definition file, resolved from that name.</para>
    ///
    /// <para><b>⛔ Authored and SHIPPED as an asset.</b> It is not editable from the scenario editor, for
    /// the same reason the road network is not: it describes content that arrives from outside.</para>
    ///
    /// <para><b>⛔ Never persisted — <c>[DataPolicy(NoScenario | NoReplay)]</c>.</b> It is re-derived from
    /// the named asset on every load, so it cannot disagree with the asset. ⚠ The <c>NoReplay</c> half is
    /// load-bearing and not decorative: the flight recorder walks the singleton tables and records every
    /// one whose type <c>IsRecordable</c>, so without this policy the parsed definition WOULD be written
    /// into recordings.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1e ①a ②, §2.1a.
    /// </summary>
    // ⭐ G-7 / CE-4354 NodeLocal (DESIGN_Review_Perspective §6.4 I9): a per-node terrain service definition — never copied into a merged view.
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay | DataPolicy.NodeLocal)]
    [ComponentId(GlobalComponentIds.TerrainDefinition)]
    public sealed class TerrainDefinition
    {
        /// <summary>
        /// ⭐ New scenario N4 (📄 docs/UX/DESIGN_New_Scenario_And_Recipe.md §4, R-4124) — the loaded terrain's name as a person reads it (the Scenario
        /// form's Terrain row, the window title): <see cref="ResolvedName"/>, else the asset's own <see cref="Name"/>; null when no terrain is loaded.
        /// ONE reader for every surface. Allocates nothing.
        /// </summary>
        public static string? LoadedName(EntityRepository world)
        {
            if (!world.HasSingletonManaged<TerrainDefinition>() || world.GetSingletonManaged<TerrainDefinition>() is not { } d) return null;
            return !string.IsNullOrEmpty(d.ResolvedName) ? d.ResolvedName : string.IsNullOrEmpty(d.Name) ? null : d.Name;
        }

        /// <summary>The schema version this code writes and understands.</summary>
        public const int CurrentSchemaVersion = 2;

        /// <summary>
        /// Version of the definition-file schema this instance was parsed from. ⭐ A versioned root is
        /// required so a future field can be added without every older asset becoming unreadable.
        /// </summary>
        public int SchemaVersion { get; init; } = CurrentSchemaVersion;

        /// <summary>
        /// The terrain's own name, as the asset declares it. ⚠ This is the asset's self-description; the
        /// name a scenario RESOLVES BY is the scenario header's <c>TerrainName</c>. They will normally
        /// agree, and a host that finds they do not should say so rather than silently prefer one.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Road network asset paths this terrain provides, relative to the terrain asset's own folder,
        /// in load order. ⭐ First and currently the only real content: this is what takes over from the
        /// retiring zone bundle's per-zone <c>RoadNetworkPath</c>.
        /// </summary>
        public IReadOnlyList<string> RoadNetworks { get; init; } = new List<string>();

        /// <summary>
        /// ⭐ The name the scenario RESOLVED this terrain by (the header's <c>TerrainName</c>), set by the
        /// loader when it commits the terrain. This — not the asset's self-declared <see cref="Name"/> — is
        /// what a save writes back into the scenario header, so a load→save round-trip keeps the terrain
        /// (<c>CE-3015</c>). Empty until a loader commits it.
        /// </summary>
        public string ResolvedName { get; set; } = string.Empty;

        /// <summary>⭐ O-8 (CE-4420) — the content hash of the overlay merged into the resident world (empty: none); stamped into a recording's metadata.</summary>
        public string ResolvedOverlayHash { get; set; } = string.Empty;

        /// <summary>⭐ O-8 — the content hash of the BASE terrain under that overlay (empty when no overlay is resident).</summary>
        public string ResolvedBaseHash { get; set; } = string.Empty;

        /// <summary>
        /// The terrain name resident in <paramref name="world"/> — the scenario's <c>TerrainName</c> to save —
        /// or <c>null</c> when no terrain is loaded.
        /// </summary>
        public static string? ResidentName(EntityRepository? world)
        {
            if (world == null || !world.HasSingletonManaged<TerrainDefinition>()) return null;
            var def = world.GetSingletonManaged<TerrainDefinition>();
            return string.IsNullOrEmpty(def?.ResolvedName) ? null : def!.ResolvedName;
        }

        /// <summary>
        /// ⭐ Schema v2 — the terrain's WORLD file (GeoJSON, local metres), relative to the definition's folder,
        /// or empty when the terrain has no world geometry (roads only). 📄 docs/DESIGN_Terrain_World.md §2.
        /// </summary>
        public string World { get; init; } = string.Empty;

        /// <summary>
        /// ⭐ <c>CE-3126</c> (R-229) — where the terrain's local metres sit on the Earth: the geodetic point its (0,0,0) is.
        /// Committing the terrain sets every node's geo transform to it (<c>TerrainResidency.Commit</c>), so lat/lon on the wire
        /// and in exports follow the terrain. ⛔ No default in code: a terrain that declares none has origin 0,0,0 (said once in
        /// the log). 📄 docs/DESIGN_Geo_Origin.md §2 A/B.
        /// </summary>
        public TerrainGeoOrigin? Origin { get; init; }

        /// <summary>
        /// ⭐ CE-4394 (docs/DESIGN_Terrain_Import_UTM.md §12.2) — the EXACT UTM frame of the terrain's local (0, 0), when it declares one
        /// (<c>frame.utm</c> + <c>frame.corner</c>, written by the importer): the node's geo transform then uses it instead of the tangent
        /// plane at <see cref="Origin"/>. Null for every terrain that declares none.
        /// </summary>
        public TerrainUtmFrame? Utm { get; init; }

        /// <summary>
        /// ⭐ AQ93 R-4021 — the terrain's SUMO road network (a <c>.net.xml</c> beside the definition), or <c>null</c> when it has
        /// none. Kept OUT of <see cref="RoadNetworks"/> on purpose: those are our own CarKinem road graphs, and the SUMO net stays
        /// separate and independent of them (R-4026). 📄 docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md §11.3.
        /// </summary>
        public TerrainSumoNet? SumoNet { get; init; }

        /// <summary>True when this definition declares nothing to load — legal, and not an error.</summary>
        public bool IsEmpty => RoadNetworks.Count == 0 && string.IsNullOrEmpty(World) && SumoNet == null;
    }

    /// <summary>
    /// ⭐ AQ93 R-4021 / E1b — a terrain's SUMO net: its file (relative to the definition's folder) and, for a net that is NOT
    /// georeferenced (<c>projParameter="!"</c>), the ANCHOR — the net point that sits at the terrain's local (0, 0): terrain local
    /// = net − anchor. A georeferenced net (E1) is placed through the terrain's geo origin instead and needs no anchor.
    /// </summary>
    public sealed record TerrainSumoNet(string File, double AnchorX, double AnchorY);

    /// <summary>
    /// ⭐ CE-4394 — the UTM frame of a terrain's local (0, 0): zone, hemisphere and the easting / northing of that point (the importer's
    /// <c>e0 + corner[0]</c>, <c>n0 + corner[1]</c>), metres.
    /// </summary>
    public sealed record TerrainUtmFrame(int Zone, bool South, double Easting, double Northing);

    /// <summary>
    /// A geodetic point — latitude and longitude in degrees, altitude in metres — as a terrain file declares its origin.
    /// 📄 docs/DESIGN_Geo_Origin.md §2 A.
    /// </summary>
    public readonly record struct TerrainGeoOrigin(double Lat, double Lon, double Alt)
    {
        /// <summary>The origin a node has when no terrain says otherwise.</summary>
        public static readonly TerrainGeoOrigin Zero = new(0.0, 0.0, 0.0);
    }
}
