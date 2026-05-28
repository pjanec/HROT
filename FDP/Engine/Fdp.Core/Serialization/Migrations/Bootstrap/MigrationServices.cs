using Fdp.Core.Serialization.Migrations.Adapters;

namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// A bundle of the migration infrastructure components. Constructed once
/// per process by <see cref="MigrationBootstrap"/> and consumed by the
/// subsystems that load/save versioned JSON.
/// </summary>
public sealed record MigrationServices(
    MigrationRegistry Registry,
    MigrationPipeline Pipeline,
    ReadOnlyMigrationAdapter ReadOnly,
    PersistentMigrationAdapter Persistent);
