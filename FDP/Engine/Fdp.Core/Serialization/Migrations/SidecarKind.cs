namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// Distinguishes the two kinds of migration sidecar files stored in the
/// <c>.migration-snapshots/</c> directory.
/// </summary>
public enum SidecarKind
{
    /// <summary>
    /// A verbatim pre-migration snapshot of the original file.
    /// Filename pattern: <c>{base}.v{N}.{hash16}.snapshot.json</c>
    /// </summary>
    Snapshot,

    /// <summary>
    /// An unknowns journal that records higher-version fields lost during
    /// down-migration so they can be restored on save-back.
    /// Filename pattern: <c>{base}.v{N}.{hash16}.unknowns.json</c>
    /// </summary>
    Journal
}
