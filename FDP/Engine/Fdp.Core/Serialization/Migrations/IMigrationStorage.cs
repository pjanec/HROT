using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// Abstracts all sidecar I/O for the migration system.
/// Implementations: <see cref="InMemoryMigrationStorage"/> (tests),
/// <see cref="FileSystemMigrationStorage"/> (production).
/// </summary>
/// <remarks>
/// Made internal because <see cref="UnknownsJournal"/> (used in
/// WriteJournalAsync/FindJournalAsync) is internal sealed, and a public
/// interface referencing internal types would cause an accessibility mismatch.
/// All implementations live in the same assembly as the interface.
/// </remarks>
internal interface IMigrationStorage
{
    /// <summary>
    /// Reads the content of the original file at <paramref name="originalPath"/>.
    /// Returns <c>null</c> if the file does not exist (does NOT throw).
    /// </summary>
    Task<string?> ReadOriginalAsync(string originalPath, CancellationToken ct = default);

    /// <summary>
    /// Atomically writes <paramref name="content"/> to <paramref name="originalPath"/>.
    /// For FileSystem: temp-and-move. For InMemory: dict update.
    /// </summary>
    Task WriteOriginalAsync(string originalPath, string content,
        CancellationToken ct = default);

    /// <summary>
    /// Writes a snapshot sidecar for the given original file.
    /// Filename: <c>{baseName}.v{sourceVersion}.{contentHash}.snapshot.json</c>.
    /// Creates the sidecar directory if needed.
    /// </summary>
    Task WriteSnapshotAsync(string originalPath, int sourceVersion,
        string contentHash, string content,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the highest-version snapshot with version &lt;= <paramref name="maxVersion"/>,
    /// or <c>null</c> if none found.
    /// Verifies the stored content's hash against the filename hash;
    /// throws <see cref="MigrationException"/> on mismatch.
    /// </summary>
    Task<SnapshotEntry?> FindBestSnapshotAsync(string originalPath, int maxVersion,
        CancellationToken ct = default);

    /// <summary>
    /// Writes a journal sidecar. Throws <see cref="System.ArgumentException"/>
    /// if <paramref name="journal"/> has zero operations (defense-in-depth).
    /// Filename: <c>{baseName}.v{N}.{hash16}.unknowns.json</c> where
    /// N = SourceFileVersion and hash16 = SourceContentHash.
    /// </summary>
    Task WriteJournalAsync(string originalPath, UnknownsJournal journal,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the journal whose filename hash matches <paramref name="sourceContentHash"/>,
    /// or <c>null</c> if not found.
    /// Throws <see cref="MigrationException"/> if the hash inside the journal body
    /// does not match the filename hash.
    /// </summary>
    Task<UnknownsJournal?> FindJournalAsync(string originalPath,
        string sourceContentHash, CancellationToken ct = default);

    /// <summary>
    /// Deletes the sidecar file for <paramref name="journal"/>.
    /// No-op if the file does not exist (idempotent).
    /// </summary>
    Task DeleteJournalAsync(string originalPath, UnknownsJournal journal,
        CancellationToken ct = default);

    /// <summary>
    /// Enumerates sidecar files for <paramref name="originalPath"/> by filename only
    /// (no content reading). Returns only entries whose base name matches the
    /// original file's base name.
    /// </summary>
    Task<IReadOnlyList<SidecarFileInfo>> ListSidecarsAsync(string originalPath,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes the sidecar file with the given <paramref name="sidecarFileName"/>
    /// from the sidecar directory of <paramref name="originalPath"/>.
    /// No-op if the file does not exist.
    /// </summary>
    Task DeleteSidecarAsync(string originalPath, string sidecarFileName,
        CancellationToken ct = default);
}
