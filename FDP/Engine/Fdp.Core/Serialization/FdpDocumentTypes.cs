namespace Fdp.Core.Serialization;

/// <summary>
/// Well-known document type identifiers used in the <c>$meta.docType</c>
/// envelope field. Registration in <see cref="MigrationBootstrap"/> uses
/// these constants, not raw string literals.
/// </summary>
public static class FdpDocumentTypes
{
    /// <summary>Flight recorder run metadata.</summary>
    public const string FlightRecorderMetadata = "Fdp.FlightRecorder.Metadata";

    /// <summary>Persisted road network graph.</summary>
    public const string RoadNetwork = "Fdp.RoadNetwork";

    /// <summary>Migration journal (unknowns store).</summary>
    public const string MigrationJournal = "Fdp.MigrationJournal";
}
