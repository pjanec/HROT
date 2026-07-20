using System.Numerics;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core.Commands;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Slice 2a-3 — end-to-end headless coverage of the <see cref="GetSharedNode"/>/
/// <see cref="SetSharedNode"/> authoring path: placing the node via
/// <see cref="BlueprintCommandSink"/> (the same path the palette drag/drop and wire-drop
/// create-flows use), editing <c>VariableId</c>/<c>SharedTypeId</c> through
/// <see cref="GraphCommand.SetNodeProperty"/> (the same command an inspector widget would issue —
/// mirrors <c>BlueprintCommandSinkTests.CommandSink_SetProperty_UpdatesNode</c>), and finally a
/// full save→load round trip through <see cref="BlueprintJsonServices"/> proving both fields
/// survive serialization. All tests are headless (no ImGui).
/// </summary>
public sealed class SharedNodeCommandSinkAndPersistenceTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static (BlueprintAsset asset, Graph graph) MakeAssetWithGraph()
    {
        var asset = BlueprintAssetBuilder.Instance("SharedNodeAsset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { })
            .Build();
        return (asset, asset.Graphs[0]);
    }

    private static (BlueprintCommandSink sink, BlueprintGraphModel model) MakeSink(
        BlueprintAsset asset, Graph graph)
    {
        // Full bootstrap registry so kind "GetShared"/"SetShared" resolve through the real
        // palette descriptors (same registry the production editor wires up).
        var registry   = BlueprintEditorBootstrap.CreatePaletteRegistry();
        var model      = new BlueprintGraphModel(asset, graph, registry);
        var catalog    = new BlueprintNodeCatalog(registry);
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var editSvc    = new EditService { Context = new EditServiceContext(history, _ => { }) };
        var sink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editSvc, _ => { });
        return (sink, model);
    }

    // ── AddNode create-path (palette) ─────────────────────────────────────────

    [Fact]
    public void AddNode_GetShared_ProducesGetSharedNode_ViaPaletteRegistry()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var (sink, _)      = MakeSink(asset, graph);

        var result = sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey("GetShared"),
            new Vector2(0, 0),
            null));

        Assert.True(result.Success, result.Message);
        var node = graph.Nodes.OfType<GetSharedNode>().Single();
        Assert.Equal("", node.VariableId);
        Assert.Equal("", node.SharedTypeId);
    }

    [Fact]
    public void AddNode_SetShared_ProducesSetSharedNode_ViaPaletteRegistry()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var (sink, _)      = MakeSink(asset, graph);

        var result = sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey("SetShared"),
            new Vector2(0, 0),
            null));

        Assert.True(result.Success, result.Message);
        Assert.Single(graph.Nodes.OfType<SetSharedNode>());
    }

    [Fact]
    public void AddNode_GetShared_WithInitialProperties_BakesVariableIdAndSharedTypeId()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var (sink, _)      = MakeSink(asset, graph);

        var result = sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey("GetShared"),
            new Vector2(0, 0),
            new Dictionary<string, object?>
            {
                ["VariableId"]   = "RallyPoint",
                ["SharedTypeId"] = "global::Hrot.AI.Behaviors.SquadRallyState",
            }));

        Assert.True(result.Success, result.Message);
        var node = graph.Nodes.OfType<GetSharedNode>().Single();
        Assert.Equal("RallyPoint", node.VariableId);
        Assert.Equal("global::Hrot.AI.Behaviors.SquadRallyState", node.SharedTypeId);
    }

    // ── SetNodeProperty (post-placement config) ───────────────────────────────

    [Fact]
    public void SetNodeProperty_GetShared_UpdatesVariableIdAndSharedTypeId()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var node = new GetSharedNode { Id = Guid.NewGuid() };
        graph.Nodes.Add(node);
        var (sink, _) = MakeSink(asset, graph);

        var r1 = sink.Apply(new GraphCommand.SetNodeProperty(
            new NodeId(node.Id), "VariableId", "RallyPoint"));
        var r2 = sink.Apply(new GraphCommand.SetNodeProperty(
            new NodeId(node.Id), "SharedTypeId", "global::Hrot.AI.Behaviors.SquadRallyState"));

        Assert.True(r1.Success, r1.Message);
        Assert.True(r2.Success, r2.Message);
        Assert.Equal("RallyPoint", node.VariableId);
        Assert.Equal("global::Hrot.AI.Behaviors.SquadRallyState", node.SharedTypeId);
    }

    [Fact]
    public void SetNodeProperty_SetShared_UpdatesVariableIdAndSharedTypeId()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var node = new SetSharedNode { Id = Guid.NewGuid() };
        graph.Nodes.Add(node);
        var (sink, _) = MakeSink(asset, graph);

        sink.Apply(new GraphCommand.SetNodeProperty(new NodeId(node.Id), "VariableId", "Ammo"));
        sink.Apply(new GraphCommand.SetNodeProperty(
            new NodeId(node.Id), "SharedTypeId", "global::My.Namespace.AmmoState"));

        Assert.Equal("Ammo", node.VariableId);
        Assert.Equal("global::My.Namespace.AmmoState", node.SharedTypeId);
    }

    [Fact]
    public void SetNodeProperty_GetShared_MarksAssetDirty()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var node = new GetSharedNode { Id = Guid.NewGuid() };
        graph.Nodes.Add(node);

        var registry   = BlueprintEditorBootstrap.CreatePaletteRegistry();
        var model      = new BlueprintGraphModel(asset, graph, registry);
        var catalog    = new BlueprintNodeCatalog(registry);
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var dirtyLog   = new List<BlueprintAsset>();
        // SetNodeProperty routes dirty-marking through EditServiceContext.MarkDirty (not the
        // sink's own markDirty callback, which only fires for structural ops) — wire both to
        // the same log so this test observes the actual dirty path SetNodeProperty uses.
        var editSvc    = new EditService { Context = new EditServiceContext(history, a => dirtyLog.Add(a)) };
        var sink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editSvc, a => dirtyLog.Add(a));

        sink.Apply(new GraphCommand.SetNodeProperty(new NodeId(node.Id), "VariableId", "Slot"));

        Assert.Contains(asset, dirtyLog);
    }

    // ── Full round trip: create → configure → save → load ─────────────────────

    [Fact]
    public void RoundTrip_GetShared_CreateConfigureSaveLoad_FieldsSurvive()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var (sink, _)      = MakeSink(asset, graph);

        // 1. Create via the same palette create-path a designer would use.
        var addResult = sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()), new NodeKindKey("GetShared"), new Vector2(0, 0), null));
        Assert.True(addResult.Success, addResult.Message);
        var placed = graph.Nodes.OfType<GetSharedNode>().Single();

        // 2. Configure via the same command an inspector widget would issue.
        sink.Apply(new GraphCommand.SetNodeProperty(new NodeId(placed.Id), "VariableId", "RallyPoint"));
        sink.Apply(new GraphCommand.SetNodeProperty(
            new NodeId(placed.Id), "SharedTypeId", "global::Hrot.AI.Behaviors.SquadRallyState"));

        // 3. Save → load through the real blueprint JSON persistence boundary.
        var json    = BlueprintJsonServices.Serialize(asset);
        var reloaded = BlueprintJsonServices.Deserialize(json);

        Assert.NotNull(reloaded);
        var reloadedGraph = reloaded!.Graphs.Single(g => g.Id == graph.Id);
        var reloadedNode  = reloadedGraph.Nodes.OfType<GetSharedNode>().Single();

        Assert.Equal("RallyPoint", reloadedNode.VariableId);
        Assert.Equal("global::Hrot.AI.Behaviors.SquadRallyState", reloadedNode.SharedTypeId);
    }

    [Fact]
    public void RoundTrip_SetShared_CreateConfigureSaveLoad_FieldsSurvive()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var (sink, _)      = MakeSink(asset, graph);

        var addResult = sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()), new NodeKindKey("SetShared"), new Vector2(0, 0), null));
        Assert.True(addResult.Success, addResult.Message);
        var placed = graph.Nodes.OfType<SetSharedNode>().Single();

        sink.Apply(new GraphCommand.SetNodeProperty(new NodeId(placed.Id), "VariableId", "Ammo"));
        sink.Apply(new GraphCommand.SetNodeProperty(
            new NodeId(placed.Id), "SharedTypeId", "global::My.Namespace.AmmoState"));

        var json     = BlueprintJsonServices.Serialize(asset);
        var reloaded = BlueprintJsonServices.Deserialize(json);

        Assert.NotNull(reloaded);
        var reloadedGraph = reloaded!.Graphs.Single(g => g.Id == graph.Id);
        var reloadedNode  = reloadedGraph.Nodes.OfType<SetSharedNode>().Single();

        Assert.Equal("Ammo", reloadedNode.VariableId);
        Assert.Equal("global::My.Namespace.AmmoState", reloadedNode.SharedTypeId);
    }

    /// <summary>
    /// The node-drawer edit path (GetSharedNodeSession, the Details-panel session a designer
    /// actually drives) produces the identical persisted result as driving SetNodeProperty
    /// directly — proving the two authoring surfaces (palette config bake vs. post-placement
    /// drawer edit) agree.
    /// </summary>
    [Fact]
    public void RoundTrip_ViaNodeDrawerSession_FieldsSurviveSaveLoad()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var node = new GetSharedNode { Id = Guid.NewGuid() };
        graph.Nodes.Add(node);

        var dirtyLog = new List<BlueprintAsset>();
        var drawer   = new GetSharedNodeDrawer(new RecordingEditService(dirtyLog), new ReflectionSharedStructTypeProvider());
        var session  = (GetSharedNodeSession)drawer.CreateSession(node, asset);

        session.SetVariableIdForTest("RallyPoint");
        session.SetSharedTypeIdForTest("global::Hrot.AI.Behaviors.SquadRallyState");

        Assert.True(session.IsDirty);
        Assert.Contains(asset, dirtyLog);

        var json     = BlueprintJsonServices.Serialize(asset);
        var reloaded = BlueprintJsonServices.Deserialize(json);

        Assert.NotNull(reloaded);
        var reloadedNode = reloaded!.Graphs.Single(g => g.Id == graph.Id)
            .Nodes.OfType<GetSharedNode>().Single();
        Assert.Equal("RallyPoint", reloadedNode.VariableId);
        Assert.Equal("global::Hrot.AI.Behaviors.SquadRallyState", reloadedNode.SharedTypeId);
    }

    [Fact]
    public void ExpandFields_BakesPerFieldDecls_FromReflectedStruct_AndCollapseClears()
    {
        var (asset, graph) = MakeAssetWithGraph();
        var node = new SetSharedNode
        {
            Id = Guid.NewGuid(),
            SharedTypeId = typeof(Runtime.MultiPinShared).FullName!,   // resolvable top-level blittable struct
        };
        graph.Nodes.Add(node);

        var drawer  = new SetSharedNodeDrawer(new RecordingEditService(new List<BlueprintAsset>()), new ReflectionSharedStructTypeProvider());
        var session = (SetSharedNodeSession)drawer.CreateSession(node, asset);

        Assert.False(session.IsExpandedForTest());       // default = legacy whole-struct

        session.SetExpandFieldsForTest(true);            // opt-in multi-pin: reflect + bake fields
        Assert.True(session.IsExpandedForTest());
        Assert.NotNull(node.Fields);
        Assert.Equal(new[] { "A", "B", "C" }, node.Fields!.Select(f => f.Name).ToArray());
        Assert.All(node.Fields, f => Assert.Equal("System.Int32", f.TypeId));
        Assert.Equal(new[] { 0, 4, 8 }, node.Fields.Select(f => f.Offset).ToArray());

        session.SetExpandFieldsForTest(false);           // collapse → back to whole-struct
        Assert.False(session.IsExpandedForTest());
        Assert.Null(node.Fields);
    }

    private sealed class RecordingEditService : IEditService
    {
        private readonly List<BlueprintAsset> _log;
        public RecordingEditService(List<BlueprintAsset> log) => _log = log;
        public void MarkDirty(BlueprintAsset asset) => _log.Add(asset);
    }
}
