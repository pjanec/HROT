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
    public record ScenarioHeader(string SubsystemType, string? TkbName = null);
}
