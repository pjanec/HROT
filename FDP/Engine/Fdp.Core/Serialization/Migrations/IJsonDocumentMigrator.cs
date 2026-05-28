using System.Text.Json.Nodes;

namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// Describes a single versioned migration step for a JSON document type.
/// </summary>
public interface IJsonDocumentMigrator
{
    /// <summary>The document type this migrator handles.</summary>
    string DocType { get; }

    /// <summary>The schema version this migrator transforms FROM.</summary>
    int FromVersion { get; }

    /// <summary>The schema version this migrator transforms TO.</summary>
    int ToVersion { get; }

    /// <summary>
    /// Transforms <paramref name="root"/> in-place from
    /// <see cref="FromVersion"/> to <see cref="ToVersion"/>.
    /// </summary>
    void Apply(JsonObject root, MigrationContext ctx);
}
