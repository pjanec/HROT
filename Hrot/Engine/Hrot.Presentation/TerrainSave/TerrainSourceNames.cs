using Fdp.Toolkit.Orchestration;

namespace Hrot.UI.Common.TerrainSave;

/// <summary>
/// ⭐ Terrain authoring U-5 (docs/DESIGN_Terrain_Authoring.md §14 E-1/E-2) — how a terrain's SOURCE scenario is opened for editing. Every scenario open is by NAME (<see cref="ContentLayout.ResolveScenarioDirectory"/> is the one name → folder resolver and
/// knows saved · <c>recipes/scenarios</c> · <c>opened/</c> · <c>macros</c>); the source lives inside the terrain folder (<c>&lt;terrain&gt;/source/scenario</c>), which no name reaches. So <i>Edit terrain…</i> STAGES a copy of it under the
/// <c>opened/&lt;area&gt;/&lt;key&gt;</c> area — the "opened, not saved" one (<see cref="ContentLayout.OpenedCopyName"/>, the recordings' precedent) — as <c>opened/terrain/&lt;terrain&gt;</c>, and opens THAT. A scenario so named is a TERRAIN SOURCE (the title says so);
/// <i>Save terrain</i> bakes it back over the terrain it came from. ⛔ No new resolver prefix: the opened area is generic by key.
/// </summary>
public static class TerrainSourceNames
{
    /// <summary>The <c>opened/</c> area that holds staged terrain sources.</summary>
    public const string Area = "terrain";

    /// <summary>The scenario name that opens the staged source of <paramref name="terrain"/>.</summary>
    public static string NameOf(string terrain) => ContentLayout.OpenedCopyName(Area, terrain);

    /// <summary>The terrain whose source <paramref name="scenarioName"/> is (<c>opened/terrain/&lt;terrain&gt;</c>), or null for any other scenario. ⭐ ONE rule, owned by the window title (Hrot.Common cannot reference this assembly).</summary>
    public static string? TerrainOf(string? scenarioName)
        => string.IsNullOrWhiteSpace(scenarioName) ? null : Hrot.Common.Scenarios.ScenarioWindowTitle.TerrainSourceOf(scenarioName);
}
