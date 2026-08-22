using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Windows;
using Hrot.Editor.AiShared.Selection;
using EditorSelectionStore = Hrot.Editor.AiShared.Selection.EditorSelectionStore;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Core;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Headless tests for <see cref="FunctionCallNodeDrawer"/> and
/// <see cref="FunctionCallNodeSession"/> (BATCH-03D1).
/// No ImGui calls — all mutation logic is exercised through internal test hooks.
/// </summary>
public sealed class FunctionCallNodeDrawerTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static FunctionCallNode MakeNode() => new()
    {
        Id           = Guid.NewGuid(),
        TargetTypeId = "",
        MethodName   = "",
        IsPure       = false,
        TargetGraphId = "",
    };

    private static BlueprintAsset MakeAsset(params Graph[] graphs)
    {
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "TestBP" };
        foreach (var g in graphs) asset.Graphs.Add(g);
        return asset;
    }

    private static Graph MakeFunctionGraph(string name = "MyFunc")
        => new() { Id = Guid.NewGuid(), Kind = GraphKind.Function, Name = name };

    // ── FC-01: Handles ────────────────────────────────────────────────────────

    [Fact]
    public void Drawer_Handles_FunctionCallNode_True()
    {
        var drawer = new FunctionCallNodeDrawer(new SpyEditService());
        Assert.True(drawer.Handles(new FunctionCallNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Drawer_Handles_OtherNodeTypes_False()
    {
        var drawer = new FunctionCallNodeDrawer(new SpyEditService());
        Assert.False(drawer.Handles(new WhenNode          { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new ReadEqsResultNode { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new SpawnEqsSensorNode{ Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new BranchNode        { Id = Guid.NewGuid() }));
    }

    // ── FC-02: CreateSession ──────────────────────────────────────────────────

    [Fact]
    public void Drawer_CreateSession_ReturnsNonNull()
    {
        var drawer = new FunctionCallNodeDrawer(new SpyEditService());
        var node   = MakeNode();
        var asset  = MakeAsset();

        using var session = drawer.CreateSession(node, asset);

        Assert.NotNull(session);
    }

    [Fact]
    public void Drawer_CreateSession_InitiallyNotDirty()
    {
        var drawer = new FunctionCallNodeDrawer(new SpyEditService());
        using var session = drawer.CreateSession(MakeNode(), MakeAsset());

        Assert.False(session.IsDirty);
    }

    // ── FC-03: SelectFunctionGraphForTest ─────────────────────────────────────

    [Fact]
    public void Session_SelectFunctionGraphForTest_SetsTargetGraphId()
    {
        var graph  = MakeFunctionGraph();
        var asset  = MakeAsset(graph);
        var node   = MakeNode();
        var drawer = new FunctionCallNodeDrawer(new SpyEditService());

        var session = (FunctionCallNodeSession)drawer.CreateSession(node, asset);
        session.SelectFunctionGraphForTest(graph.Id);

        Assert.Equal(graph.Id.ToString(), node.TargetGraphId);
    }

    [Fact]
    public void Session_SelectFunctionGraphForTest_ClearsCLRFields()
    {
        var graph  = MakeFunctionGraph();
        var asset  = MakeAsset(graph);
        var node   = MakeNode();
        node.TargetTypeId = "SomeType";
        node.MethodName   = "SomeMethod";

        var drawer  = new FunctionCallNodeDrawer(new SpyEditService());
        var session = (FunctionCallNodeSession)drawer.CreateSession(node, asset);
        session.SelectFunctionGraphForTest(graph.Id);

        Assert.Equal("", node.TargetTypeId);
        Assert.Equal("", node.MethodName);
    }

    [Fact]
    public void Session_SelectFunctionGraphForTest_MarksDirty()
    {
        var graph  = MakeFunctionGraph();
        var asset  = MakeAsset(graph);
        var node   = MakeNode();
        var drawer = new FunctionCallNodeDrawer(new SpyEditService());

        var session = (FunctionCallNodeSession)drawer.CreateSession(node, asset);
        session.SelectFunctionGraphForTest(graph.Id);

        Assert.True(session.IsDirty);
    }

    // ── FC-04: SetClrTargetForTest ────────────────────────────────────────────

    [Fact]
    public void Session_SetClrTargetForTest_SetsCLRFields()
    {
        var asset  = MakeAsset();
        var node   = MakeNode();
        var drawer = new FunctionCallNodeDrawer(new SpyEditService());

        var session = (FunctionCallNodeSession)drawer.CreateSession(node, asset);
        session.SetClrTargetForTest("MyNamespace.MyType", "Execute", true);

        Assert.Equal("MyNamespace.MyType", node.TargetTypeId);
        Assert.Equal("Execute", node.MethodName);
        Assert.True(node.IsPure);
    }

    [Fact]
    public void Session_SetClrTargetForTest_ClearsTargetGraphId()
    {
        var graph  = MakeFunctionGraph();
        var asset  = MakeAsset(graph);
        var node   = MakeNode();
        node.TargetGraphId = graph.Id.ToString();  // pre-set to in-blueprint mode

        var drawer  = new FunctionCallNodeDrawer(new SpyEditService());
        var session = (FunctionCallNodeSession)drawer.CreateSession(node, asset);
        session.SetClrTargetForTest("T", "M", false);

        Assert.Equal("", node.TargetGraphId);
    }

    [Fact]
    public void Session_SetClrTargetForTest_MarksDirty()
    {
        var asset  = MakeAsset();
        var node   = MakeNode();
        var drawer = new FunctionCallNodeDrawer(new SpyEditService());

        var session = (FunctionCallNodeSession)drawer.CreateSession(node, asset);
        session.SetClrTargetForTest("T", "M", false);

        Assert.True(session.IsDirty);
    }

    // ── FC-05: MutualExclusivity ──────────────────────────────────────────────

    [Fact]
    public void Session_GraphPickThenClr_OnlyCLRFieldsSet()
    {
        var graph  = MakeFunctionGraph();
        var asset  = MakeAsset(graph);
        var node   = MakeNode();
        var drawer = new FunctionCallNodeDrawer(new SpyEditService());

        var session = (FunctionCallNodeSession)drawer.CreateSession(node, asset);
        session.SelectFunctionGraphForTest(graph.Id);   // sets TargetGraphId
        session.SetClrTargetForTest("T", "M", true);    // should clear TargetGraphId

        Assert.Equal("T", node.TargetTypeId);
        Assert.Equal("M", node.MethodName);
        Assert.True(node.IsPure);
        Assert.Equal("", node.TargetGraphId);
    }

    [Fact]
    public void Session_ClrThenGraphPick_OnlyGraphIdSet()
    {
        var graph  = MakeFunctionGraph();
        var asset  = MakeAsset(graph);
        var node   = MakeNode();
        var drawer = new FunctionCallNodeDrawer(new SpyEditService());

        var session = (FunctionCallNodeSession)drawer.CreateSession(node, asset);
        session.SetClrTargetForTest("T", "M", false);     // sets CLR fields
        session.SelectFunctionGraphForTest(graph.Id);      // should clear CLR fields

        Assert.Equal(graph.Id.ToString(), node.TargetGraphId);
        Assert.Equal("", node.TargetTypeId);
        Assert.Equal("", node.MethodName);
    }

    // ── FC-06: MarkDirty on IEditService ─────────────────────────────────────

    [Fact]
    public void Session_GraphPick_CallsMarkDirtyOnEditService()
    {
        var spy    = new SpyEditService();
        var graph  = MakeFunctionGraph();
        var asset  = MakeAsset(graph);
        var node   = MakeNode();
        var drawer = new FunctionCallNodeDrawer(spy);

        var session = (FunctionCallNodeSession)drawer.CreateSession(node, asset);
        session.SelectFunctionGraphForTest(graph.Id);

        Assert.Equal(1, spy.MarkDirtyCallCount);
        Assert.Same(asset, spy.LastMarkedAsset);
    }

    [Fact]
    public void Session_ClrSet_CallsMarkDirtyOnEditService()
    {
        var spy    = new SpyEditService();
        var asset  = MakeAsset();
        var node   = MakeNode();
        var drawer = new FunctionCallNodeDrawer(spy);

        var session = (FunctionCallNodeSession)drawer.CreateSession(node, asset);
        session.SetClrTargetForTest("T", "M", true);

        Assert.Equal(1, spy.MarkDirtyCallCount);
        Assert.Same(asset, spy.LastMarkedAsset);
    }

    [Fact]
    public void Session_TwoEdits_CallsMarkDirtyTwice()
    {
        var spy    = new SpyEditService();
        var graph  = MakeFunctionGraph();
        var asset  = MakeAsset(graph);
        var node   = MakeNode();
        var drawer = new FunctionCallNodeDrawer(spy);

        var session = (FunctionCallNodeSession)drawer.CreateSession(node, asset);
        session.SelectFunctionGraphForTest(graph.Id);
        session.SetClrTargetForTest("T", "M", false);

        Assert.Equal(2, spy.MarkDirtyCallCount);
    }

    // ── FC-07: ResetDirty ─────────────────────────────────────────────────────

    [Fact]
    public void Session_ResetDirty_ClearsDirtyFlag()
    {
        var graph  = MakeFunctionGraph();
        var asset  = MakeAsset(graph);
        var node   = MakeNode();
        var drawer = new FunctionCallNodeDrawer(new SpyEditService());

        var session = (FunctionCallNodeSession)drawer.CreateSession(node, asset);
        session.SelectFunctionGraphForTest(graph.Id);
        Assert.True(session.IsDirty);

        session.ResetDirty();

        Assert.False(session.IsDirty);
    }

    // ── FC-08: Registration in CreateNodeDrawerRegistry ──────────────────────

    [Fact]
    public void DrawerRegistry_Contains_FunctionCallNodeDrawer()
    {
        var registry = CreateTestDrawerRegistry();

        var drawer = registry.GetDrawerFor(new FunctionCallNode { Id = Guid.NewGuid() });

        Assert.NotNull(drawer);
        Assert.IsType<FunctionCallNodeDrawer>(drawer);
    }

    [Fact]
    public void DrawerRegistry_TryGet_FunctionCallNode_Succeeds()
    {
        var registry = CreateTestDrawerRegistry();

        Assert.True(registry.TryGet(typeof(FunctionCallNode), out var drawer));
        Assert.NotNull(drawer);
    }

    // ── FC-09: the Details node view resolves FunctionCallNodeDrawer ──────────

    /// <remarks>⭐ <c>S1</c> (<c>BP-399</c>): the pump moved from <c>BlueprintDetailsWindow</c> to
    /// <c>BlueprintNodeDetailsView</c> — 📄 §7.4, content EXTRACTED. ⚠ The claim is unchanged.</remarks>
    [Fact]
    public void DetailsNodeView_ResolveSession_ReturnsFunctionCallSession_WithCorrectDrawerKind()
    {
        // Build asset with a Function graph and a FunctionCallNode
        var funcGraph = MakeFunctionGraph("CalcDamage");
        var node      = MakeNode();
        var graphId   = Guid.NewGuid();
        var eventGraph = new Graph { Id = graphId, Kind = GraphKind.Event, Name = "EventGraph" };
        eventGraph.Nodes.Add(node);

        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "TestBP" };
        asset.Graphs.Add(funcGraph);
        asset.Graphs.Add(eventGraph);

        // Build registry with real FunctionCallNodeDrawer
        var registry = CreateTestDrawerRegistry();

        // Wire up the details node view
        var view = new Hrot.Blueprints.Editor.Windows.BlueprintNodeDetailsView(() => asset, registry);

        var context = new Hrot.Editor.AiShared.Shell.DetailsContext(
            Hrot.Editor.AiShared.Selection.SelectionOrigin.GraphCanvas,
            new Hrot.Editor.AiShared.Selection.IAssetSubSelection[]
                { new BlueprintNodeSelection(graphId, node.Id) },
            Array.Empty<Fdp.Core.Entity>(),
            new FakeEditableAsset(asset.AssetId),
            "Blueprint",
            Hrot.Editor.AiShared.Variables.VariableRunState.Planning);

        var session = view.ResolveSession(context);

        Assert.NotNull(session);
        Assert.Equal(typeof(FunctionCallNodeDrawer), view.ResolvedDrawerKind);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static BlueprintNodeDrawerRegistry CreateTestDrawerRegistry()
    {
        var channelCatalog    = BuiltInChannelCommandCatalog.Instance;
        var eventCatalog      = BuiltInEngineEventCatalog.Instance;
        var editService       = new SpyEditService();
        var predicateCompiler = new TestPredicateCompiler();
        var eqsTemplates      = new EqsTemplateRegistry();

        return BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
            channelCatalog, eventCatalog, editService, predicateCompiler, eqsTemplates);
    }

    // ── Test stubs ────────────────────────────────────────────────────────────

    private sealed class SpyEditService : IEditService
    {
        public int MarkDirtyCallCount { get; private set; }
        public BlueprintAsset? LastMarkedAsset { get; private set; }

        public void MarkDirty(BlueprintAsset asset)
        {
            MarkDirtyCallCount++;
            LastMarkedAsset = asset;
        }
    
        /// <summary>BP-11: every recorded (label, apply, undo) triple, in order.</summary>
        public List<(string Label, Action Apply, Action Undo)> Recorded { get; } = new();

        public int StructureChangedCallCount { get; private set; }

        /// <summary>
        /// Mirrors the real service: recording performs the edit, so a drawer that routes through
        /// here mutates exactly once — and the captured <c>Undo</c> lets a headless test reverse it.
        /// </summary>
        public void RecordPropertyEdit(BlueprintAsset asset, string description, Action apply, Action undo)
        {
            Recorded.Add((description, apply, undo));
            apply();
            MarkDirty(asset);
        }

        public void NotifyStructureChanged(BlueprintAsset asset) => StructureChangedCallCount++;
}

    private sealed class TestPredicateCompiler : IPredicateCompiler
    {
        public Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto predicate)
            => (_, _) => true;

        public Func<EntityRepository, Entity, bool> CompileEntityPredicate(SearchPredicateDto predicate)
            => (_, _) => true;

        public IReadOnlyList<Type> ExtractMandatoryComponents(SearchPredicateDto predicate)
            => Array.Empty<Type>();
    }

    private sealed class FakeEditableAsset : Hrot.Editor.AiShared.IEditableAsset
    {
        public Guid   AssetId        { get; }
        public string Name           => "";
        public Hrot.Editor.AiShared.AssetKind Kind => Hrot.Editor.AiShared.AssetKind.Blueprint;
        public string SourceFilePath => "";
        public bool   IsDirty        => false;
        public bool   IsEditorOwned  => false;
        public event System.Action? Changed;
        public FakeEditableAsset(Guid id) { AssetId = id; }
    }
}
