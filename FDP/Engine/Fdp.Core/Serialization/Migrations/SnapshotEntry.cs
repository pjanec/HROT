namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// Returned by <see cref="IMigrationStorage.FindBestSnapshotAsync"/>.
/// Carries the snapshot's content and metadata.
/// </summary>
public sealed record SnapshotEntry(
    string SidecarPath,
    int Version,
    string ContentHash,
    string Content);
