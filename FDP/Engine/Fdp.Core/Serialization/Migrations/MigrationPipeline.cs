using System;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// Orchestrates migration of a JSON document through the registry's
/// migrator chain. Enforces post-migrator invariants and returns a
/// <see cref="MigrationReport"/> describing what was done.
/// </summary>
public sealed class MigrationPipeline
{
    private readonly MigrationRegistry _registry;

    /// <summary>Creates a pipeline backed by the given registry.</summary>
    public MigrationPipeline(MigrationRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Migrates <paramref name="root"/> to the current version registered
    /// for its document type.
    /// </summary>
    public MigrationReport MigrateToCurrent(JsonObject root, string? sourcePath = null)
    {
        if (root is null) throw new ArgumentNullException(nameof(root));

        var meta = JsonEnvelope.Read(root);
        int targetVersion = _registry.GetCurrentVersion(meta.DocType);
        return MigrateTo(root, targetVersion, sourcePath);
    }

    /// <summary>
    /// Migrates <paramref name="root"/> to <paramref name="targetVersion"/>.
    /// Returns an empty report if the document is already at the target version
    /// or if the doc type is a passthrough type.
    /// </summary>
    public MigrationReport MigrateTo(JsonObject root, int targetVersion, string? sourcePath = null)
    {
        if (root is null) throw new ArgumentNullException(nameof(root));

        var meta = JsonEnvelope.Read(root);
        string docType = meta.DocType;
        int fromVersion = meta.SchemaVersion;

        // Passthrough doc types need no migration.
        if (_registry.IsPassthrough(docType))
            return new MigrationReport(docType, fromVersion, fromVersion, MigrationDirection.Up);

        // Already at target: nothing to do.
        if (fromVersion == targetVersion)
            return new MigrationReport(docType, fromVersion, targetVersion, MigrationDirection.Up);

        var direction = targetVersion > fromVersion ? MigrationDirection.Up : MigrationDirection.Down;
        var chain = _registry.GetPath(docType, fromVersion, targetVersion);

        var ctx = new MigrationContext(docType, fromVersion, targetVersion, direction, sourcePath);
        var sw = Stopwatch.StartNew();

        foreach (var migrator in chain)
        {
            // Snapshot the $meta object reference and its fields before Apply.
            var metaBefore = root["$meta"] as JsonObject
                ?? throw new MigrationException(
                    $"Document of type '{docType}' is missing '$meta' object.",
                    docType, fromVersion, targetVersion, sourcePath, "$meta");

            string? snapshotDocType     = SnapshotField(metaBefore, "docType");
            string? snapshotVersion     = SnapshotField(metaBefore, "schemaVersion");
            string? snapshotEngineVer   = SnapshotField(metaBefore, "engineVersion");
            string? snapshotCreatedBy   = SnapshotField(metaBefore, "createdBy");
            string? snapshotCreatedUtc  = SnapshotField(metaBefore, "createdUtc");

            try
            {
                migrator.Apply(root, ctx);
            }
            catch (MigrationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new MigrationException(
                    $"Migrator '{migrator.GetType().Name}' failed during '{docType}' migration " +
                    $"step {migrator.FromVersion}->{migrator.ToVersion}: {ex.Message}",
                    docType, fromVersion, targetVersion, sourcePath, ctx.CurrentPath, ex);
            }

            // --- Invariant checks ---

            // Invariant 1: $meta object identity must not change.
            var metaAfter = root["$meta"] as JsonObject;
            if (!ReferenceEquals(metaAfter, metaBefore))
                throw new MigrationException(
                    $"Migrator '{migrator.GetType().Name}' violated invariant 1: " +
                    $"'$meta' object was replaced by the migrator.",
                    docType, fromVersion, targetVersion, sourcePath, "$meta");

            // Invariant 2: $meta.docType must be unchanged.
            string? afterDocType = SnapshotField(metaAfter!, "docType");
            if (afterDocType != snapshotDocType)
                throw new MigrationException(
                    $"Migrator '{migrator.GetType().Name}' violated invariant 2: " +
                    $"'$meta.docType' changed from {snapshotDocType} to {afterDocType}.",
                    docType, fromVersion, targetVersion, sourcePath, "$meta.docType");

            // Invariant 3: $meta.schemaVersion must not be touched by the migrator;
            // the pipeline sets it after each successful step.
            string? afterVersion = SnapshotField(metaAfter!, "schemaVersion");
            if (afterVersion != snapshotVersion)
                throw new MigrationException(
                    $"Migrator '{migrator.GetType().Name}' violated invariant 3: " +
                    $"'$meta.schemaVersion' was changed (was {snapshotVersion}, now {afterVersion}); " +
                    $"the pipeline sets schemaVersion, not the migrator.",
                    docType, fromVersion, targetVersion, sourcePath, "$meta.schemaVersion");

            // Invariant 4: diagnostic fields must be unchanged.
            if (SnapshotField(metaAfter!, "engineVersion") != snapshotEngineVer
                || SnapshotField(metaAfter!, "createdBy")    != snapshotCreatedBy
                || SnapshotField(metaAfter!, "createdUtc")   != snapshotCreatedUtc)
            {
                throw new MigrationException(
                    $"Migrator '{migrator.GetType().Name}' violated invariant 4: " +
                    $"diagnostic fields (engineVersion, createdBy, createdUtc) were modified.",
                    docType, fromVersion, targetVersion, sourcePath, "$meta");
            }

            // Pipeline sets $meta.schemaVersion after a successful step.
            metaAfter!["schemaVersion"] = migrator.ToVersion;
        }

        sw.Stop();
        ctx.Report.Duration = sw.Elapsed;
        return ctx.Report;
    }

    // ---------------------------------------------------------------
    // Internal helpers (accessible to Adapters via InternalsVisibleTo)
    // ---------------------------------------------------------------

    /// <summary>
    /// Returns the current schema version registered for <paramref name="docType"/>.
    /// Delegates to the registry; throws <see cref="MigrationException"/> if the
    /// doc type is not registered.
    /// </summary>
    internal int GetCurrentVersion(string docType)
        => _registry.GetCurrentVersion(docType);

    /// <summary>
    /// Returns true if the registry can migrate <paramref name="docType"/>
    /// from <paramref name="fromVersion"/> to <paramref name="toVersion"/>
    /// without any gaps. Never throws.
    /// </summary>
    internal bool CanMigrateTo(string docType, int fromVersion, int toVersion)
        => _registry.CanMigrate(docType, fromVersion, toVersion);

    // ---------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Returns a stable JSON-string snapshot of a $meta field for invariant
    /// comparison. Returns null when the field is absent.
    /// </summary>
    private static string? SnapshotField(JsonObject meta, string key)
        => meta[key]?.ToJsonString();
}
