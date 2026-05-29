using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario;
using Hrot.Common.Scenario.Migrations;
using Hrot.Common.Scenario.Migrations.Migrators.Scenario;

namespace Hrot.Common.Tests.Scenario.Migrations;

/// <summary>
/// Phase 3 tests for the first migrator pair: V1ToV2_EntityInfo_AddTags and
/// V2ToV1_EntityInfo_RemoveTags (JM-P3-001 through JM-P3-005).
/// </summary>
public sealed class Phase3MigratorTests
{
    // ── Test helpers ─────────────────────────────────────────────────────────

    private static MigrationContext MakeContext() =>
        new MigrationContext(HrotDocumentTypes.Scenario, null);

    private static JsonObject MakeRoot(params (string id, JsonObject entity)[] entities)
    {
        var entitiesObj = new JsonObject();
        foreach (var (id, entity) in entities)
            entitiesObj[id] = entity;
        return new JsonObject
        {
            ["$meta"] = new JsonObject
            {
                ["docType"] = HrotDocumentTypes.Scenario,
                ["schemaVersion"] = 1
            },
            ["entities"] = entitiesObj
        };
    }

    private static JsonObject MakeEntityWith(JsonObject entityInfo) =>
        new JsonObject { ["EntityInfo"] = entityInfo };

    private static JsonObject MakeEntityInfoV1(string name, string forceId) =>
        new JsonObject { ["Name"] = name, ["ForceId"] = forceId };

    private static MigrationServices BuildServices(Action<MigrationRegistry> registerFormats) =>
        MigrationBootstrap.Build(
            registerFormats,
            new InMemoryMigrationStorage(),
            () => "test-1.0",
            "Hrot.Common.Tests");

