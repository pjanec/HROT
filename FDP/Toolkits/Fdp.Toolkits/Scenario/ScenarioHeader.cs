namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// Immutable header block written as the first entry in every scenario JSON file.
    /// Used by each subsystem's Cluster handler to filter files intended for it before
    /// performing a full DOM parse.
    /// </summary>
    /// <param name="SubsystemType">
    /// Human-readable identifier of the subsystem that produced this file
    /// (e.g. <c>"Hrot.CGF"</c>, <c>"Hrot.SimHost"</c>). Written to <c>$meta.docType</c>
    /// in Phase 2 format.
    /// </param>
    /// <param name="TkbName">
    /// Optional TKB name required by this scenario. Null means no opinion.
    /// </param>
    /// <param name="TerrainName">
    /// Optional TERRAIN name required by this scenario, with an optional subfolder path
    /// (e.g. <c>"desert/kandahar"</c>). Null means no opinion, and such a scenario still loads.
    ///
    /// <para><b>⭐ A NAME, and nothing else.</b> Road networks, terrain DB, heightmaps and built-in
    /// buildings are internal data of the terrain asset and are declared in the terrain's own definition
    /// file — ⛔ never in the scenario. This field is exactly the same shape as
    /// <paramref name="TkbName"/>: a name that resolves to an artifact, with the artifact's contents
    /// living in the artifact.</para>
    ///
    /// <para>⛔ Do NOT grow this into an identity-plus-asset-reference block. A prior design draft did
    /// and it was retracted: an asset list here would be a second place the truth can live, and it would
    /// re-create the embedded content bundle that zones-as-entities exists to retire.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1e ①.
    /// </param>
    public record ScenarioHeader(
        string SubsystemType,
        string? TkbName = null,
        string? TerrainName = null);
}
