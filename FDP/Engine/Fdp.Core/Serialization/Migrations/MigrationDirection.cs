namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// Indicates whether a migrator transforms a document to a higher
/// or lower schema version.
/// </summary>
public enum MigrationDirection
{
    /// <summary>Migrator transforms from FromVersion to ToVersion where ToVersion = FromVersion + 1.</summary>
    Up,

    /// <summary>Migrator transforms from FromVersion to ToVersion where ToVersion = FromVersion - 1.</summary>
    Down
}