    private static string FindWorkspaceRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IOS-IG-SimHost.sln")))
            dir = dir.Parent;
        if (dir == null)
            throw new InvalidOperationException("Cannot locate workspace root.");
        return dir.FullName;
    }

    // ── Group 1: V1ToV2 up-migrator unit tests ────────────────────────────

    // Test 1
    [Fact]
    public void V1ToV2_AddTags_EntityWithEntityInfo_AddsEmptyTagsArray()
    {
        var migrator = new V1ToV2_EntityInfo_AddTags();
        var ctx = MakeContext();
        var root = MakeRoot(("guid-1", MakeEntityWith(MakeEntityInfoV1("Alpha-1", "Friend"))));

        migrator.Apply(root, ctx);

        var entityInfo = root["entities"]!["guid-1"]!["EntityInfo"]!.AsObject();
        var tags = entityInfo["Tags"];
        Assert.NotNull(tags);
        Assert.IsType<JsonArray>(tags);
        Assert.Equal(0, tags.AsArray().Count);
    }

    // Test 2
    [Fact]
    public void V1ToV2_AddTags_EntityWithoutEntityInfo_IsNotModified()
    {
        var migrator = new V1ToV2_EntityInfo_AddTags();
        var ctx = MakeContext();
        var entity = new JsonObject
        {
            ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(0.0, 0.0, 0.0) }
        };
        var root = MakeRoot(("guid-no-info", entity));

        migrator.Apply(root, ctx);

        var entityAfter = root["entities"]!["guid-no-info"]!.AsObject();
        Assert.False(entityAfter.ContainsKey("EntityInfo"));
        Assert.Equal(1, entityAfter.Count); // only SimTransform
    }

    // Test 3
    [Fact]
    public void V1ToV2_AddTags_EntityAlreadyHasTags_IsIdempotent()
    {
        var migrator = new V1ToV2_EntityInfo_AddTags();
        var ctx = MakeContext();
        var info = new JsonObject
        {
            ["Name"] = "Alpha-1",
            ["ForceId"] = "Friend",
            ["Tags"] = new JsonArray(JsonValue.Create("existing")!)
        };
        var root = MakeRoot(("guid-1", MakeEntityWith(info)));

        migrator.Apply(root, ctx);

        var tagsAfter = root["entities"]!["guid-1"]!["EntityInfo"]!["Tags"]!.AsArray();
        Assert.Equal(1, tagsAfter.Count);
        Assert.Equal("existing", tagsAfter[0]!.GetValue<string>());
    }

    // Test 4
    [Fact]
    public void V1ToV2_AddTags_MultipleEntities_AllGetTags()
    {
        var migrator = new V1ToV2_EntityInfo_AddTags();
        var ctx = MakeContext();
        var root = MakeRoot(
            ("guid-1", MakeEntityWith(MakeEntityInfoV1("Alpha-1", "Friend"))),
            ("guid-2", MakeEntityWith(MakeEntityInfoV1("Bravo-1", "Hostile"))),
            ("guid-3", MakeEntityWith(MakeEntityInfoV1("Charlie-1", "Neutral"))),
            ("guid-no-info", new JsonObject { ["SimTransform"] = new JsonObject() })
        );

        migrator.Apply(root, ctx);

        Assert.NotNull(root["entities"]!["guid-1"]!["EntityInfo"]!["Tags"]);
        Assert.NotNull(root["entities"]!["guid-2"]!["EntityInfo"]!["Tags"]);
        Assert.NotNull(root["entities"]!["guid-3"]!["EntityInfo"]!["Tags"]);
        Assert.False(root["entities"]!["guid-no-info"]!.AsObject().ContainsKey("EntityInfo"));
    }

    // Test 5
    [Fact]
    public void V1ToV2_AddTags_ReportNoteIncludesCount()
    {
        var migrator = new V1ToV2_EntityInfo_AddTags();
        var ctx = MakeContext();
        var root = MakeRoot(
            ("guid-1", MakeEntityWith(MakeEntityInfoV1("Alpha-1", "Friend"))),
            ("guid-2", MakeEntityWith(MakeEntityInfoV1("Bravo-1", "Hostile")))
        );

        migrator.Apply(root, ctx);

        Assert.NotEmpty(ctx.Report.Notes);
        Assert.Contains(ctx.Report.Notes, note => note.Contains("2"));
    }

    // Test 6
    [Fact]
    public void V1ToV2_AddTags_DocTypeIsScenario()
    {
        var migrator = new V1ToV2_EntityInfo_AddTags();
        Assert.Equal(HrotDocumentTypes.Scenario, migrator.DocType);
    }

    // Test 7
    [Fact]
    public void V1ToV2_AddTags_FromVersion1_ToVersion2()
    {
        var migrator = new V1ToV2_EntityInfo_AddTags();
        Assert.Equal(1, migrator.FromVersion);
        Assert.Equal(2, migrator.ToVersion);
    }

    // ── Group 1: V2ToV1 down-migrator unit tests ──────────────────────────

    // Test 8
    [Fact]
    public void V2ToV1_RemoveTags_EntityWithTags_RemovesTags()
    {
        var migrator = new V2ToV1_EntityInfo_RemoveTags();
        var ctx = MakeContext();
        var info = new JsonObject
        {
            ["Name"] = "Alpha-1",
            ["ForceId"] = "Friend",
            ["Tags"] = new JsonArray(JsonValue.Create("recon")!)
        };
        var root = MakeRoot(("guid-1", MakeEntityWith(info)));

        migrator.Apply(root, ctx);

        var entityInfo = root["entities"]!["guid-1"]!["EntityInfo"]!.AsObject();
        Assert.False(entityInfo.ContainsKey("Tags"));
    }

    // Test 9
    [Fact]
    public void V2ToV1_RemoveTags_EntityWithoutEntityInfo_IsNotModified()
    {
        var migrator = new V2ToV1_EntityInfo_RemoveTags();
        var ctx = MakeContext();
        var entity = new JsonObject
        {
            ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(0.0, 0.0, 0.0) }
        };
        var root = MakeRoot(("guid-no-info", entity));

        migrator.Apply(root, ctx);

        var entityAfter = root["entities"]!["guid-no-info"]!.AsObject();
        Assert.False(entityAfter.ContainsKey("EntityInfo"));
        Assert.Equal(1, entityAfter.Count); // only SimTransform
    }

    // Test 10
    [Fact]
    public void V2ToV1_RemoveTags_EntityWithoutTags_IsIdempotent()
    {
        var migrator = new V2ToV1_EntityInfo_RemoveTags();
        var ctx = MakeContext();
        var root = MakeRoot(("guid-1", MakeEntityWith(MakeEntityInfoV1("Alpha-1", "Friend"))));

        migrator.Apply(root, ctx);

        var entityInfo = root["entities"]!["guid-1"]!["EntityInfo"]!.AsObject();
        Assert.True(entityInfo.ContainsKey("Name"));
        Assert.True(entityInfo.ContainsKey("ForceId"));
        Assert.Equal(2, entityInfo.Count);
    }

    // Test 11
    [Fact]
    public void V2ToV1_RemoveTags_MultipleEntities_AllLoseTags()
    {
        var migrator = new V2ToV1_EntityInfo_RemoveTags();
        var ctx = MakeContext();
        var root = MakeRoot(
            ("guid-1", MakeEntityWith(new JsonObject { ["Name"] = "A", ["ForceId"] = "Friend", ["Tags"] = new JsonArray() })),
            ("guid-2", MakeEntityWith(new JsonObject { ["Name"] = "B", ["ForceId"] = "Hostile", ["Tags"] = new JsonArray(JsonValue.Create("x")!) })),
            ("guid-3", MakeEntityWith(new JsonObject { ["Name"] = "C", ["ForceId"] = "Neutral", ["Tags"] = new JsonArray() })),
            ("guid-no-info", new JsonObject { ["SimTransform"] = new JsonObject() })
        );

        migrator.Apply(root, ctx);

        Assert.False(root["entities"]!["guid-1"]!["EntityInfo"]!.AsObject().ContainsKey("Tags"));
        Assert.False(root["entities"]!["guid-2"]!["EntityInfo"]!.AsObject().ContainsKey("Tags"));
        Assert.False(root["entities"]!["guid-3"]!["EntityInfo"]!.AsObject().ContainsKey("Tags"));
        Assert.False(root["entities"]!["guid-no-info"]!.AsObject().ContainsKey("EntityInfo"));
    }

    // Test 12
    [Fact]
    public void V2ToV1_RemoveTags_DocTypeIsScenario_FromVersion2_ToVersion1()
    {
        var migrator = new V2ToV1_EntityInfo_RemoveTags();
        Assert.Equal(HrotDocumentTypes.Scenario, migrator.DocType);
        Assert.Equal(2, migrator.FromVersion);
        Assert.Equal(1, migrator.ToVersion);
    }

    // ── Group 2: Registry validation tests ───────────────────────────────

    // Test 13
    [Fact]
    public void ScenarioMigrationModule_CurrentVersion_Is2()
    {
        Assert.Equal(2, ScenarioMigrationModule.CurrentVersion);
    }

    // Test 14
    [Fact]
    public void ScenarioMigrationModule_RegisterAll_CanMigrateV1ToV2()
    {
        MigrationServices services = BuildServices(ScenarioMigrationModule.RegisterAll);

        Assert.True(services.Registry.CanMigrate(HrotDocumentTypes.Scenario, 1, 2));
    }

    // Test 15
    [Fact]
    public void ScenarioMigrationModule_RegisterAll_CanMigrateV2ToV1()
    {
        MigrationServices services = BuildServices(ScenarioMigrationModule.RegisterAll);

        Assert.True(services.Registry.CanMigrate(HrotDocumentTypes.Scenario, 2, 1));
    }

    // ── Group 3: Bootstrap integration test ──────────────────────────────

    // Test 16
    [Fact]
    public async Task ReadOnlyAdapter_LoadV1ScenarioCorpus_ProducesV2Dom()
    {
        string workspaceRoot = FindWorkspaceRoot();
        string path = Path.Combine(
            workspaceRoot,
            "test-data", "scenario-corpus", "multi-version", "v1_complete", "scenario.json");

        Assert.True(File.Exists(path), $"Corpus file not found: {path}");

        MigrationServices services = HrotMigrationBootstrap.BuildSimHostCgf("test");
        var outcome = await services.ReadOnly.LoadAndMigrateAsync(path);

        JsonObject dom = outcome.AsJsonObject();

        Assert.Equal(2, dom["$meta"]!["schemaVersion"]!.GetValue<int>());

        var entityInfo1 = dom["entities"]!
            ["aaaaaaaa-0001-0000-0000-000000000001"]!
            ["EntityInfo"]!;
        Assert.IsType<JsonArray>(entityInfo1["Tags"]);
    }

    // ── Group 4: Corpus round-trip tests ─────────────────────────────────

    // Test 17
    [Fact]
    public void V1CorpusFile_MigratedThroughPipeline_MatchesV2CorpusFile()
    {
        string workspaceRoot = FindWorkspaceRoot();
        string v1Path = Path.Combine(
            workspaceRoot, "test-data", "scenario-corpus", "multi-version", "v1_complete", "scenario.json");
        string v2Path = Path.Combine(
            workspaceRoot, "test-data", "scenario-corpus", "multi-version", "v2_complete", "scenario.json");

        Assert.True(File.Exists(v1Path), $"v1 corpus file not found: {v1Path}");
        Assert.True(File.Exists(v2Path), $"v2 corpus file not found: {v2Path}");

        MigrationServices services = BuildServices(ScenarioMigrationModule.RegisterAll);

        JsonObject v1Dom = JsonNode.Parse(File.ReadAllText(v1Path))!.AsObject();
        services.Pipeline.MigrateTo(v1Dom, 2);

        JsonObject v2Dom = JsonNode.Parse(File.ReadAllText(v2Path))!.AsObject();

        // Strip $meta.engineVersion from both (set at runtime by the engine, not present in corpus).
        // Since our hand-crafted corpus files don't include engineVersion, the DOMs should match.
        string migratedJson = NormalizeForComparison(v1Dom);
        string expectedJson = NormalizeForComparison(v2Dom);

        Assert.Equal(expectedJson, migratedJson);
    }

    // Test 18
    [Fact]
    public void V2CorpusFile_DownMigratedThroughPipeline_LosesTagsField()
    {
        string workspaceRoot = FindWorkspaceRoot();
        string v2Path = Path.Combine(
            workspaceRoot, "test-data", "scenario-corpus", "multi-version", "v2_complete", "scenario.json");

        Assert.True(File.Exists(v2Path), $"v2 corpus file not found: {v2Path}");

        MigrationServices services = BuildServices(ScenarioMigrationModule.RegisterAll);

        JsonObject v2Dom = JsonNode.Parse(File.ReadAllText(v2Path))!.AsObject();
        services.Pipeline.MigrateTo(v2Dom, 1);

        Assert.Equal(1, v2Dom["$meta"]!["schemaVersion"]!.GetValue<int>());

        var entities = v2Dom["entities"]!.AsObject();
        foreach (var kvp in entities)
        {
            if (kvp.Value is not JsonObject entity)
                continue;
            if (entity["EntityInfo"] is not JsonObject info)
                continue;
            Assert.False(info.ContainsKey("Tags"),
                $"Entity '{kvp.Key}' still has Tags field after down-migration.");
        }
    }

    // Test 19
    [Fact]
    public void V1ToV2_Then_V2ToV1_EntityInfoName_SurvivesRoundTrip()
    {
        // Arrange: v1 entity with EntityInfo.Name that the user "edited"
        var root = MakeRoot(("aaa", MakeEntityWith(MakeEntityInfoV1("Commander-Alpha", "Friend"))));
        var ctx1 = MakeContext();
        var ctx2 = MakeContext();

        // Act: up-migrate v1 -> v2 (adds Tags: [])
        new V1ToV2_EntityInfo_AddTags().Apply(root, ctx1);

        // Act: down-migrate v2 -> v1 (removes Tags)
        new V2ToV1_EntityInfo_RemoveTags().Apply(root, ctx2);

        // Assert: user's Name edit survived the round-trip
        var entities = root["entities"]!.AsObject();
        var entityInfo = entities["aaa"]!.AsObject()["EntityInfo"]!.AsObject();
        Assert.Equal("Commander-Alpha", entityInfo["Name"]!.GetValue<string>());
        Assert.Null(entityInfo["Tags"]);
    }

    // Test 20
    [Fact]
    public void V1MinimalEntity_MigratedThroughPipeline_MatchesV2MinimalEntity()
    {
        string workspaceRoot = FindWorkspaceRoot();
        string v1Path = Path.Combine(
            workspaceRoot, "test-data", "scenario-corpus", "multi-version", "v1_minimal-entity", "scenario.json");
        string v2Path = Path.Combine(
            workspaceRoot, "test-data", "scenario-corpus", "multi-version", "v2_minimal-entity", "scenario.json");

        Assert.True(File.Exists(v1Path), $"v1 corpus file not found: {v1Path}");
        Assert.True(File.Exists(v2Path), $"v2 corpus file not found: {v2Path}");

        MigrationServices services = BuildServices(ScenarioMigrationModule.RegisterAll);

        JsonObject v1Dom = JsonNode.Parse(File.ReadAllText(v1Path))!.AsObject();
        services.Pipeline.MigrateTo(v1Dom, 2);

        JsonObject v2Dom = JsonNode.Parse(File.ReadAllText(v2Path))!.AsObject();

        string migratedJson = NormalizeForComparison(v1Dom);
        string expectedJson = NormalizeForComparison(v2Dom);

        Assert.Equal(expectedJson, migratedJson);
    }

    // Test 21
    [Fact]
    public void V1EmptyEntities_MigratedThroughPipeline_MatchesV2EmptyEntities()
    {
        string workspaceRoot = FindWorkspaceRoot();
        string v1Path = Path.Combine(
            workspaceRoot, "test-data", "scenario-corpus", "multi-version", "v1_empty-entities", "scenario.json");
        string v2Path = Path.Combine(
            workspaceRoot, "test-data", "scenario-corpus", "multi-version", "v2_empty-entities", "scenario.json");

        Assert.True(File.Exists(v1Path), $"v1 corpus file not found: {v1Path}");
        Assert.True(File.Exists(v2Path), $"v2 corpus file not found: {v2Path}");

        MigrationServices services = BuildServices(ScenarioMigrationModule.RegisterAll);

        JsonObject v1Dom = JsonNode.Parse(File.ReadAllText(v1Path))!.AsObject();
        services.Pipeline.MigrateTo(v1Dom, 2);

        JsonObject v2Dom = JsonNode.Parse(File.ReadAllText(v2Path))!.AsObject();

        string migratedJson = NormalizeForComparison(v1Dom);
        string expectedJson = NormalizeForComparison(v2Dom);

        Assert.Equal(expectedJson, migratedJson);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Serializes a DOM to a canonical JSON string for comparison.
    /// Removes $meta.engineVersion, $meta.createdBy, and $meta.createdUtc
    /// since those are set at runtime and may not be present in corpus files.
    /// </summary>
    private static string NormalizeForComparison(JsonObject dom)
    {
        // Deep-clone to avoid mutating the original.
        var clone = JsonNode.Parse(dom.ToJsonString())!.AsObject();

        if (clone["$meta"] is JsonObject meta)
        {
            meta.Remove("engineVersion");
            meta.Remove("createdBy");
            meta.Remove("createdUtc");
        }

        return clone.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
