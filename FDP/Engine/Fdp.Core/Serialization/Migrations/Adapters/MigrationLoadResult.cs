using System.Text.Json.Nodes;

namespace Fdp.Core.Serialization.Migrations.Adapters;

/// <summary>
/// The result of <see cref="PersistentMigrationAdapter.LoadAndMigrateAsync"/>.
/// Carries the migrated DOM and the metadata needed to save it back correctly.
/// See design §7.3.
/// </summary>
public sealed class MigrationLoadResult
{
    /// <summary>
    /// The DOM as the caller should see it, migrated to the current registered
    /// version (or to the snapshot's version if degraded fallback was used).
    /// Always non-null; always a complete <see cref="JsonObject"/>.
    /// </summary>
    public JsonObject Dom { get; init; } = null!;

    /// <summary>The <c>$meta</c> envelope as it existed on disk before any migration.</summary>
    public DocumentMeta OriginalMeta { get; init; } = null!;

    /// <summary>The <c>$meta</c> envelope as the DOM is now shaped, after migration.</summary>
    public DocumentMeta CurrentMeta { get; init; } = null!;

    /// <summary>True if up- or down-migration was performed.</summary>
    public bool WasMigrated => OriginalMeta.SchemaVersion != CurrentMeta.SchemaVersion;

    /// <summary>
    /// True if down-migration was performed AND the resulting journal had at
    /// least one operation. False when no down-migration occurred OR the
    /// down-migration was loss-free (empty journal not written).
    /// When false, <see cref="PersistentMigrationAdapter.SaveAsync"/> skips
    /// journal application entirely.
    /// </summary>
    public bool HasUnknownsJournal { get; init; }

    /// <summary>
    /// True if the load fell back to a snapshot because down-migration was
    /// unavailable. Callers should surface a warning UI.
    /// </summary>
    public bool IsDegraded { get; init; }

    /// <summary>Path of the snapshot used during degraded fallback, if any.</summary>
    public string? UsedSnapshotPath { get; init; }

    /// <summary>The migration report, or null if no migration was performed.</summary>
    public MigrationReport? Report { get; init; }

    /// <summary>
    /// The journal, used by <see cref="PersistentMigrationAdapter.SaveAsync"/>.
    /// Non-null if and only if <see cref="HasUnknownsJournal"/> is true.
    /// </summary>
    internal UnknownsJournal? Journal { get; init; }

    /// <summary>
    /// The content hash of the source file (SHA-256, hex16), used to locate
    /// and verify the journal on save-back.
    /// </summary>
    internal string SourceContentHash { get; init; } = null!;
}
