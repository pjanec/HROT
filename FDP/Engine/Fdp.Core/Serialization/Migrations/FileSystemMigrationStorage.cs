using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations.Internal;

namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// Production implementation of <see cref="IMigrationStorage"/>.
/// All writes use a temp-file-and-move atomic protocol to avoid
/// corrupting files on a crash or concurrent write.
/// </summary>
internal sealed class FileSystemMigrationStorage : IMigrationStorage
{
    // ---------------------------------------------------------------
    // IMigrationStorage
    // ---------------------------------------------------------------

    public async Task<string?> ReadOriginalAsync(string originalPath,
        CancellationToken ct = default)
    {
        // No File.Exists pre-check: that would introduce a TOCTOU window.
        // FileNotFoundException from ReadAllTextAsync is caught below and
        // treated the same as "file not found".
        try
        {
            return await File.ReadAllTextAsync(originalPath, Encoding.UTF8, ct)
                .ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (IOException ex)
        {
            throw new MigrationException(
                $"Failed to read original file '{originalPath}': {ex.Message}", ex);
        }
    }

    public async Task WriteOriginalAsync(string originalPath, string content,
        CancellationToken ct = default)
    {
        await AtomicWriteAsync(originalPath, content, ct).ConfigureAwait(false);
    }

    public async Task WriteSnapshotAsync(string originalPath, int sourceVersion,
        string contentHash, string content, CancellationToken ct = default)
    {
        var sidecarDir = SidecarFileHelper.GetSidecarDirectory(originalPath);
        Directory.CreateDirectory(sidecarDir);

        var fileName = SidecarFileHelper.GetSnapshotFileName(
            originalPath, sourceVersion, contentHash);
        var targetPath = Path.Combine(sidecarDir, fileName);

        await AtomicWriteAsync(targetPath, content, ct).ConfigureAwait(false);
    }

    public async Task<SnapshotEntry?> FindBestSnapshotAsync(string originalPath,
        int maxVersion, CancellationToken ct = default)
    {
        var sidecarDir = SidecarFileHelper.GetSidecarDirectory(originalPath);
        if (!Directory.Exists(sidecarDir))
            return null;

        var baseName = Path.GetFileNameWithoutExtension(originalPath);

        SnapshotEntry? best = null;
        int bestVersion = -1;

        foreach (var filePath in Directory.EnumerateFiles(
            sidecarDir, "*.snapshot.json", SearchOption.TopDirectoryOnly))
        {
            var file = Path.GetFileName(filePath);
            if (!SidecarFileHelper.TryParseSnapshotFileName(
                file, baseName, out int version, out string? parsedHash))
                continue;

            if (version > maxVersion || version <= bestVersion)
                continue;

            string content;
            try
            {
                content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, ct)
                    .ConfigureAwait(false);
            }
            catch (FileNotFoundException) { continue; }
            catch (IOException ex)
            {
                throw new MigrationException(
                    $"Failed to read snapshot '{filePath}': {ex.Message}", ex);
            }

            var actualHash = HashUtilities.ComputeContentHash(content);
            if (!actualHash.Equals(parsedHash, StringComparison.Ordinal))
                throw new MigrationException(
                    $"Snapshot hash mismatch for '{file}': filename has '{parsedHash}', " +
                    $"content hashes to '{actualHash}'.");

            best = new SnapshotEntry(filePath, version, parsedHash!, content);
            bestVersion = version;
        }

        return best;
    }

    public async Task WriteJournalAsync(string originalPath, UnknownsJournal journal,
        CancellationToken ct = default)
    {
        if (journal.Operations.Count == 0)
            throw new ArgumentException(
                "Cannot write a journal with zero operations.", nameof(journal));

        var sidecarDir = SidecarFileHelper.GetSidecarDirectory(originalPath);
        Directory.CreateDirectory(sidecarDir);

        var fileName = SidecarFileHelper.GetJournalFileName(
            originalPath, journal.SourceFileVersion, journal.SourceContentHash);
        var targetPath = Path.Combine(sidecarDir, fileName);

        await AtomicWriteAsync(targetPath, journal.Serialize(), ct).ConfigureAwait(false);
    }

    public async Task<UnknownsJournal?> FindJournalAsync(string originalPath,
        string sourceContentHash, CancellationToken ct = default)
    {
        var sidecarDir = SidecarFileHelper.GetSidecarDirectory(originalPath);
        if (!Directory.Exists(sidecarDir))
            return null;

        var baseName = Path.GetFileNameWithoutExtension(originalPath);

        foreach (var filePath in Directory.EnumerateFiles(
            sidecarDir, "*.unknowns.json", SearchOption.TopDirectoryOnly))
        {
            var file = Path.GetFileName(filePath);
            if (!SidecarFileHelper.TryParseJournalFileName(
                file, baseName, out _, out string? filenameHash))
                continue;

            if (!filenameHash!.Equals(sourceContentHash, StringComparison.Ordinal))
                continue;

            string rawContent;
            try
            {
                rawContent = await File.ReadAllTextAsync(filePath, Encoding.UTF8, ct)
                    .ConfigureAwait(false);
            }
            catch (FileNotFoundException) { continue; }
            catch (IOException ex)
            {
                throw new MigrationException(
                    $"Failed to read journal '{filePath}': {ex.Message}", ex);
            }

            var journal = UnknownsJournal.Deserialize(rawContent);
            if (!journal.SourceContentHash.Equals(filenameHash, StringComparison.Ordinal))
                throw new MigrationException(
                    $"Journal hash mismatch for '{file}': filename hash '{filenameHash}', " +
                    $"body has '{journal.SourceContentHash}'.");

            return journal;
        }

        return null;
    }

    public Task DeleteJournalAsync(string originalPath, UnknownsJournal journal,
        CancellationToken ct = default)
    {
        var sidecarDir = SidecarFileHelper.GetSidecarDirectory(originalPath);
        var fileName = SidecarFileHelper.GetJournalFileName(
            originalPath, journal.SourceFileVersion, journal.SourceContentHash);
        var filePath = Path.Combine(sidecarDir, fileName);

        try { File.Delete(filePath); }
        catch (FileNotFoundException) { /* idempotent */ }
        catch (IOException ex)
        {
            throw new MigrationException(
                $"Failed to delete journal '{filePath}': {ex.Message}", ex);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SidecarFileInfo>> ListSidecarsAsync(string originalPath,
        CancellationToken ct = default)
    {
        var sidecarDir = SidecarFileHelper.GetSidecarDirectory(originalPath);
        var baseName = Path.GetFileNameWithoutExtension(originalPath);
        var result = new List<SidecarFileInfo>();

        if (!Directory.Exists(sidecarDir))
            return Task.FromResult<IReadOnlyList<SidecarFileInfo>>(result);

        foreach (var filePath in Directory.EnumerateFiles(
            sidecarDir, "*.*", SearchOption.TopDirectoryOnly))
        {
            var file = Path.GetFileName(filePath);

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
        var sidecarDir = SidecarFileHelper.GetSidecarDirectory(originalPath);
        var filePath = Path.Combine(sidecarDir, sidecarFileName);

        try { File.Delete(filePath); }
        catch (FileNotFoundException) { /* idempotent */ }
        catch (IOException ex)
        {
            throw new MigrationException(
                $"Failed to delete sidecar '{filePath}': {ex.Message}", ex);
        }

        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------
    // Atomic write helper
    // ---------------------------------------------------------------

    private static async Task AtomicWriteAsync(string targetPath, string content,
        CancellationToken ct)
    {
        var tempPath = targetPath + ".tmp." + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, ct)
                .ConfigureAwait(false);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }
    }
}
