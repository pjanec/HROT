using System.Text.Json.Nodes;

namespace Hrot.Map.Common.Scenario;

/// <summary>
/// Root envelope for a HROT scenario JSON file.
///
/// <para>
/// The <see cref="Entities"/> section is treated as an opaque
/// <see cref="JsonObject"/> (the raw FDP DOM) so that the application layer
/// never needs to know about FDP serialization internals.
/// </para>
///
/// <para>⛔ <b>There is no <c>Zones</c> section (F1, retired 2026-09-17).</b> A zone was an entry in an
/// embedded dictionary AND, once authored on the map, an entity — two places one truth could live, which
/// is the failure mode this design removes by construction. A zone is now only an entity, so it rides
/// <see cref="Entities"/> like everything else.
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §5.1, §6 (retirement).</para>
/// </summary>
public sealed class HrotScenarioEnvelopeDto
{
    /// <summary>
    /// File header: subsystem type and schema version.
    /// </summary>
    public ScenarioHeaderDto? Header { get; set; }

    /// <summary>
    /// Raw FDP entity DOM.  Treated as opaque JSON by the application layer.
    /// <see langword="null"/> when the scenario contains no entities.
    /// </summary>
    public JsonObject? Entities { get; set; }
}
