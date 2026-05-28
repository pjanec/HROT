namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// Up-migrates a Blueprint JSON document to the current registered schema version
/// before sanitization, so that version-A and version-B are compared at the same
/// schema level regardless of which engine version saved them.
/// See design §3.5 step 0 and §8.1.
/// A no-op implementation is provided in TASK-C-08 until the Migration System lands.
/// </summary>
public interface IComparisonMigrationAdapter
{
    /// <summary>
    /// Migrates <paramref name="rawJson"/> to the current schema version.
    /// Returns the migrated JSON text and sets <paramref name="didMigrate"/> to true
    /// when the schema version was actually advanced. Returns the input unchanged
    /// (with <paramref name="didMigrate"/> false) when no migration is needed or possible.
    /// Must never throw.
    /// </summary>
    string Adapt(string rawJson, out bool didMigrate);
}
