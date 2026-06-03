using System.IO;
using System.Text.Json.Nodes;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Catalog;
using Hrot.Blueprints.Editor.Host;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Tests for <see cref="SaveActiveBlueprintCommand"/> — MVE-BATCH-04.
/// All tests are fully headless (no ImGui).
/// </summary>
public sealed class SaveActiveBlueprintCommandTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static BlueprintAsset BuildMutatedAsset()
    {
        var nodeId = new Guid("aaaaaaaa-0001-0001-0001-000000000001");
        var varId  = new Guid("bbbbbbbb-0001-0001-0001-000000000001");

        return new BlueprintAsset
        {
            AssetId  = new Guid("cccccccc-0001-0001-0001-000000000001"),
            Name     = "SaveTest",
            Dispatch = BlueprintDispatchKind.Instance,
            Variables =
            [
                new VariableDecl
                {
                    Id   = varId,
                    Name = "Count",
                    Type = new BlueprintTypeRef { TypeId = "System.Int32" },
                },
            ],
            Graphs =
            [
                new Graph
                {
                    Id   = new Guid("dddddddd-0001-0001-0001-000000000001"),
                    Name = "EventGraph",
                    Kind = GraphKind.Event,
                    Nodes =
                    [
                        new FunctionCallNode
                        {
                            Id           = nodeId,
                            TargetTypeId = "System.Math",
                            MethodName   = "Abs",
                            IsPure       = true,
                            EditorMetadata = new NodeMetadata { X = 100f, Y = 200f },
                        },
                    ],
                    Links =
                    [
                        new Link
                        {
                            FromNodeId = nodeId,
                            FromPinId  = new Guid("eeeeeeee-0001-0001-0001-000000000001"),
                            ToNodeId   = new Guid("ffffffff-0001-0001-0001-000000000001"),
                            ToPinId    = new Guid("ffffffff-0002-0001-0001-000000000001"),
                        },
                    ],
                    EditorMetadata = new GraphMetadata { ViewportX = 10f, ViewportY = 20f },
                },
            ],
        };
    }

    private static string MakeTempPath() =>
        Path.Combine(Path.GetTempPath(), $"bcp04_{Guid.NewGuid():N}.bp.json");

    // ── TC-1: round-trip: mutations persist ───────────────────────────────────

    [Fact]
    public void Save_ThenReload_MutationsPersist()
    {
        var asset = BuildMutatedAsset();
        var path  = MakeTempPath();

        try
        {
            SaveActiveBlueprintCommand.Save(asset, path);

            // Reload from disk and assert mutations are present.
            var json     = File.ReadAllText(path);
            var reloaded = BlueprintJsonServices.Deserialize(json);

            Assert.NotNull(reloaded);

            // Variable persisted.
            Assert.Single(reloaded!.Variables);
            Assert.Equal("Count", reloaded.Variables[0].Name);

            // Graph persisted.
            Assert.Single(reloaded.Graphs);
            var g = reloaded.Graphs[0];
            Assert.Equal("EventGraph", g.Name);

            // Node persisted with props.
            Assert.Single(g.Nodes);
            var node = Assert.IsType<FunctionCallNode>(g.Nodes[0]);
            Assert.Equal("System.Math", node.TargetTypeId);
            Assert.Equal("Abs", node.MethodName);
            Assert.True(node.IsPure);

            // Node position (EditorMetadata) persisted.
            Assert.Equal(100f, node.EditorMetadata.X);
            Assert.Equal(200f, node.EditorMetadata.Y);

            // Link persisted.
            Assert.Single(g.Links);
            Assert.Equal(new Guid("eeeeeeee-0001-0001-0001-000000000001"), g.Links[0].FromPinId);

            // Graph viewport persisted.
            Assert.Equal(10f, g.EditorMetadata.ViewportX);
            Assert.Equal(20f, g.EditorMetadata.ViewportY);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── TC-2: projection-only: saved file has Pins:[] for every node ──────────

    [Fact]
    public void Save_NodesHaveProjectedPins_SavedFileHasPinsEmpty()
    {
        var asset = BuildMutatedAsset();

        // Inject in-memory projected pins (as the editor canvas would).
        var node = asset.Graphs[0].Nodes[0];
        node.Pins.Add(new Pin
        {
            Id        = Guid.NewGuid(),
            Name      = "ExecIn",
            Direction = "Input",
            IsExec    = true,
        });
        node.Pins.Add(new Pin
        {
            Id        = Guid.NewGuid(),
            Name      = "ExecOut",
            Direction = "Output",
            IsExec    = true,
        });
        Assert.Equal(2, node.Pins.Count); // verify pre-condition

        var path = MakeTempPath();
        try
        {
            SaveActiveBlueprintCommand.Save(asset, path);

            var savedJson = File.ReadAllText(path);
            var dom       = JsonNode.Parse(savedJson)!;

            // Every node in the saved JSON must have Pins as an empty array (or absent).
            var graphsNode = dom["Graphs"]?.AsArray();
            Assert.NotNull(graphsNode);
            foreach (var graphNode in graphsNode!)
            {
                var nodesNode = graphNode?["Nodes"]?.AsArray();
                if (nodesNode == null) continue;
                foreach (var n in nodesNode)
                {
                    var pinsNode = n?["Pins"];
                    if (pinsNode != null)
                        Assert.Empty(pinsNode.AsArray());
                }
            }

            // Verify by reloading: deserialized nodes should also have empty Pins.
            var reloaded = BlueprintJsonServices.Deserialize(savedJson);
            Assert.NotNull(reloaded);
            Assert.All(
                reloaded!.Graphs.SelectMany(g => g.Nodes),
                n => Assert.Empty(n.Pins));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── TC-3: Save does NOT mutate the live asset's pins ──────────────────────

    [Fact]
    public void Save_DoesNotMutateLiveAssetPins()
    {
        var asset = BuildMutatedAsset();
        var node  = asset.Graphs[0].Nodes[0];

        // Add projected pins to the live node.
        var pinA = new Pin { Id = Guid.NewGuid(), Name = "A", Direction = "Input" };
        var pinB = new Pin { Id = Guid.NewGuid(), Name = "B", Direction = "Output" };
        node.Pins.Add(pinA);
        node.Pins.Add(pinB);

        // Keep reference to original list to check identity.
        var originalPinsList = node.Pins;

        var path = MakeTempPath();
        try
        {
            SaveActiveBlueprintCommand.Save(asset, path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }

        // Live asset pins must be exactly as before (same list object, same contents).
        Assert.Same(originalPinsList, node.Pins);
        Assert.Equal(2, node.Pins.Count);
        Assert.Same(pinA, node.Pins[0]);
        Assert.Same(pinB, node.Pins[1]);
    }

    // ── TC-4: byte-stability: load fixture → Save → reload → Serialize → equal

    [Fact]
    public void Save_FixtureAsset_ByteStable()
    {
        // Load a known fixture (InstanceCounter — simple, has Pins:[]).
        var asset = TestData.LoadAsset(TestData.SampleAssets.InstanceCounter);

        var path = MakeTempPath();
        try
        {
            SaveActiveBlueprintCommand.Save(asset, path);

            // Reload from saved file.
            var savedJson = File.ReadAllText(path);
            var reloaded  = BlueprintJsonServices.Deserialize(savedJson);
            Assert.NotNull(reloaded);

            // Serialize again and compare.
            var reserialized = BlueprintJsonServices.Serialize(reloaded!);

            // Both serializations must be equal (modulo $meta which is always re-stamped
            // identically, so no special handling needed — they should be exactly equal).
            Assert.Equal(savedJson, reserialized);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── TC-5: SaveFromActiveDocument — no document open returns NoBlueprintOpen ─

    [Fact]
    public void SaveFromActiveDocument_NoDocumentOpen_ReturnsNoBlueprintOpen()
    {
        // Manager with no documents open → Active is null.
        var manager = new AiDocumentManager(_ => { });
        var tracker = new DirtyTracker();
        string? reported = null;

        var result = SaveActiveBlueprintCommand.SaveFromActiveDocument(
            manager, tracker, msg => reported = msg);

        Assert.Equal(SaveActiveBlueprintCommand.SaveStatus.NoBlueprintOpen, result.Status);
        Assert.NotNull(reported); // report callback was called
    }

    // ── TC-6: SaveFromActiveDocument — empty source path returns NoSourcePath ──

    [Fact]
    public void SaveFromActiveDocument_EmptySourcePath_ReturnsNoSourcePath()
    {
        var asset   = BuildMutatedAsset();
        var tracker = new DirtyTracker();

        // IEditableAsset with empty source path.
        var fileAsset = new StubEditableAsset(asset.AssetId, "SaveTest", sourcePath: "");
        var manager   = MakeManagerWithDocument(fileAsset, asset);

        var result = SaveActiveBlueprintCommand.SaveFromActiveDocument(manager, tracker);

        Assert.Equal(SaveActiveBlueprintCommand.SaveStatus.NoSourcePath, result.Status);
    }

    // ── TC-7: SaveFromActiveDocument — saves and marks document + tracker clean ─

    [Fact]
    public void SaveFromActiveDocument_ValidPath_SavesAndMarksBothClean()
    {
        var asset   = BuildMutatedAsset();
        var path    = MakeTempPath();
        var tracker = new DirtyTracker();
        tracker.MarkDirty(asset.AssetId);

        var fileAsset = new StubEditableAsset(asset.AssetId, "SaveTest", sourcePath: path);
        var manager   = MakeManagerWithDocument(fileAsset, asset);
        var doc       = manager.Active!;
        doc.MarkDirty();

        string? reported = null;
        try
        {
            var result = SaveActiveBlueprintCommand.SaveFromActiveDocument(
                manager, tracker, msg => reported = msg);

            Assert.Equal(SaveActiveBlueprintCommand.SaveStatus.Saved, result.Status);
            Assert.Equal(path, result.SavedPath);
            Assert.True(File.Exists(path), "File should have been written.");
            Assert.False(tracker.IsDirty(asset.AssetId), "DirtyTracker should be cleared after save.");
            Assert.False(doc.IsDirty, "AiDocument should be marked clean after save.");
            Assert.NotNull(reported);
            Assert.Contains(path, reported!); // status message contains the path
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── TC-8: Save writes pins-empty even when asset has many graphs/nodes ────

    [Fact]
    public void Save_MultipleGraphsWithPins_AllSavedWithEmptyPins()
    {
        var pinId = Guid.NewGuid();
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "MultiGraph",
            Dispatch = BlueprintDispatchKind.Library,
            Graphs   =
            [
                new Graph
                {
                    Id    = Guid.NewGuid(), Name = "G1", Kind = GraphKind.Function,
                    Nodes = [ new BranchNode { Id = Guid.NewGuid(), Pins = [ new Pin { Id = pinId, Name = "P1" } ] } ],
                },
                new Graph
                {
                    Id    = Guid.NewGuid(), Name = "G2", Kind = GraphKind.Event,
                    Nodes = [ new ReturnNode  { Id = Guid.NewGuid(), Pins = [ new Pin { Id = Guid.NewGuid(), Name = "P2" } ] } ],
                },
            ],
        };

        var path = MakeTempPath();
        try
        {
            SaveActiveBlueprintCommand.Save(asset, path);

            var savedJson = File.ReadAllText(path);
            var reloaded  = BlueprintJsonServices.Deserialize(savedJson);
            Assert.NotNull(reloaded);

            Assert.All(
                reloaded!.Graphs.SelectMany(g => g.Nodes),
                n => Assert.Empty(n.Pins));

            // Live asset nodes still have pins.
            Assert.All(
                asset.Graphs.SelectMany(g => g.Nodes),
                n => Assert.NotEmpty(n.Pins));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an <see cref="AiDocumentManager"/> that has one open Blueprint document.
    /// The document's <see cref="AiDocument.ViewState"/> is a minimal <see cref="AiCanvasContext"/>
    /// with <c>AssetRef</c> set to <paramref name="blueprintAsset"/> so the Save resolver
    /// can extract both the in-memory asset and the source path.
    /// </summary>
    private static AiDocumentManager MakeManagerWithDocument(
        IEditableAsset     fileAsset,
        BlueprintAsset     blueprintAsset)
    {
        var manager = new AiDocumentManager(_ => { });

        // Wire DocumentOpened to populate ViewState before Activate fires.
        manager.DocumentOpened += doc =>
        {
            // Build a minimal canvas context: the resolver only needs AssetRef.
            var ctx = new AiCanvasContext(
                view: MakeMinimalGraphView(blueprintAsset),
                kind: "Blueprint")
            {
                AssetRef = blueprintAsset,
            };
            doc.ViewState = ctx;
        };

        manager.Open(fileAsset);
        return manager;
    }

    /// <summary>
    /// Creates the minimal <c>GraphView</c> required by <see cref="AiCanvasContext"/>.
    /// Uses the same stub approach as <c>BcpBatch02BlueprintTests</c>.
    /// </summary>
    private static NodeEditor.Core.View.GraphView MakeMinimalGraphView(BlueprintAsset asset)
    {
        var graph      = asset.Graphs.Count > 0
                             ? asset.Graphs[0]
                             : new Hrot.Blueprints.Core.Assets.Graph();
        var model      = new Hrot.Blueprints.Editor.Host.BlueprintGraphModel(asset, graph);
        var typeSystem = new Hrot.Blueprints.Editor.Host.BlueprintTypeSystem(
                             Hrot.Blueprints.Editor.Host.NullPinDefaultValueEditorRegistry.Instance);
        var validator  = new Hrot.Blueprints.Editor.Host.BlueprintLinkValidator(model, typeSystem);
        var catalog    = new Hrot.Blueprints.Editor.Host.BlueprintNodeCatalog(
                             new Hrot.Blueprints.Editor.NodeDrawers.NodeKindRegistry());
        var sink       = new StubCommandSink();
        var host       = new StubEditorHostServices(catalog, typeSystem, validator, sink);
        return new NodeEditor.Core.View.GraphView(model, sink, validator, typeSystem, catalog, host);
    }

    /// <summary>Minimal <see cref="IEditableAsset"/> stub for test composition.</summary>
    private sealed class StubEditableAsset : IEditableAsset
    {
        public StubEditableAsset(Guid assetId, string name, string sourcePath)
        {
            AssetId        = assetId;
            Name           = name;
            SourceFilePath = sourcePath;
        }

        public Guid     AssetId        { get; }
        public string   Name           { get; }
        public AssetKind Kind          => AssetKind.Blueprint;
        public string   SourceFilePath { get; }
        public bool     IsDirty        => false;
        public bool     IsEditorOwned  => false;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    /// <summary>Minimal <see cref="NodeEditor.Core.Interfaces.IGraphCommandSink"/> stub.</summary>
    private sealed class StubCommandSink : NodeEditor.Core.Interfaces.IGraphCommandSink
    {
        public NodeEditor.Core.Interfaces.GraphCommandResult Apply(NodeEditor.Core.Commands.GraphCommand command)
            => new(true, null);
    }

    /// <summary>Minimal <see cref="NodeEditor.Core.Interfaces.IEditorHostServices"/> stub.</summary>
    private sealed class StubEditorHostServices : NodeEditor.Core.Interfaces.IEditorHostServices
    {
        public StubEditorHostServices(
            NodeEditor.Core.Interfaces.INodeCatalog      catalog,
            NodeEditor.Core.Interfaces.ITypeSystem       typeSystem,
            NodeEditor.Core.Interfaces.ILinkValidator    validator,
            NodeEditor.Core.Interfaces.IGraphCommandSink sink)
        {
            NodeCatalog   = catalog;
            TypeSystem    = typeSystem;
            LinkValidator = validator;
            CommandSink   = sink;
        }

        public NodeEditor.Core.Interfaces.INodeCatalog      NodeCatalog   { get; }
        public NodeEditor.Core.Interfaces.ITypeSystem        TypeSystem    { get; }
        public NodeEditor.Core.Interfaces.ILinkValidator     LinkValidator { get; }
        public NodeEditor.Core.Interfaces.IGraphCommandSink  CommandSink   { get; }
        public NodeEditor.Core.Interfaces.IPickerRegistry    Pickers       => null!;
        public NodeEditor.Core.Interfaces.IClipboard         Clipboard     => null!;
        public NodeEditor.Core.Interfaces.IIconProvider      Icons         => null!;
        public NodeEditor.Core.Interfaces.IDiagnosticsSink?  Diagnostics   => null;
        public NodeEditor.Core.Interfaces.IDebugSession?     Debug         => null;
        public NodeEditor.Core.Interfaces.IInputSource       Input         => null!;
        public NodeEditor.Core.Interfaces.IEditorTheme       Theme         => null!;
        public System.Collections.Generic.IReadOnlyList<NodeEditor.Core.Interfaces.ICustomCanvasRenderer>
            CustomCanvasRenderers => System.Array.Empty<NodeEditor.Core.Interfaces.ICustomCanvasRenderer>();
        public NodeEditor.Core.Interfaces.ICustomElementContextMenuProvider? CustomElementContextMenu => null;
    }
}
