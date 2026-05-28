namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// Describes a sidecar file (snapshot or journal) discovered in the
/// <c>.migration-snapshots/</c> directory alongside an original document.
/// All fields are parsed directly from the filename — no I/O is performed.
/// </summary>
public sealed record SidecarFileInfo(
    string FileName,
    SidecarKind Kind,
    int Version,
    string ContentHash);
