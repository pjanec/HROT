using System.Collections.Generic;
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
/// </summary>
public sealed class HrotScenarioEnvelopeDto
{
    /// <summary>
    /// File header: subsystem type and schema version.
    /// </summary>
    public ScenarioHeaderDto? Header { get; set; }

    /// <summary>
    /// Zone definitions keyed by zone name.
    /// <see langword="null"/> when the scenario has no zone section.
    /// </summary>
    public Dictionary<string, ZoneDefinitionDto>? Zones { get; set; }

    /// <summary>
    /// Raw FDP entity DOM.  Treated as opaque JSON by the application layer.
    /// <see langword="null"/> when the scenario contains no entities.
    /// </summary>
    public JsonObject? Entities { get; set; }
}
