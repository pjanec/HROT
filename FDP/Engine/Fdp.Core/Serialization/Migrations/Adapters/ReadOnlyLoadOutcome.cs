using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fdp.Core.Serialization.Migrations.Adapters;

/// <summary>
/// The result of <see cref="ReadOnlyMigrationAdapter.LoadAndMigrateAsync"/>.
/// When <see cref="WasMigrated"/> is false, <see cref="RawContent"/> carries the
/// file content without DOM allocation. When true, <see cref="MigratedDom"/>
/// is the migrated DOM.
/// </summary>
public sealed class ReadOnlyLoadOutcome
{
    // Read-only properties; set by the adapter.
    public DocumentMeta Meta { get; init; } = null!;
    public bool WasMigrated { get; init; }
    public string? RawContent { get; init; }
    public JsonObject? MigratedDom { get; init; }
    public MigrationReport? Report { get; init; }

    /// <summary>
    /// Returns a parsed JsonObject regardless of which path was taken.
    /// On the fast path, parses RawContent (allocates a DOM).
    /// On the slow path, returns MigratedDom directly.
    /// </summary>
    public JsonObject AsJsonObject()
    {
        if (MigratedDom is not null)
            return MigratedDom;
        if (RawContent is not null)
            return JsonNode.Parse(RawContent)!.AsObject();
        throw new InvalidOperationException(
            "ReadOnlyLoadOutcome has neither RawContent nor MigratedDom.");
    }

    /// <summary>
    /// Returns the JSON text regardless of which path was taken.
    /// On the slow path, serializes MigratedDom.
    /// </summary>
    public string AsJsonString()
    {
        if (RawContent is not null)
            return RawContent;
        if (MigratedDom is not null)
            return MigratedDom.ToJsonString();
        throw new InvalidOperationException(
            "ReadOnlyLoadOutcome has neither RawContent nor MigratedDom.");
    }
}
