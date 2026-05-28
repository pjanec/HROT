using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;

namespace Fdp.Core.Tests.Serialization.Migrations;

/// <summary>
/// Minimal stub migrator for registry and pipeline tests. Records whether
/// Apply was called. Never throws.
/// </summary>
internal sealed class StubMigrator : IJsonDocumentMigrator
{
    public string DocType { get; }
    public int FromVersion { get; }
    public int ToVersion { get; }
    public int ApplyCallCount { get; private set; }

    public StubMigrator(string docType, int from, int to)
    {
        DocType = docType;
        FromVersion = from;
        ToVersion = to;
    }

    public void Apply(JsonObject root, MigrationContext ctx)
    {
        ApplyCallCount++;
    }
}

/// <summary>
/// Builds a complete up+down migrator pair for adjacent versions.
/// </summary>
internal static class MigratorFactory
{
    public static IEnumerable<IJsonDocumentMigrator> MakePair(string docType, int fromVersion)
        => new IJsonDocumentMigrator[]
        {
            new StubMigrator(docType, fromVersion, fromVersion + 1),
            new StubMigrator(docType, fromVersion + 1, fromVersion)
        };

    /// <summary>
    /// Creates pairs for all steps from 1 to <paramref name="currentVersion"/> - 1.
    /// </summary>
    public static IEnumerable<IJsonDocumentMigrator> MakeAllPairs(string docType, int currentVersion)
    {
        var all = new List<IJsonDocumentMigrator>();
        for (int v = 1; v < currentVersion; v++)
            all.AddRange(MakePair(docType, v));
        return all;
    }
}
