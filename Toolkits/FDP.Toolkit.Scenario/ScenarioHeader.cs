namespace FDP.Toolkit.Scenario
{
    /// <summary>
    /// Immutable header block written as the first entry in every scenario JSON file.
    /// Used by each subsystem's Cluster handler to filter files intended for it before
    /// performing a full DOM parse.
    /// </summary>
    /// <param name="SubsystemType">
    /// Human-readable identifier of the subsystem that produced this file
    /// (e.g. <c>"Hrot.CGF"</c>, <c>"Hrot.SimHost"</c>).
    /// </param>
    /// <param name="SchemaVersion">
    /// Integer schema version.  Increment when structural changes to the JSON
    /// contract are made that break old readers.
    /// </param>
    public record ScenarioHeader(string SubsystemType, int SchemaVersion = 1);
}
