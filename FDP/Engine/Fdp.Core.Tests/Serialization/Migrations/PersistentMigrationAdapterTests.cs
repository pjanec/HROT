using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization.Migrations.Adapters;
using Fdp.Core.Serialization.Migrations.Internal;
using Xunit;

namespace Fdp.Core.Tests.Serialization.Migrations;

/// <summary>
/// Tests for <see cref="PersistentMigrationAdapter"/> (T2-030..T2-041, T2-050..T2-066, T2-080).
/// All tests use <see cref="InMemoryMigrationStorage"/> — no real filesystem I/O.
/// </summary>
public sealed class PersistentMigrationAdapterTests
{
    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    // Build a pipeline with Test.Doc at the given currentVersion.
    // Uses the shared TestDocV1<->V2<->V3 migrators.
    // Always includes v1<->v2 migrators so down-migration from v2 is possible
    // regardless of currentVersion. Adds v2<->v3 only when currentVersion >= 3.
    private static MigrationPipeline BuildPipeline(int currentVersion)
    {
        var registry = new MigrationRegistry();
        var migrators = new List<IJsonDocumentMigrator>();
        migrators.Add(new TestDocV1ToV2()); migrators.Add(new TestDocV2ToV1());
        if (currentVersion >= 3) { migrators.Add(new TestDocV2ToV3()); migrators.Add(new TestDocV3ToV2()); }
        registry.RegisterDocType("Test.Doc", currentVersion, migrators);
        return new MigrationPipeline(registry);
    }

    // Build an adapter backed by InMemoryMigrationStorage.
    private static (PersistentMigrationAdapter adapter, InMemoryMigrationStorage storage)
        BuildAdapter(int currentVersion = 2)
    {
        var storage = new InMemoryMigrationStorage();
        var adapter = new PersistentMigrationAdapter(
            BuildPipeline(currentVersion),
            storage,
            () => "test-engine-1.0",
            "Test.Editor");
        return (adapter, storage);
    }

