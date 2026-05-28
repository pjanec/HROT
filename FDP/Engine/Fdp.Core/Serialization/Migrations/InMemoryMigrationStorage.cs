using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations.Internal;

namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// In-memory implementation of <see cref="IMigrationStorage"/> used in unit tests.
/// All storage is backed by dictionaries; no real filesystem I/O is performed.
/// </summary>
internal sealed class InMemoryMigrationStorage : IMigrationStorage
{
    // Key: original path (case-sensitive on purpose for realistic path semantics).
    private readonly Dictionary<string, string> _originals = new(StringComparer.Ordinal);

    // Key: synthetic sidecar key built via SidecarKey(originalPath, fileName).
    private readonly Dictionary<string, string> _sidecars = new(StringComparer.Ordinal);

    private static string SidecarKey(string originalPath, string fileName)
        => Path.Combine(SidecarFileHelper.GetSidecarDirectory(originalPath), fileName);

    // ---------------------------------------------------------------
    // IMigrationStorage
    // ---------------------------------------------------------------

    public Task<string?> ReadOriginalAsync(string originalPath, CancellationToken ct = default)
    {
        _originals.TryGetValue(originalPath, out var content);
        return Task.FromResult<string?>(content);
    }

    public Task WriteOriginalAsync(string originalPath, string content,
        CancellationToken ct = default)
    {
        _originals[originalPath] = content;
        return Task.CompletedTask;
    }

    public Task WriteSnapshotAsync(string originalPath, int sourceVersion,
        string contentHash, string content, CancellationToken ct = default)
    {
        var fileName = SidecarFileHelper.GetSnapshotFileName(originalPath, sourceVersion, contentHash);
        _sidecars[SidecarKey(originalPath, fileName)] = content;
        return Task.CompletedTask;
    }

