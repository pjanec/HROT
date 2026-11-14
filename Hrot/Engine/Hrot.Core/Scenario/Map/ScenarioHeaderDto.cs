using System.Text.Json.Serialization;

namespace Hrot.Map.Common.Scenario;

/// <summary>
/// Header section of the HROT scenario envelope JSON file.
/// Identifies the subsystem type and the optional terrain name.
/// ⛔ <c>R-4018</c>: no TKB name — a scenario never names a TKB; an older file's <c>tkbName</c> is ignored on read.
/// </summary>
public sealed class ScenarioHeaderDto
{
    /// <summary>
    /// Identifies the subsystem that owns this scenario (e.g. "Hrot.Scenario").
    /// </summary>
    public string? SubsystemType { get; set; }

    /// <summary>
    /// Identifies the TERRAIN required by this scenario, with an optional subfolder path
    /// (e.g. "desert/kandahar"). Null means "no opinion" and the scenario still loads.
    ///
    /// <para>⭐ A NAME and nothing more: what the terrain CONTAINS (road networks, terrain DB,
    /// heightmaps, built-in buildings) is declared in the terrain asset's own definition file, never
    /// here. 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1e ①.</para>
    /// </summary>
    public string? TerrainName { get; set; }

    /// <summary>
    /// ⭐ <c>CE-3288</c> — what the scenario shows and how to judge that it worked ("What: … · Judge: …"); null = none.
    /// 📄 docs/DESIGN_Distributed_Scenario_Persistence.md §4d.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>⭐ Macros S3a ② — <c>"macro"</c> for a macro file (SR-173); null for a scenario. See <c>ScenarioHeaderJson</c> (Fdp.Toolkits) for the typed read.</summary>
    public string? Kind { get; set; }

    /// <summary>⭐ SR-44 — the declared params rows (opaque JSON here; typed by <c>ScenarioHeaderJson.Read</c>).</summary>
    public System.Text.Json.Nodes.JsonArray? Params { get; set; }

    /// <summary>⭐ SR-152 — the authoring anchor <c>{Position:[x,y,z], Heading:deg}</c> (opaque JSON here).</summary>
    public System.Text.Json.Nodes.JsonObject? Anchor { get; set; }

    /// <summary>⭐ SR-182 — <c>{"MaxShift": m}</c> (opaque JSON here).</summary>
    public System.Text.Json.Nodes.JsonObject? Placement { get; set; }

    /// <summary>⭐ SR-156 — <c>false</c> pins a macro to its authored place; null/true = relocatable.</summary>
    public bool? Relocatable { get; set; }
}