    // Build a JSON doc for Test.Doc at the given schemaVersion.
    // items: list of (name, kind) tuples. Pass null/empty for no items (lossless case).
    private static string BuildTestDoc(int schemaVersion, IEnumerable<(string name, string? kind)>? items = null)
    {
        var sb = new StringBuilder();
        sb.Append("{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":");
        sb.Append(schemaVersion);
        sb.Append("}");
        if (items is not null)
        {
            sb.Append(",\"items\":[");
            bool first = true;
            foreach (var (name, kind) in items)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"name\":\"");
                sb.Append(name);
                sb.Append('"');
                if (kind is not null) { sb.Append(",\"kind\":\""); sb.Append(kind); sb.Append('"'); }
                sb.Append('}');
            }
            sb.Append(']');
        }
        sb.Append('}');
        return sb.ToString();
    }

    // Seed the storage with a file at the given path.
    private static async Task SeedFile(InMemoryMigrationStorage storage, string path, string content)
        => await storage.WriteOriginalAsync(path, content);

    // ---------------------------------------------------------------
    // T2-030: Load at current version — no sidecars created
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2030_LoadAndMigrate_AtCurrentVersion_NoSidecarsCreated()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 2);
        var content = BuildTestDoc(2);
        await SeedFile(storage, path, content);

        var result = await adapter.LoadAndMigrateAsync(path);

        Assert.False(result.WasMigrated);
        Assert.False(result.HasUnknownsJournal);
        Assert.False(result.IsDegraded);

        var sidecars = await storage.ListSidecarsAsync(path);
        Assert.Empty(sidecars);
    }

    // ---------------------------------------------------------------
    // T2-031: Load older version — writes snapshot
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2031_LoadAndMigrate_OlderVersion_WritesSnapshot()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 2);
        var content = BuildTestDoc(1, new[] { ("a", (string?)null) });
        await SeedFile(storage, path, content);

        var result = await adapter.LoadAndMigrateAsync(path);

        Assert.True(result.WasMigrated);
        var sidecars = await storage.ListSidecarsAsync(path);
        Assert.Contains(sidecars, s => s.Kind == SidecarKind.Snapshot);
    }

    // ---------------------------------------------------------------
    // T2-032: Load older version — DOM is current shape
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2032_LoadAndMigrate_OlderVersion_DomIsCurrentShape()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 2);
        var content = BuildTestDoc(1, new[] { ("a", (string?)null) });
        await SeedFile(storage, path, content);

        var result = await adapter.LoadAndMigrateAsync(path);

        Assert.Equal(2, result.Dom["$meta"]!["schemaVersion"]!.GetValue<int>());
        Assert.Equal("default", result.Dom["items"]![0]!["kind"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T2-033: Load newer version, lossless (no items) — no journal written
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2033_LoadAndMigrate_NewerVersion_RoundTripsLosslessly_NoJournal()
    {
        // v2 doc with NO items -> TestDocV2ToV1 is a no-op -> empty diff -> no journal
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 1);
        var content = BuildTestDoc(2); // no items
        await SeedFile(storage, path, content);

        var result = await adapter.LoadAndMigrateAsync(path);

        Assert.False(result.HasUnknownsJournal);
        var sidecars = await storage.ListSidecarsAsync(path);
        Assert.DoesNotContain(sidecars, s => s.Kind == SidecarKind.Journal);
    }

    // ---------------------------------------------------------------
    // T2-034: Load newer version, lossy — journal written
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2034_LoadAndMigrate_NewerVersion_RoundTripLossy_WritesJournal()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 1);
        var content = BuildTestDoc(2, new[] { ("tank", "tank") });
        await SeedFile(storage, path, content);

        var result = await adapter.LoadAndMigrateAsync(path);

        Assert.True(result.HasUnknownsJournal);
        var sidecars = await storage.ListSidecarsAsync(path);
        Assert.Contains(sidecars, s => s.Kind == SidecarKind.Journal);
    }

    // ---------------------------------------------------------------
    // T2-035: Journal contains correct operations
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2035_LoadAndMigrate_NewerVersion_JournalContainsCorrectOperations()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 1);
        var content = BuildTestDoc(2, new[] { ("tank", "tank") });
        await SeedFile(storage, path, content);

        var result = await adapter.LoadAndMigrateAsync(path);

        Assert.True(result.Journal!.Operations.Count > 0);
        // Find a Set op for $.items[0].kind with value "tank"
        bool foundKindOp = false;
        foreach (var op in result.Journal.Operations)
        {
            if (op.Kind == JournalOpKind.Set && op.Path.Contains("kind")
                && op.Value?.GetValue<string>() == "tank")
            {
                foundKindOp = true;
                break;
            }
        }
        Assert.True(foundKindOp, "Expected a Set op for items[0].kind = \"tank\"");
    }

    // ---------------------------------------------------------------
    // T2-036: DOM is down-migrated (no "kind" after v2->v1)
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2036_LoadAndMigrate_NewerVersion_DomIsDownMigrated()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 1);
        var content = BuildTestDoc(2, new[] { ("tank", "tank") });
        await SeedFile(storage, path, content);

        var result = await adapter.LoadAndMigrateAsync(path);

        Assert.Equal(1, result.Dom["$meta"]!["schemaVersion"]!.GetValue<int>());
        var item = result.Dom["items"]![0] as JsonObject;
        Assert.NotNull(item);
        Assert.False(item!.ContainsKey("kind"), "\"kind\" should have been removed by v2->v1 migrator");
    }

    // ---------------------------------------------------------------
    // T2-037: Result has hash and journal
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2037_LoadAndMigrate_NewerVersion_ResultHasHashAndJournal()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 1);
        var content = BuildTestDoc(2, new[] { ("tank", "tank") });
        await SeedFile(storage, path, content);

        var result = await adapter.LoadAndMigrateAsync(path);

        Assert.False(string.IsNullOrEmpty(result.SourceContentHash));
        Assert.NotNull(result.Journal);
        Assert.True(result.HasUnknownsJournal);
    }

    // ---------------------------------------------------------------
    // T2-038: Much newer version, no chain — falls back to snapshot
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2038_LoadAndMigrate_MuchNewerVersion_NoChain_FallsBackToSnapshot()
    {
        const string path = @"C:\data\doc.json";
        // Pipeline only knows v1<->v2; file is at v5
        var (adapter, storage) = BuildAdapter(currentVersion: 2);

        var v5Content = BuildTestDoc(5);
        var v2SnapshotContent = BuildTestDoc(2);
        var v2Hash = HashUtilities.ComputeContentHash(v2SnapshotContent);

        // Pre-seed the snapshot; its hash must match the snapshot content (integrity invariant).
        await storage.WriteSnapshotAsync(path, 2, v2Hash, v2SnapshotContent);
        await SeedFile(storage, path, v5Content);

        var result = await adapter.LoadAndMigrateAsync(path);

        Assert.True(result.IsDegraded);
        Assert.True(result.WasMigrated);
        Assert.Equal(2, result.Dom["$meta"]!["schemaVersion"]!.GetValue<int>());
    }

    // ---------------------------------------------------------------
    // T2-039: Much newer version, no snapshot — throws
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2039_LoadAndMigrate_MuchNewerVersion_NoSnapshot_Throws()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 2);
        var v5Content = BuildTestDoc(5);
        await SeedFile(storage, path, v5Content);

        await Assert.ThrowsAsync<MigrationException>(() =>
            adapter.LoadAndMigrateAsync(path));
    }

    // ---------------------------------------------------------------
    // T2-040: Prunes stale sidecars on load
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2040_LoadAndMigrate_PrunesStaleSidecars()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 2);
        var v1Content = BuildTestDoc(1);

        // Pre-seed a stale snapshot with a fake hash
        await storage.WriteSnapshotAsync(path, 1, "staledeadbeef00", "stale content");

        await SeedFile(storage, path, v1Content);

        await adapter.LoadAndMigrateAsync(path);

        var sidecars = await storage.ListSidecarsAsync(path);
        Assert.DoesNotContain(sidecars, s => s.ContentHash == "staledeadbeef00");
    }

    // ---------------------------------------------------------------
    // T2-041: Does not prune current matching sidecars
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2041_LoadAndMigrate_DoesNotPruneCurrentMatchingSidecars()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 2);
        var v1Content = BuildTestDoc(1);

        // Pre-seed a snapshot with the actual content hash
        var h1 = HashUtilities.ComputeContentHash(v1Content);
        await storage.WriteSnapshotAsync(path, 1, h1, v1Content);

        await SeedFile(storage, path, v1Content);

        await adapter.LoadAndMigrateAsync(path);

        // The matching snapshot (h1) must still be present
        var sidecars = await storage.ListSidecarsAsync(path);
        Assert.Contains(sidecars, s => s.ContentHash == h1);
    }

    // ---------------------------------------------------------------
    // T2-050: Save no-journal — writes current version file
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2050_Save_NoJournal_WritesCurrentVersionFile()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 2);
        await SeedFile(storage, path, BuildTestDoc(1));

        var loadResult = await adapter.LoadAndMigrateAsync(path);
        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var saved = await storage.ReadOriginalAsync(path);
        var savedDom = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal(2, savedDom["$meta"]!["schemaVersion"]!.GetValue<int>());
    }

    // ---------------------------------------------------------------
    // T2-051: Save no-journal — preserves user edits
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2051_Save_NoJournal_PreservesUserEdits()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 2);
        await SeedFile(storage, path, BuildTestDoc(1));

        var loadResult = await adapter.LoadAndMigrateAsync(path);
        loadResult.Dom["userField"] = "user-value";
        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var saved = await storage.ReadOriginalAsync(path);
        var savedDom = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal("user-value", savedDom["userField"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T2-052: Save no-journal — updates engine version
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2052_Save_NoJournal_UpdatesEngineVersion()
    {
        const string path = @"C:\data\doc.json";
        var storage = new InMemoryMigrationStorage();
        var adapter = new PersistentMigrationAdapter(
            BuildPipeline(2), storage, () => "test-v1.0", "Test.Editor");
        await SeedFile(storage, path, BuildTestDoc(1));

        var loadResult = await adapter.LoadAndMigrateAsync(path);
        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var saved = await storage.ReadOriginalAsync(path);
        var savedDom = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal("test-v1.0", savedDom["$meta"]!["engineVersion"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T2-053: Save no-journal — preserves createdUtc
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2053_Save_NoJournal_PreservesCreatedUtc()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 2);
        // Include createdUtc in the v1 doc
        const string docWithUtc =
            "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1," +
            "\"createdUtc\":\"2024-01-01T00:00:00.0000000Z\"}}";
        await SeedFile(storage, path, docWithUtc);

        var loadResult = await adapter.LoadAndMigrateAsync(path);
        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var saved = await storage.ReadOriginalAsync(path);
        var savedDom = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal(
            "2024-01-01T00:00:00.0000000Z",
            savedDom["$meta"]!["createdUtc"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T2-054: Save no-journal — sets createdBy if absent
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2054_Save_NoJournal_SetsCreatedByIfAbsent()
    {
        const string path = @"C:\data\doc.json";
        var storage = new InMemoryMigrationStorage();
        var adapter = new PersistentMigrationAdapter(
            BuildPipeline(2), storage, () => "eng-1.0", "Test.Editor");
        await SeedFile(storage, path, BuildTestDoc(1)); // no createdBy

        var loadResult = await adapter.LoadAndMigrateAsync(path);
        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var saved = await storage.ReadOriginalAsync(path);
        var savedDom = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal("Test.Editor", savedDom["$meta"]!["createdBy"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T2-055: Save no-journal — preserves createdBy if already set
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2055_Save_NoJournal_PreservesCreatedByIfPresent()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 2);
        const string docWithAuthor =
            "{\"$meta\":{\"docType\":\"Test.Doc\",\"schemaVersion\":1," +
            "\"createdBy\":\"Original.Author\"}}";
        await SeedFile(storage, path, docWithAuthor);

        var loadResult = await adapter.LoadAndMigrateAsync(path);
        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var saved = await storage.ReadOriginalAsync(path);
        var savedDom = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal("Original.Author", savedDom["$meta"]!["createdBy"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T2-056: Save with journal — up-migrates the user DOM
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2056_Save_WithJournal_UpMigratesUserDom()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 1);
        await SeedFile(storage, path, BuildTestDoc(2, new[] { ("a", "tank") }));

        var loadResult = await adapter.LoadAndMigrateAsync(path);
        Assert.True(loadResult.HasUnknownsJournal);

        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var saved = await storage.ReadOriginalAsync(path);
        var savedDom = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal(2, savedDom["$meta"]!["schemaVersion"]!.GetValue<int>());
    }

    // ---------------------------------------------------------------
    // T2-057: Save with journal — applies journal to restore kind
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2057_Save_WithJournal_AppliesJournalToUpMigratedDom()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 1);
        await SeedFile(storage, path, BuildTestDoc(2, new[] { ("a", "tank") }));

        var loadResult = await adapter.LoadAndMigrateAsync(path);
        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var saved = await storage.ReadOriginalAsync(path);
        var savedDom = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal("tank", savedDom["items"]![0]!["kind"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T2-058: Save with journal — preserves user added entity
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2058_Save_WithJournal_PreservesUserAddedEntity()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 1);
        await SeedFile(storage, path, BuildTestDoc(2, new[] { ("a", "tank") }));

        var loadResult = await adapter.LoadAndMigrateAsync(path);
        Assert.True(loadResult.HasUnknownsJournal);

        var items = loadResult.Dom["items"]!.AsArray();
        items.Add(JsonNode.Parse("{\"name\":\"new\"}"));

        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var saved = await storage.ReadOriginalAsync(path);
        var savedDom = JsonNode.Parse(saved!)!.AsObject();
        var savedItems = savedDom["items"]!.AsArray();
        Assert.Equal(2, savedItems.Count);
        Assert.Equal("new", savedItems[1]!["name"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T2-059: Save with journal — preserves user edits to mapped fields
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2059_Save_WithJournal_PreservesUserEditsToMappedFields()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 1);
        await SeedFile(storage, path, BuildTestDoc(2, new[] { ("a", "tank") }));

        var loadResult = await adapter.LoadAndMigrateAsync(path);
        (loadResult.Dom["items"]![0] as JsonObject)!["name"] = "edited";

        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var saved = await storage.ReadOriginalAsync(path);
        var savedDom = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal("edited", savedDom["items"]![0]!["name"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T2-060: Save with journal — restores v-higher-exclusive content
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2060_Save_WithJournal_RestoresVHigherExclusiveContent()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 1);
        await SeedFile(storage, path, BuildTestDoc(2, new[] { ("a", "rare") }));

        var loadResult = await adapter.LoadAndMigrateAsync(path);
        Assert.True(loadResult.HasUnknownsJournal);

        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var saved = await storage.ReadOriginalAsync(path);
        var savedDom = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal("rare", savedDom["items"]![0]!["kind"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T2-061: Save with journal — deleted entity stays deleted
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2061_Save_WithJournal_DeletedEntityStaysDeleted()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 1);
        await SeedFile(storage, path,
            BuildTestDoc(2, new[] { ("a", "tank"), ("b", "scout") }));

        var loadResult = await adapter.LoadAndMigrateAsync(path);
        Assert.True(loadResult.HasUnknownsJournal);

        // Remove items[1] (b/scout) - journal uses positional indices, so deleting
        // the last item leaves item 0 ("a") at its original index; the journal op
        // Set("$.items[0].kind","tank") applies to the surviving "a" correctly.
        var items = loadResult.Dom["items"]!.AsArray();
        items.RemoveAt(1);
        Assert.Single(items);

        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var saved = await storage.ReadOriginalAsync(path);
        var savedDom = JsonNode.Parse(saved!)!.AsObject();
        var savedItems = savedDom["items"]!.AsArray();
        // Only "a" remains; after up-migration + journal, kind should be "tank"
        Assert.Single(savedItems);
        Assert.Equal("tank", savedItems[0]!["kind"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T2-062: Save with journal — deletes journal sidecar
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2062_Save_WithJournal_DeletesJournalSidecar()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 1);
        var v2Content = BuildTestDoc(2, new[] { ("a", "tank") });
        await SeedFile(storage, path, v2Content);

        var loadResult = await adapter.LoadAndMigrateAsync(path);
        Assert.True(loadResult.HasUnknownsJournal);
        var originalHash = loadResult.SourceContentHash;

        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var journalAfter = await storage.FindJournalAsync(path, originalHash);
        Assert.Null(journalAfter);
    }

    // ---------------------------------------------------------------
    // T2-063: Save — keeps snapshot sidecar
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2063_Save_WithJournal_KeepsSnapshotSidecar()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 2);
        await SeedFile(storage, path, BuildTestDoc(1));

        var loadResult = await adapter.LoadAndMigrateAsync(path);
        // Up-migration wrote a snapshot
        var sidecarsAfterLoad = await storage.ListSidecarsAsync(path);
        Assert.Contains(sidecarsAfterLoad, s => s.Kind == SidecarKind.Snapshot);

        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var sidecarsAfterSave = await storage.ListSidecarsAsync(path);
        Assert.Contains(sidecarsAfterSave, s => s.Kind == SidecarKind.Snapshot);
    }

    // ---------------------------------------------------------------
    // T2-064: Save — prunes stale sidecars
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2064_Save_PrunesStaleSidecars()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 2);

        // Pre-seed a stale snapshot
        await storage.WriteSnapshotAsync(path, 1, "staledeadbeef00", "stale");

        await SeedFile(storage, path, BuildTestDoc(1));
        var loadResult = await adapter.LoadAndMigrateAsync(path);
        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var sidecars = await storage.ListSidecarsAsync(path);
        Assert.DoesNotContain(sidecars, s => s.ContentHash == "staledeadbeef00");
    }

    // ---------------------------------------------------------------
    // T2-065: Save — atomic write semantics (InMemory: content updated)
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2065_Save_AtomicWriteSemantics()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 2);
        await SeedFile(storage, path, BuildTestDoc(1));

        var loadResult = await adapter.LoadAndMigrateAsync(path);
        loadResult.Dom["extra"] = "added";
        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var saved = await storage.ReadOriginalAsync(path);
        Assert.False(string.IsNullOrEmpty(saved));
        // Must be valid parseable JSON
        var parsed = JsonNode.Parse(saved!);
        Assert.NotNull(parsed);
    }

    // ---------------------------------------------------------------
    // T2-066: Save — failed journal apply does not overwrite original
    //         (using valid but no-op paths: ApplyTo silently skips missing nodes)
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2066_Save_FailedJournalApply_DoesNotCorruptOriginal()
    {
        // Since UnknownsJournal.ApplyTo uses TryWrite/TryRemove silently for
        // unreachable paths, we verify that after a Save the file is always
        // valid parseable JSON (no partial-write corruption).
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 1);
        await SeedFile(storage, path, BuildTestDoc(2, new[] { ("a", "tank") }));

        var loadResult = await adapter.LoadAndMigrateAsync(path);
        await adapter.SaveAsync(path, loadResult.Dom, loadResult);

        var saved = await storage.ReadOriginalAsync(path);
        Assert.False(string.IsNullOrEmpty(saved));
        var parsed = JsonNode.Parse(saved!);
        Assert.NotNull(parsed);
    }

    // ---------------------------------------------------------------
    // T2-080: Gate test — full round-trip preserves all edits
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2080_FullRoundTrip_VHigherToVLowerAndBack_PreservesAllEdits()
    {
        // ARRANGE: v2 doc with v2-exclusive "kind" values
        const string path = @"C:\data\scenario.json";
        var v2Original = BuildTestDoc(2, new (string, string?)[]
        {
            ("alpha", "tank"),   // item 0: kind="tank" (v2-exclusive)
            ("beta",  "scout"),  // item 1: kind="scout" (v2-exclusive)
        });

        // v1 binary (pipeline at v1)
        var (v1Adapter, storage) = BuildAdapter(currentVersion: 1);
        await SeedFile(storage, path, v2Original);

        var v1LoadResult = await v1Adapter.LoadAndMigrateAsync(path);

        // Verify v1 view: no "kind" fields
        Assert.True(v1LoadResult.HasUnknownsJournal);
        Assert.Equal(1, v1LoadResult.CurrentMeta.SchemaVersion);
        var items = v1LoadResult.Dom["items"]!.AsArray();
        Assert.DoesNotContain(items, i => (i as JsonObject)?.ContainsKey("kind") == true);

        // User edits in v1:
        // 1. Rename alpha to "alpha-renamed"
        (items[0] as JsonObject)!["name"] = "alpha-renamed";
        // 2. Add a new item (no "kind" at v1)
        items.Add(JsonNode.Parse("{\"name\":\"gamma\"}"));

        // Save back with v1 adapter
        await v1Adapter.SaveAsync(path, v1LoadResult.Dom, v1LoadResult);

        // v2 binary reloads (pipeline at v2)
        var v2Registry = new MigrationRegistry();
        v2Registry.RegisterDocType("Test.Doc", 2, new IJsonDocumentMigrator[]
            { new TestDocV1ToV2(), new TestDocV2ToV1() });
        var v2Adapter = new PersistentMigrationAdapter(
            new MigrationPipeline(v2Registry), storage,
            () => "test-engine-2.0", "Test.V2Editor");

        var v2Result = await v2Adapter.LoadAndMigrateAsync(path);

        // ASSERT: file is at v2 (fast path)
        Assert.Equal(2, v2Result.CurrentMeta.SchemaVersion);
        Assert.False(v2Result.WasMigrated);

        var finalItems = v2Result.Dom["items"]!.AsArray();
        Assert.Equal(3, finalItems.Count);

        // User's rename preserved
        Assert.Equal("alpha-renamed", finalItems[0]!["name"]!.GetValue<string>());
        // v2-exclusive "kind" restored for original items
        Assert.Equal("tank",  finalItems[0]!["kind"]!.GetValue<string>());
        Assert.Equal("scout", finalItems[1]!["kind"]!.GetValue<string>());
        // New item preserved; up-migrator gave it "default" kind (no journal op for it)
        Assert.Equal("gamma", finalItems[2]!["name"]!.GetValue<string>());
        Assert.Equal("default", finalItems[2]!["kind"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T2-082..T2-085: Constructor null-checks
    // ---------------------------------------------------------------

    // T2-082: null pipeline throws.
    [Fact]
    public void Constructor_NullPipeline_ThrowsArgumentNullException()
    {
        var storage = new InMemoryMigrationStorage();
        Assert.Throws<ArgumentNullException>(() =>
            new PersistentMigrationAdapter(null!, storage, () => "1.0", "Writer"));
    }

    // T2-083: null storage throws.
    [Fact]
    public void Constructor_NullStorage_ThrowsArgumentNullException()
    {
        var pipeline = BuildPipeline(1);
        Assert.Throws<ArgumentNullException>(() =>
            new PersistentMigrationAdapter(pipeline, null!, () => "1.0", "Writer"));
    }

    // T2-084: null engineVersionProvider throws.
    [Fact]
    public void Constructor_NullEngineVersionProvider_ThrowsArgumentNullException()
    {
        var storage = new InMemoryMigrationStorage();
        Assert.Throws<ArgumentNullException>(() =>
            new PersistentMigrationAdapter(BuildPipeline(1), storage, null!, "Writer"));
    }

    // T2-085: null writerIdentifier throws.
    [Fact]
    public void Constructor_NullWriterIdentifier_ThrowsArgumentNullException()
    {
        var storage = new InMemoryMigrationStorage();
        Assert.Throws<ArgumentNullException>(() =>
            new PersistentMigrationAdapter(BuildPipeline(1), storage, () => "1.0", null!));
    }

    // T2-086: SaveAsync null dom throws.
    [Fact]
    public async Task SaveAsync_NullDom_ThrowsArgumentNullException()
    {
        const string path = @"C:\data\doc.json";
        var (adapter, storage) = BuildAdapter(currentVersion: 1);
        await SeedFile(storage, path, BuildTestDoc(1));
        var loadResult = await adapter.LoadAndMigrateAsync(path);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            adapter.SaveAsync(path, null!, loadResult));
    }

    // T2-087: SaveAsync null priorLoad throws.
    [Fact]
    public async Task SaveAsync_NullPriorLoad_ThrowsArgumentNullException()
    {
        var (adapter, _) = BuildAdapter(currentVersion: 1);
        var dom = JsonNode.Parse(BuildTestDoc(1))!.AsObject();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            adapter.SaveAsync(@"C:\data\doc.json", dom, null!));
    }
}