    public Task<SnapshotEntry?> FindBestSnapshotAsync(string originalPath, int maxVersion,
        CancellationToken ct = default)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalPath);
        var sidecarDir = SidecarFileHelper.GetSidecarDirectory(originalPath);

        SnapshotEntry? best = null;
        int bestVersion = -1;

        foreach (var kvp in _sidecars)
        {
            var (dir, file) = SplitKey(kvp.Key);
            if (!string.Equals(dir, sidecarDir, StringComparison.Ordinal))
                continue;

            if (!SidecarFileHelper.TryParseSnapshotFileName(
                file, baseName, out int version, out string? parsedHash))
                continue;

            if (version > maxVersion || version <= bestVersion)
                continue;

            var actualHash = HashUtilities.ComputeContentHash(kvp.Value);
            if (!actualHash.Equals(parsedHash, StringComparison.Ordinal))
                throw new MigrationException(
                    $"Snapshot sidecar content hash mismatch for '{file}': " +
                    $"expected '{parsedHash}', got '{actualHash}'. " +
                    "The sidecar may be corrupted.");

            best = new SnapshotEntry(kvp.Key, version, parsedHash!, kvp.Value);
            bestVersion = version;
        }

        return Task.FromResult<SnapshotEntry?>(best);
    }

    public Task WriteJournalAsync(string originalPath, UnknownsJournal journal,
        CancellationToken ct = default)
    {
        if (journal.Operations.Count == 0)
            throw new ArgumentException(
                "Cannot write a journal with zero operations.", nameof(journal));

        var fileName = SidecarFileHelper.GetJournalFileName(
            originalPath, journal.SourceFileVersion, journal.SourceContentHash);
        _sidecars[SidecarKey(originalPath, fileName)] = journal.Serialize();
        return Task.CompletedTask;
    }

    public Task<UnknownsJournal?> FindJournalAsync(string originalPath,
        string sourceContentHash, CancellationToken ct = default)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalPath);
        var sidecarDir = SidecarFileHelper.GetSidecarDirectory(originalPath);

        foreach (var kvp in _sidecars)
        {
            var (dir, file) = SplitKey(kvp.Key);
            if (!string.Equals(dir, sidecarDir, StringComparison.Ordinal))
                continue;

            if (!SidecarFileHelper.TryParseJournalFileName(
                file, baseName, out _, out string? filenameHash))
                continue;

            if (!filenameHash!.Equals(sourceContentHash, StringComparison.Ordinal))
                continue;

            // Deserialize and verify body hash matches filename hash
            var journal = UnknownsJournal.Deserialize(kvp.Value);
            if (!journal.SourceContentHash.Equals(filenameHash, StringComparison.Ordinal))
                throw new MigrationException(
                    $"Journal hash mismatch for '{file}': filename hash '{filenameHash}', " +
                    $"body has '{journal.SourceContentHash}'.");

            return Task.FromResult<UnknownsJournal?>(journal);
        }

        return Task.FromResult<UnknownsJournal?>(null);
    }

    public Task DeleteJournalAsync(string originalPath, UnknownsJournal journal,
        CancellationToken ct = default)
    {
        var fileName = SidecarFileHelper.GetJournalFileName(
            originalPath, journal.SourceFileVersion, journal.SourceContentHash);
        _sidecars.Remove(SidecarKey(originalPath, fileName));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SidecarFileInfo>> ListSidecarsAsync(string originalPath,
        CancellationToken ct = default)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalPath);
        var sidecarDir = SidecarFileHelper.GetSidecarDirectory(originalPath);
        var result = new List<SidecarFileInfo>();

        foreach (var kvp in _sidecars)
        {
            var (dir, file) = SplitKey(kvp.Key);
            if (!string.Equals(dir, sidecarDir, StringComparison.Ordinal))
                continue;

            if (SidecarFileHelper.TryParseSnapshotFileName(
                file, baseName, out int version, out string? hash))
                result.Add(new SidecarFileInfo(file, SidecarKind.Snapshot, version, hash!));
            else if (SidecarFileHelper.TryParseJournalFileName(
                file, baseName, out version, out hash))
                result.Add(new SidecarFileInfo(file, SidecarKind.Journal, version, hash!));
        }

        return Task.FromResult<IReadOnlyList<SidecarFileInfo>>(result);
    }

    public Task DeleteSidecarAsync(string originalPath, string sidecarFileName,
        CancellationToken ct = default)
    {
        _sidecars.Remove(SidecarKey(originalPath, sidecarFileName));
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------
    // Test-helper methods (visible via InternalsVisibleTo)
    // ---------------------------------------------------------------

    /// <summary>Seeds an original file with the given content.</summary>
    public void Seed(string originalPath, string content)
        => _originals[originalPath] = content;

    /// <summary>
    /// Seeds a snapshot sidecar; the content hash is computed automatically
    /// so that <see cref="FindBestSnapshotAsync"/> can locate it.
    /// </summary>
    public void SeedSnapshot(string originalPath, int sourceVersion, string content)
    {
        var hash = HashUtilities.ComputeContentHash(content);
        var fileName = SidecarFileHelper.GetSnapshotFileName(originalPath, sourceVersion, hash);
        _sidecars[SidecarKey(originalPath, fileName)] = content;
    }

    /// <summary>
    /// Seeds a raw sidecar directly by filename, bypassing hash computation.
    /// Use for corruption tests (T1-321, T1-326, T1-327).
    /// </summary>
    internal void SeedRawSidecar(string originalPath, string fileName, string rawContent)
        => _sidecars[SidecarKey(originalPath, fileName)] = rawContent;

    /// <summary>
    /// Returns <c>true</c> if a snapshot sidecar exists for the given version.
    /// </summary>
    public bool HasSnapshot(string originalPath, int sourceVersion)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalPath);
        var sidecarDir = SidecarFileHelper.GetSidecarDirectory(originalPath);

        foreach (var key in _sidecars.Keys)
        {
            var (dir, file) = SplitKey(key);
            if (!string.Equals(dir, sidecarDir, StringComparison.Ordinal))
                continue;
            if (SidecarFileHelper.TryParseSnapshotFileName(
                file, baseName, out int v, out _) && v == sourceVersion)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if a journal sidecar exists with the given content hash.
    /// </summary>
    public bool HasJournal(string originalPath, string sourceContentHash)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalPath);
        var sidecarDir = SidecarFileHelper.GetSidecarDirectory(originalPath);

        foreach (var key in _sidecars.Keys)
        {
            var (dir, file) = SplitKey(key);
            if (!string.Equals(dir, sidecarDir, StringComparison.Ordinal))
                continue;
            if (SidecarFileHelper.TryParseJournalFileName(
                file, baseName, out _, out string? hash)
                && hash!.Equals(sourceContentHash, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Returns the current stored content for <paramref name="originalPath"/>,
    /// or <c>null</c> if not present.</summary>
    public string? ReadCurrent(string originalPath)
    {
        _originals.TryGetValue(originalPath, out var content);
        return content;
    }

    // ---------------------------------------------------------------
    // Key split utility
    // ---------------------------------------------------------------

    private static (string dir, string file) SplitKey(string key)
    {
        var idx = key.LastIndexOf(Path.DirectorySeparatorChar);
        if (idx < 0)
            idx = key.LastIndexOf('/');
        if (idx < 0)
            return (string.Empty, key);
        return (key[..idx], key[(idx + 1)..]);
    }
}
