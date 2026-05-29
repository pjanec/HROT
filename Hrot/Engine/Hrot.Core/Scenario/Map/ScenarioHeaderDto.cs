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
}
