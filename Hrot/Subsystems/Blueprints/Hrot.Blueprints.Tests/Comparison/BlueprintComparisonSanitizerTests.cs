using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hrot.Blueprints.Editor.Comparison;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;
using Xunit;

namespace Hrot.Blueprints.Tests.Comparison;

public sealed class BlueprintComparisonSanitizerTests
{
    // ---- Fake implementations ----

    private sealed class FakeAsset : IEditableAsset
    {
        public Guid AssetId { get; init; }
        public string Name { get; init; } = string.Empty;
        public AssetKind Kind { get; init; }
        public string SourceFilePath { get; init; } = string.Empty;
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
        public event Action? Changed { add { } remove { } }
    }

    private sealed class FakeCatalog : IAssetCatalog
    {
        private readonly Dictionary<Guid, IEditableAsset> _assets = new();

        public FakeCatalog(params IEditableAsset[] assets)
        {
            foreach (var a in assets)
                _assets[a.AssetId] = a;
        }

        public IReadOnlyList<IEditableAsset> All => _assets.Values.ToList();
        public IEditableAsset? FindByAssetId(Guid assetId) =>
            _assets.TryGetValue(assetId, out var a) ? a : null;
        public IEditableAsset? FindByName(string name) =>
            _assets.Values.FirstOrDefault(a => a.Name == name);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) =>
            Array.Empty<IEditableAsset>();
        public event Action? Changed { add { } remove { } }
    }

    private sealed class FakeMigrationAdapter : IComparisonMigrationAdapter
    {
        private readonly bool _didMigrate;

        public FakeMigrationAdapter(bool didMigrate = false) => _didMigrate = didMigrate;

        public string Adapt(string rawJson, out bool didMigrate)
        {
            didMigrate = _didMigrate;
            return rawJson;
        }
    }

    private sealed class FakeMetaSanitizer : IMetaEnvelopeSanitizer
    {
        public string Sanitize(string metaEnvelopeJson) => metaEnvelopeJson;
    }

    // ---- Helpers ----

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Comparison", "Fixtures", fileName);

    private static BlueprintComparisonSanitizer MakeSanitizer(
        IAssetCatalog? catalog = null,
        IComparisonMigrationAdapter? migrationAdapter = null)
    {
        return new BlueprintComparisonSanitizer(
            migrationAdapter ?? new FakeMigrationAdapter(),
            new FakeMetaSanitizer(),
            catalog ?? new FakeCatalog());
    }

    private static string WriteTempBlueprint(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"bp_test_{Guid.NewGuid():N}.bp.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static SanitizationResult RunOnJson(string json,
        IAssetCatalog? catalog = null,
        IComparisonMigrationAdapter? migrationAdapter = null)
    {
        string path = WriteTempBlueprint(json);
        try
        {
            return MakeSanitizer(catalog, migrationAdapter)
                .Sanitize(new AssetExportRequest(path, null, AssetKind.Blueprint));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static SanitizationResult RunOnFixture(string fixtureName,
        IAssetCatalog? catalog = null,
        IComparisonMigrationAdapter? migrationAdapter = null)
    {
        return MakeSanitizer(catalog, migrationAdapter)
            .Sanitize(new AssetExportRequest(FixturePath(fixtureName), null, AssetKind.Blueprint));
    }

    private static JsonObject ParseOutput(string sanitizedText)
    {
        var node = JsonNode.Parse(sanitizedText);
        Assert.IsType<JsonObject>(node);
        return (JsonObject)node!;
    }

    private static JsonObject? GetFirstNode(JsonObject root)
    {
        var graphs = root["Graphs"] as JsonArray;
        if (graphs == null || graphs.Count == 0) return null;
        var graph = graphs[0] as JsonObject;
        var nodes = graph?["Nodes"] as JsonArray;
        if (nodes == null || nodes.Count == 0) return null;
        return nodes[0] as JsonObject;
    }

    private static JsonObject? GetFirstGraph(JsonObject root)
    {
        var graphs = root["Graphs"] as JsonArray;
        if (graphs == null || graphs.Count == 0) return null;
        return graphs[0] as JsonObject;
    }

    // ---- Tests ----

    [Fact]
    public void Sanitize_NodeComment_IsHoistedToTopLevelNodeProperty()
    {
        const string json = """
            {
              "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" },
              "AssetId": "aaaaaaaa-0001-0000-0000-000000000001",
              "Name": "TestAsset",
              "Dispatch": "AiPrimitive",
              "Graphs": [
                {
                  "Id": "bbbbbbbb-0001-0000-0000-000000000001",
                  "Name": "Execute",
                  "Kind": "Function",
                  "Inputs": [], "Outputs": [],
                  "Nodes": [
                    {
                      "kind": "FunctionCall",
                      "Id": "cccccccc-0001-0000-0000-000000000001",
                      "Pins": [],
                      "EditorMetadata": {
                        "X": 320,
                        "Y": 180,
                        "Comment": "writes the debug message"
                      }
                    }
                  ],
                  "Links": [],
                  "EditorMetadata": {}
                }
              ],
              "EditorMetadata": {}
            }
            """;

        var result = RunOnJson(json);
        var root = ParseOutput(result.SanitizedText);
        var node = GetFirstNode(root);

        Assert.NotNull(node);
        // Comment hoisted to top-level node property.
        Assert.Equal("writes the debug message", node!["Comment"]?.GetValue<string>());
        // EditorMetadata removed from node.
        Assert.Null(node["EditorMetadata"]);
    }

    [Fact]
    public void Sanitize_CanvasComments_AreHoistedToGraphLevelWithTextOnly()
    {
        const string json = """
            {
              "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" },
              "AssetId": "aaaaaaaa-0002-0000-0000-000000000001",
              "Name": "TestAsset",
              "Dispatch": "AiPrimitive",
              "Graphs": [
                {
                  "Id": "bbbbbbbb-0002-0000-0000-000000000001",
                  "Name": "Execute",
                  "Kind": "Function",
                  "Inputs": [], "Outputs": [],
                  "Nodes": [],
                  "Links": [],
                  "EditorMetadata": {
                    "CanvasComments": [
                      { "Text": "Main flow", "X": 100, "Y": -50 }
                    ]
                  }
                }
              ],
              "EditorMetadata": {}
            }
            """;

        var result = RunOnJson(json);
        var root = ParseOutput(result.SanitizedText);
        var graph = GetFirstGraph(root);

        Assert.NotNull(graph);
        var canvasComments = graph!["_canvasComments"] as JsonArray;
        Assert.NotNull(canvasComments);
        Assert.Single(canvasComments!);

        var entry = canvasComments[0] as JsonObject;
        Assert.NotNull(entry);
        Assert.Equal("Main flow", entry!["Text"]?.GetValue<string>());
        // X and Y must not be in the hoisted entry.
        Assert.Null(entry["X"]);
        Assert.Null(entry["Y"]);

        // EditorMetadata must be absent from graph.
        Assert.Null(graph["EditorMetadata"]);
    }

    [Fact]
    public void Sanitize_NodePositionXY_IsStripped()
    {
        const string json = """
            {
              "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" },
              "AssetId": "aaaaaaaa-0003-0000-0000-000000000001",
              "Name": "TestAsset",
              "Dispatch": "AiPrimitive",
              "Graphs": [
                {
                  "Id": "bbbbbbbb-0003-0000-0000-000000000001",
                  "Name": "Execute",
                  "Kind": "Function",
                  "Inputs": [], "Outputs": [],
                  "Nodes": [
                    {
                      "kind": "Return",
                      "Id": "cccccccc-0003-0000-0000-000000000001",
                      "Pins": [],
                      "EditorMetadata": { "X": 320, "Y": 180 }
                    }
                  ],
                  "Links": [],
                  "EditorMetadata": {}
                }
              ],
              "EditorMetadata": {}
            }
            """;

        var result = RunOnJson(json);
        // Output must not contain X or Y at node level.
        Assert.DoesNotContain("\"X\"", result.SanitizedText);
        Assert.DoesNotContain("\"Y\"", result.SanitizedText);
        var node = GetFirstNode(ParseOutput(result.SanitizedText));
        Assert.Null(node?["EditorMetadata"]);
    }

    [Fact]
    public void Sanitize_GraphViewport_IsStripped()
    {
        const string json = """
            {
              "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" },
              "AssetId": "aaaaaaaa-0004-0000-0000-000000000001",
              "Name": "TestAsset",
              "Dispatch": "AiPrimitive",
              "Graphs": [
                {
                  "Id": "bbbbbbbb-0004-0000-0000-000000000001",
                  "Name": "Execute",
                  "Kind": "Function",
                  "Inputs": [], "Outputs": [],
                  "Nodes": [],
                  "Links": [],
                  "EditorMetadata": {
                    "Viewport": { "Pan": [0, 0], "Zoom": 1.0 }
                  }
                }
              ],
              "EditorMetadata": {}
            }
            """;

        var result = RunOnJson(json);
        Assert.DoesNotContain("Viewport", result.SanitizedText);
        var graph = GetFirstGraph(ParseOutput(result.SanitizedText));
        Assert.Null(graph?["EditorMetadata"]);
    }

    [Fact]
    public void Sanitize_NodeId_IsPreserved()
    {
        const string json = """
            {
              "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" },
              "AssetId": "aaaaaaaa-0005-0000-0000-000000000001",
              "Name": "TestAsset",
              "Dispatch": "AiPrimitive",
              "Graphs": [
                {
                  "Id": "bbbbbbbb-0005-0000-0000-000000000001",
                  "Name": "Execute",
                  "Kind": "Function",
                  "Inputs": [], "Outputs": [],
                  "Nodes": [
                    {
                      "kind": "Return",
                      "Id": "cccccccc-0001-0000-0000-000000000001",
                      "Pins": [],
                      "EditorMetadata": {}
                    }
                  ],
                  "Links": [],
                  "EditorMetadata": {}
                }
              ],
              "EditorMetadata": {}
            }
            """;

        var result = RunOnJson(json);
        var node = GetFirstNode(ParseOutput(result.SanitizedText));
        Assert.NotNull(node);
        Assert.Equal("cccccccc-0001-0000-0000-000000000001", node!["Id"]?.GetValue<string>());
    }

    [Fact]
    public void Sanitize_CallPeerBlueprint_AddsTargetName_WhenCatalogHit()
    {
        var peerAsset = new FakeAsset
        {
            AssetId = new Guid("11111111-0000-0000-0000-000000000099"),
            Name    = "PeerAsset",
            Kind    = AssetKind.Blueprint,
        };
        var catalog = new FakeCatalog(peerAsset);

        var result = RunOnFixture("with_peer_call.bp.json", catalog);
        var root = ParseOutput(result.SanitizedText);
        var node = GetFirstNode(root);

        Assert.NotNull(node);
        Assert.Equal("PeerAsset (Blueprint)", node!["_targetName"]?.GetValue<string>());
    }

    [Fact]
    public void Sanitize_CallPeerBlueprint_AddsMissMessage_WhenCatalogMiss()
    {
        var result = RunOnFixture("with_peer_call.bp.json", new FakeCatalog());
        var root = ParseOutput(result.SanitizedText);
        var node = GetFirstNode(root);

        Assert.NotNull(node);
        Assert.Equal("(asset not found in catalog)", node!["_targetName"]?.GetValue<string>());
    }

    [Fact]
    public void Sanitize_OutputIsAlphabeticallySorted()
    {
        // Provide root keys in non-alphabetical order.
        const string json = """
            {
              "Name": "TestAsset",
              "Graphs": [],
              "AssetId": "aaaaaaaa-0008-0000-0000-000000000001",
              "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" },
              "Dispatch": "AiPrimitive",
              "EditorMetadata": {}
            }
            """;

        var result = RunOnJson(json);

        // Parse the output and verify that keys are alphabetically ordered.
        var root = ParseOutput(result.SanitizedText);
        var keys = root.Select(kv => kv.Key).ToList();
        var sorted = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, keys);
    }

    [Fact]
    public void Sanitize_RunTenTimes_ProducesByteIdenticalOutput()
    {
        string fixturePath = FixturePath("with_editor_metadata.bp.json");
        var sanitizer = MakeSanitizer();
        var request = new AssetExportRequest(fixturePath, null, AssetKind.Blueprint);

        string reference = sanitizer.Sanitize(request).SanitizedText;

        for (int i = 0; i < 9; i++)
        {
            string run = sanitizer.Sanitize(request).SanitizedText;
            Assert.Equal(reference, run);
        }
    }

    [Fact]
    public void Sanitize_ShuffledInput_SameOutputAsCanonicalInput()
    {
        // Read the simple_node fixture (canonical ordering).
        string canonicalPath = FixturePath("simple_node.bp.json");
        var sanitizer = MakeSanitizer();

        string canonicalOutput = sanitizer
            .Sanitize(new AssetExportRequest(canonicalPath, null, AssetKind.Blueprint))
            .SanitizedText;

        // Build a version with root-level keys in reverse-alphabetical order.
        string shuffledJson = BuildReverseOrderJson(File.ReadAllText(canonicalPath));
        string shuffledPath = WriteTempBlueprint(shuffledJson);
        try
        {
            string shuffledOutput = sanitizer
                .Sanitize(new AssetExportRequest(shuffledPath, null, AssetKind.Blueprint))
                .SanitizedText;

            Assert.Equal(canonicalOutput, shuffledOutput);
        }
        finally
        {
            File.Delete(shuffledPath);
        }
    }

    private static string BuildReverseOrderJson(string originalJson)
    {
        var root = JsonNode.Parse(originalJson) as JsonObject;
        if (root == null) return originalJson;

        var reversed = new JsonObject();
        foreach (var kv in root.OrderByDescending(kv => kv.Key, StringComparer.Ordinal))
            reversed[kv.Key] = kv.Value?.DeepClone();

        return reversed.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    [Fact]
    public void Sanitize_MissingFile_ReturnsWarning_NeverThrows()
    {
        string nonExistentPath = Path.Combine(Path.GetTempPath(), $"does_not_exist_{Guid.NewGuid():N}.bp.json");
        var sanitizer = MakeSanitizer();
        var request = new AssetExportRequest(nonExistentPath, null, AssetKind.Blueprint);

        SanitizationResult result = sanitizer.Sanitize(request);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Sanitize_WithNoOpMigrationAdapter_NoMigrationNotice()
    {
        var adapter = new NoOpComparisonMigrationAdapter();
        var sanitizer = new BlueprintComparisonSanitizer(adapter, new FakeMetaSanitizer(), new FakeCatalog());
        string path = FixturePath("simple_node.bp.json");

        var result = sanitizer.Sanitize(new AssetExportRequest(path, null, AssetKind.Blueprint));

        Assert.Null(result.Metadata.MigrationNotice);
    }

    [Fact]
    public void Sanitize_WithFakeMigrationAdapter_MigrationNoticePopulated()
    {
        var adapter = new FakeMigrationAdapter(didMigrate: true);
        var sanitizer = new BlueprintComparisonSanitizer(adapter, new FakeMetaSanitizer(), new FakeCatalog());
        string path = FixturePath("simple_node.bp.json");

        var result = sanitizer.Sanitize(new AssetExportRequest(path, null, AssetKind.Blueprint));

        Assert.NotNull(result.Metadata.MigrationNotice);
        Assert.Contains("migrated", result.Metadata.MigrationNotice!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- C-32: DeepNested Blueprint fixture ---------------------------------

    [Fact]
    public void DeepNested_Blueprint_SanitizesAllGraphsInOrder()
    {
        // A Blueprint with a root graph and two inner graphs; each inner graph has 2 nodes.
        const string json = """
            {
              "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" },
              "AssetId": "ddddeeee-0001-0000-0000-000000000001",
              "Name": "DeepNestedBlueprint",
              "Dispatch": "AiPrimitive",
              "Graphs": [
                {
                  "Id": "11111111-0001-0000-0000-000000000001",
                  "Name": "Execute",
                  "Kind": "Function",
                  "Inputs": [], "Outputs": [],
                  "Nodes": [
                    {
                      "kind": "FunctionCall",
                      "Id": "aaaaaaaa-0001-0000-0000-000000000001",
                      "FunctionRef": "InnerGraph1.Execute",
                      "Pins": [],
                      "EditorMetadata": { "X": 100, "Y": 100, "Comment": "delegates to inner graph 1" }
                    },
                    {
                      "kind": "FunctionCall",
                      "Id": "bbbbbbbb-0001-0000-0000-000000000001",
                      "FunctionRef": "InnerGraph2.Execute",
                      "Pins": [],
                      "EditorMetadata": { "X": 300, "Y": 100, "Comment": "delegates to inner graph 2" }
                    }
                  ],
                  "Links": [], "EditorMetadata": {}
                },
                {
                  "Id": "22222222-0001-0000-0000-000000000001",
                  "Name": "InnerGraph1",
                  "Kind": "Function",
                  "Inputs": [], "Outputs": [],
                  "Nodes": [
                    {
                      "kind": "Return",
                      "Id": "cccccccc-0001-0000-0000-000000000001",
                      "Pins": [],
                      "EditorMetadata": { "X": 200, "Y": 150, "Comment": "inner graph 1 node A" }
                    },
                    {
                      "kind": "FunctionCall",
                      "Id": "dddddddd-0001-0000-0000-000000000001",
                      "FunctionRef": "SomeAction",
                      "Pins": [],
                      "EditorMetadata": { "X": 400, "Y": 150, "Comment": "inner graph 1 node B" }
                    }
                  ],
                  "Links": [], "EditorMetadata": {}
                },
                {
                  "Id": "33333333-0001-0000-0000-000000000001",
                  "Name": "InnerGraph2",
                  "Kind": "Function",
                  "Inputs": [], "Outputs": [],
                  "Nodes": [
                    {
                      "kind": "Return",
                      "Id": "eeeeeeee-0001-0000-0000-000000000001",
                      "Pins": [],
                      "EditorMetadata": { "X": 200, "Y": 250, "Comment": "inner graph 2 node A" }
                    },
                    {
                      "kind": "FunctionCall",
                      "Id": "ffffffff-0001-0000-0000-000000000001",
                      "FunctionRef": "AnotherAction",
                      "Pins": [],
                      "EditorMetadata": { "X": 400, "Y": 250, "Comment": "inner graph 2 node B" }
                    }
                  ],
                  "Links": [], "EditorMetadata": {}
                }
              ],
              "EditorMetadata": {}
            }
            """;

        var sanitizer = MakeSanitizer();
        var result1 = RunOnJson(json, catalog: null, migrationAdapter: null);

        // All node IDs from all graphs must appear in the sanitized output.
        Assert.Contains("aaaaaaaa-0001-0000-0000-000000000001", result1.SanitizedText);
        Assert.Contains("bbbbbbbb-0001-0000-0000-000000000001", result1.SanitizedText);
        Assert.Contains("cccccccc-0001-0000-0000-000000000001", result1.SanitizedText);
        Assert.Contains("dddddddd-0001-0000-0000-000000000001", result1.SanitizedText);
        Assert.Contains("eeeeeeee-0001-0000-0000-000000000001", result1.SanitizedText);
        Assert.Contains("ffffffff-0001-0000-0000-000000000001", result1.SanitizedText);

        // Determinism: second run produces identical output.
        var result2 = RunOnJson(json, catalog: null, migrationAdapter: null);
        Assert.Equal(result1.SanitizedText, result2.SanitizedText);
    }
}
