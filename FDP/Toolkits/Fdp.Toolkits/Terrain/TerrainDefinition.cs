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
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
    [ComponentId(GlobalComponentIds.TerrainDefinition)]
    public sealed class TerrainDefinition
    {
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

        /// <summary>True when this definition declares nothing to load — legal, and not an error.</summary>
        public bool IsEmpty => RoadNetworks.Count == 0 && string.IsNullOrEmpty(World);
    }
}
