using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations.Internal;

namespace Fdp.Core.Serialization.Migrations.Adapters;

/// <summary>
/// Editor-facing migration adapter that writes pre-migration snapshots,
/// computes unknowns journals for down-migrations (Round-Trip Diff algorithm),
/// and saves back with journal restoration.
/// See design §7.2 and §7.3.
/// </summary>
public sealed class PersistentMigrationAdapter
{
    private readonly MigrationPipeline _pipeline;
    private readonly IMigrationStorage _storage;
    private readonly Func<string> _engineVersionProvider;
    private readonly string _writerIdentifier;

    /// <summary>Creates an adapter backed by the given pipeline and storage.</summary>
    internal PersistentMigrationAdapter(
        MigrationPipeline pipeline,
        IMigrationStorage storage,
        Func<string> engineVersionProvider,
        string writerIdentifier)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _engineVersionProvider = engineVersionProvider ?? throw new ArgumentNullException(nameof(engineVersionProvider));
        _writerIdentifier = writerIdentifier ?? throw new ArgumentNullException(nameof(writerIdentifier));
    }

    // ---------------------------------------------------------------
    // Load
    // ---------------------------------------------------------------

    /// <summary>
    /// Loads the document at <paramref name="path"/> from storage, migrates it
    /// if necessary, and returns a <see cref="MigrationLoadResult"/> that carries
    /// the DOM and all sidecar metadata needed for a subsequent
    /// <see cref="SaveAsync"/> call.
    /// </summary>
    public async Task<MigrationLoadResult> LoadAndMigrateAsync(
        string path,
        CancellationToken ct = default)
    {
        // 1. Read raw content from storage.
        var rawText = await _storage.ReadOriginalAsync(path, ct).ConfigureAwait(false);
        if (rawText is null)
            throw new MigrationException($"File not found in storage: {path}");

        // 2. Peek envelope (streaming from string).
        var utf8 = System.Text.Encoding.UTF8.GetBytes(rawText);
        var diskMeta = JsonEnvelope.Peek(utf8.AsSpan());
        string docType = diskMeta.DocType;
        int diskVersion = diskMeta.SchemaVersion;

        // 3. Compute content hash.
        var contentHash = HashUtilities.ComputeContentHash(rawText);

        // 4. Determine current version.
        int currentVersion = _pipeline.GetCurrentVersion(docType);

        // Case A: already at current version — fast path.
        if (diskVersion == currentVersion)
        {
            var dom = JsonNode.Parse(rawText)!.AsObject();
            return new MigrationLoadResult
            {
                Dom = dom,
                OriginalMeta = diskMeta,
                CurrentMeta = diskMeta,
                HasUnknownsJournal = false,
                IsDegraded = false,
                SourceContentHash = contentHash
            };
        }

        // Case B: older version — up-migrate, write snapshot.
        if (diskVersion < currentVersion)
        {
            await _storage.WriteSnapshotAsync(path, diskVersion, contentHash, rawText, ct)
                .ConfigureAwait(false);

            var dom = JsonNode.Parse(rawText)!.AsObject();
            var report = _pipeline.MigrateToCurrent(dom, path);
            var updatedMeta = JsonEnvelope.Read(dom);

            await PruneStaleAsync(path, contentHash, contentHash, ct).ConfigureAwait(false);

            return new MigrationLoadResult
            {
                Dom = dom,
                OriginalMeta = diskMeta,
                CurrentMeta = updatedMeta,
                HasUnknownsJournal = false,
                IsDegraded = false,
                Report = report,
                SourceContentHash = contentHash
            };
        }

        // diskVersion > currentVersion

        // Case C: newer version with a down-migration chain.
        if (_pipeline.CanMigrateTo(docType, diskVersion, currentVersion))
        {
            var dom = JsonNode.Parse(rawText)!.AsObject();
            var preDown = DeepClone(dom);

            var report = _pipeline.MigrateTo(dom, currentVersion, path);
            var updatedMeta = JsonEnvelope.Read(dom);

            var journal = UnknownsJournal.Compute(
                preDown, dom,
                docType, diskVersion, currentVersion,
                contentHash,
                _engineVersionProvider(),
                _writerIdentifier);

            bool hasJournal;
            if (journal.Operations.Count > 0)
            {
                await _storage.WriteJournalAsync(path, journal, ct).ConfigureAwait(false);
                hasJournal = true;
            }
            else
            {
                journal = null;
                hasJournal = false;
            }

            await PruneStaleAsync(path, contentHash, contentHash, ct).ConfigureAwait(false);

            return new MigrationLoadResult
            {
                Dom = dom,
                OriginalMeta = diskMeta,
                CurrentMeta = updatedMeta,
                HasUnknownsJournal = hasJournal,
                Journal = journal,
                IsDegraded = false,
                Report = report,
                SourceContentHash = contentHash
            };
        }

        // Case D: newer version, no down-migration chain — fall back to snapshot.
        {
            var snapshot = await _storage.FindBestSnapshotAsync(path, currentVersion, ct)
                .ConfigureAwait(false);

            if (snapshot is null)
                throw new MigrationException(
                    $"Cannot down-migrate '{docType}' from v{diskVersion} to v{currentVersion}: " +
                    $"no migration chain exists and no usable snapshot was found. " +
                    $"Source path: {path}");

            var snapshotDom = JsonNode.Parse(snapshot.Content)!.AsObject();
            var snapshotMeta = JsonEnvelope.Read(snapshotDom);

            // Up-migrate snapshot DOM to currentVersion if needed.
            MigrationReport? snapshotReport = null;
            if (snapshotMeta.SchemaVersion < currentVersion)
                snapshotReport = _pipeline.MigrateToCurrent(snapshotDom, path);

            var currentMeta = JsonEnvelope.Read(snapshotDom);

            return new MigrationLoadResult
            {
                Dom = snapshotDom,
                OriginalMeta = diskMeta,
                CurrentMeta = currentMeta,
                HasUnknownsJournal = false,
                IsDegraded = true,
                UsedSnapshotPath = snapshot.SidecarPath,
                Report = snapshotReport,
                SourceContentHash = contentHash
            };
        }
    }

    // ---------------------------------------------------------------
    // Save
    // ---------------------------------------------------------------

    /// <summary>
    /// Saves <paramref name="dom"/> back to <paramref name="path"/>, restoring
    /// any higher-version-exclusive content captured in <paramref name="priorLoad"/>.
    /// </summary>
    public async Task SaveAsync(
        string path,
        JsonObject dom,
        MigrationLoadResult priorLoad,
        CancellationToken ct = default)
    {
        if (dom is null) throw new ArgumentNullException(nameof(dom));
        if (priorLoad is null) throw new ArgumentNullException(nameof(priorLoad));

        JsonObject domToSave;
        int targetVersion;

        if (priorLoad.HasUnknownsJournal)
        {
            // Up-migrate from the down-migrated version back to the original disk version,
            // then apply the journal to restore v_higher-exclusive content.
            domToSave = DeepClone(dom);
            _pipeline.MigrateTo(domToSave, priorLoad.Journal!.SourceFileVersion, path);
            priorLoad.Journal!.ApplyTo(domToSave);
            targetVersion = priorLoad.Journal!.SourceFileVersion;
        }
        else
        {
            domToSave = dom;
            targetVersion = priorLoad.CurrentMeta.SchemaVersion;
        }

        // Stamp $meta fields.
        var metaObj = domToSave["$meta"] as JsonObject
            ?? throw new MigrationException(
                $"Document is missing '$meta' object. Source path: {path}");

        metaObj["schemaVersion"] = targetVersion;
        metaObj["engineVersion"] = _engineVersionProvider();
        if (metaObj["createdBy"] is null)
            metaObj["createdBy"] = _writerIdentifier;
        // createdUtc: do not touch

        // Write the file.
        var json = domToSave.ToJsonString();
        await _storage.WriteOriginalAsync(path, json, ct).ConfigureAwait(false);

        // Delete the journal sidecar if one was used.
        if (priorLoad.HasUnknownsJournal)
            await _storage.DeleteJournalAsync(path, priorLoad.Journal!, ct).ConfigureAwait(false);

        // Prune stale sidecars based on the new file hash.
        // Also keep sidecars that match the original source hash (e.g. the v_old
        // snapshot written on load) so they survive the save of the up-migrated file.
        var newHash = HashUtilities.ComputeContentHash(json);
        await PruneStaleAsync(path, newHash, priorLoad.SourceContentHash, ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------

    private static JsonObject DeepClone(JsonObject source)
        => JsonNode.Parse(source.ToJsonString())!.AsObject();

    private async Task PruneStaleAsync(string path, string newHash, string oldSourceHash, CancellationToken ct)
    {
        var sidecars = await _storage.ListSidecarsAsync(path, ct).ConfigureAwait(false);
        foreach (var s in sidecars)
        {
            // Keep sidecars that match either the new saved file hash or the
            // original source file hash (snapshot written on the preceding load).
            if (!string.Equals(s.ContentHash, newHash, StringComparison.Ordinal)
                && !string.Equals(s.ContentHash, oldSourceHash, StringComparison.Ordinal))
            {
                await _storage.DeleteSidecarAsync(path, s.FileName, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Enumerates sidecar files (snapshots and journals) stored alongside
    /// <paramref name="originalPath"/>. Returns an empty list when no sidecar
    /// directory exists. Delegates to <see cref="IMigrationStorage.ListSidecarsAsync"/>.
    /// </summary>
    public Task<IReadOnlyList<SidecarFileInfo>> ListSidecarsAsync(
        string originalPath,
        CancellationToken ct = default)
        => _storage.ListSidecarsAsync(originalPath, ct);
}
