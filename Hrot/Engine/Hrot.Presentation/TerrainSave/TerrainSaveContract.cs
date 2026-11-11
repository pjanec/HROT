namespace Hrot.UI.Common.TerrainSave;

/// <summary>What <i>Save terrain as</i> would make — the mockup 4 dialog's rows (docs/DESIGN_Terrain_Authoring.md §13 W-3, §14 E-4…E-6, §18d D-6). A plain DTO so the window never references the engine's bake.</summary>
public sealed record TerrainSavePreview(int Blocks, int Houses, int Woods, int Trees, double WallKm, int Bridges, int UnitsNotBaked, int AreasNotBaked, int DamagedHousesNotBaked, int? TilesToBake, double? EstimateSeconds);

/// <summary>What a save wrote: the new terrain's folder and, when one was asked for, the new scenario's name.</summary>
public sealed record TerrainSaveOutcome(string TerrainFolder, string? ScenarioName);

/// <summary>
/// ⭐ Terrain authoring U-5 — what the Save terrain as dialog needs from the host, ONE seam (the OSM wizard's precedent: the window and its model are pure, the host owns the engine). The editor's implementation calls
/// <c>TerrainBake.SaveAs</c> / <c>TerrainUsers.Of</c> (backend's, CE-4422); ⛔ the window never writes a terrain itself.
/// </summary>
public interface ITerrainSaveService
{
    /// <summary>The scenario the terrain would be made from (its blocks are the source), or null when none is loaded — the menu item is then greyed.</summary>
    string? SourceScenarioName { get; }

    /// <summary>The terrain the source was built on (shown in the Save terrain overwrite line of a terrain source, and as the default name's stem).</summary>
    string? SourceTerrainName { get; }

    /// <summary>The summary of saving as <paramref name="newName"/>; throws (the window shows the message) when it cannot be made.</summary>
    TerrainSavePreview Preview(string newName);

    /// <summary>Whether a terrain called <paramref name="name"/> already exists (W-4: saving over it asks first).</summary>
    bool Exists(string name);

    /// <summary>The scenarios that name terrain <paramref name="name"/> (W-4's "Used by N scenarios").</summary>
    IReadOnlyList<string> UsersOf(string name);

    /// <summary>Writes the terrain (and, with <paramref name="scenarioName"/>, the new scenario on it); <paramref name="prebuildMesh"/> bakes the walkable mesh at save.</summary>
    TerrainSaveOutcome Save(string newName, bool prebuildMesh, string? scenarioName);
}
