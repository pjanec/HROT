using System.Text.Json.Serialization;

namespace Hrot.Map.Common.Scenario;

/// <summary>
/// Header section of the HROT scenario envelope JSON file.
/// Identifies the subsystem type and optional TKB name.
/// </summary>
public sealed class ScenarioHeaderDto
{
    /// <summary>
    /// Identifies the subsystem that owns this scenario (e.g. "Hrot.Scenario").
    /// </summary>
    public string? SubsystemType { get; set; }

    /// <summary>
    /// Identifies the TKB required by this scenario. Null means "no opinion" -- the node uses the fallback catalog.
    /// </summary>
    public string? TkbName { get; set; }

    /// <summary>
    /// Identifies the TERRAIN required by this scenario, with an optional subfolder path
    /// (e.g. "desert/kandahar"). Null means "no opinion" and the scenario still loads, exactly like
    /// <see cref="TkbName"/>.
    ///
    /// <para>⭐ A NAME and nothing more: what the terrain CONTAINS (road networks, terrain DB,
    /// heightmaps, built-in buildings) is declared in the terrain asset's own definition file, never
    /// here. 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1e ①.</para>
    /// </summary>
    public string? TerrainName { get; set; }
}
