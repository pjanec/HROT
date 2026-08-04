using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Windows;
using Hrot.Editor.AiShared.Selection;
using EditorSelectionStore = Hrot.Editor.AiShared.Selection.EditorSelectionStore;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Behavioral tests for <see cref="BlueprintDetailsWindow"/> (AIE-048).
/// All tests are headless — no ImGui calls; only the projection logic is exercised.
/// </summary>
public sealed class BlueprintDetailsWindowTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="BlueprintNodeDrawerRegistry"/> with three stub drawers
    /// covering <see cref="WhenNode"/>, <see cref="ReadEqsResultNode"/>, and a
    /// generic <see cref="FunctionCallNode"/> as a stand-in for "any other node".
    /// </summary>
    private static BlueprintNodeDrawerRegistry MakeRegistry()
    {
        var registry = new BlueprintNodeDrawerRegistry();
        registry.Register(typeof(WhenNode),          new StubDrawer<WhenNode>());
        registry.Register(typeof(ReadEqsResultNode), new StubDrawer<ReadEqsResultNode>());
        registry.Register(typeof(SpawnEqsSensorNode),new StubDrawer<SpawnEqsSensorNode>());
        return registry;
    }

    private static (BlueprintDetailsWindow window, EditorSelectionStore store)
        MakeWindow(BlueprintAsset? asset = null)
    {
        var store    = new EditorSelectionStore();
        var registry = MakeRegistry();
        var window   = new BlueprintDetailsWindow(store, registry);
        if (asset != null) window.Retarget(asset);
        return (window, store);
    }

    private static BlueprintAsset MakeAsset()
        => new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "TestBP" };

    // ── SC1: WhenNode selection resolves to WhenNodeDrawer kind ───────────────

    [Fact]
    public void BlueprintDetails_SelectedNode_ResolvesWhenNodeDrawer()
    {
        var asset     = MakeAsset();
        var graphId   = Guid.NewGuid();
        var nodeId    = Guid.NewGuid();
        var whenNode  = new WhenNode { Id = nodeId };
        var graph     = new Graph { Id = graphId, Name = "EventGraph" };
        graph.Nodes.Add(whenNode);
        asset.Graphs.Add(graph);

        var (window, store) = MakeWindow(asset);

        // Set active asset + sub-selection.
        store.ActiveAsset          = new FakeEditableAsset(asset.AssetId);
        store.ActiveSubSelection   = new BlueprintNodeSelection(graphId, nodeId);

        var session = window.ResolveSession();

        Assert.NotNull(session);
        Assert.Equal(typeof(StubDrawer<WhenNode>), window.ResolvedDrawerKind);
    }

    // ── SC2: ReadEqsResultNode resolves to its drawer ─────────────────────────

    [Fact]
    public void BlueprintDetails_SelectedNode_ResolvesReadEqsDrawer()
    {
        var asset   = MakeAsset();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var node    = new ReadEqsResultNode { Id = nodeId };
        var graph   = new Graph { Id = graphId, Name = "EventGraph" };
        graph.Nodes.Add(node);
        asset.Graphs.Add(graph);

        var (window, store) = MakeWindow(asset);
        store.ActiveAsset        = new FakeEditableAsset(asset.AssetId);
        store.ActiveSubSelection = new BlueprintNodeSelection(graphId, nodeId);

        var session = window.ResolveSession();

        Assert.NotNull(session);
        Assert.Equal(typeof(StubDrawer<ReadEqsResultNode>), window.ResolvedDrawerKind);
    }

    // ── SC3: SpawnEqsSensorNode resolves to its drawer ────────────────────────

    [Fact]
    public void BlueprintDetails_SelectedNode_ResolvesSpawnEqsDrawer()
    {
        var asset   = MakeAsset();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var node    = new SpawnEqsSensorNode { Id = nodeId };
        var graph   = new Graph { Id = graphId, Name = "EventGraph" };
        graph.Nodes.Add(node);
        asset.Graphs.Add(graph);

        var (window, store) = MakeWindow(asset);
        store.ActiveAsset        = new FakeEditableAsset(asset.AssetId);
        store.ActiveSubSelection = new BlueprintNodeSelection(graphId, nodeId);

        var session = window.ResolveSession();

        Assert.NotNull(session);
        Assert.Equal(typeof(StubDrawer<SpawnEqsSensorNode>), window.ResolvedDrawerKind);
    }

    // ── SC4: unregistered node type → no drawer, null session ─────────────────

    [Fact]
    public void BlueprintDetails_UnregisteredNodeType_ReturnsNullSession()
    {
        var asset   = MakeAsset();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        // FunctionCallNode is NOT registered in MakeRegistry().
        var node  = new FunctionCallNode { Id = nodeId };
        var graph = new Graph { Id = graphId, Name = "EventGraph" };
        graph.Nodes.Add(node);
        asset.Graphs.Add(graph);

        var (window, store) = MakeWindow(asset);
        store.ActiveAsset        = new FakeEditableAsset(asset.AssetId);
        store.ActiveSubSelection = new BlueprintNodeSelection(graphId, nodeId);

        var session = window.ResolveSession();

        Assert.Null(session);
        Assert.Null(window.ResolvedDrawerKind);
    }

    // ── SC5: no selection → null session ─────────────────────────────────────

    [Fact]
    public void BlueprintDetails_NoSelection_ReturnsNullSession()
    {
        var (window, _) = MakeWindow(MakeAsset());

        var session = window.ResolveSession();

        Assert.Null(session);
    }

    // ── SC6: retarget clears session ──────────────────────────────────────────

    [Fact]
    public void BlueprintDetails_Retarget_ClearsSession()
    {
        var asset1  = MakeAsset();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var node    = new WhenNode { Id = nodeId };
        var graph   = new Graph { Id = graphId };
        graph.Nodes.Add(node);
        asset1.Graphs.Add(graph);

        var (window, store) = MakeWindow(asset1);
        store.ActiveAsset        = new FakeEditableAsset(asset1.AssetId);
        store.ActiveSubSelection = new BlueprintNodeSelection(graphId, nodeId);

        // Establish a session.
        var session1 = window.ResolveSession();
        Assert.NotNull(session1);

        // Retarget to a different asset — session must be cleared.
        var asset2 = MakeAsset();
        window.Retarget(asset2);

        // Same sub-selection still present but asset changed — node not found → null.
        var session2 = window.ResolveSession();
        Assert.Null(session2);
    }

    // ── SC7: session is cached while selection stays same ─────────────────────

    [Fact]
    public void BlueprintDetails_SameSelection_ReturnsCachedSession()
    {
        var asset   = MakeAsset();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var node    = new WhenNode { Id = nodeId };
        var graph   = new Graph { Id = graphId };
        graph.Nodes.Add(node);
        asset.Graphs.Add(graph);

        var (window, store) = MakeWindow(asset);
        store.ActiveAsset        = new FakeEditableAsset(asset.AssetId);
        store.ActiveSubSelection = new BlueprintNodeSelection(graphId, nodeId);

        var session1 = window.ResolveSession();
        var session2 = window.ResolveSession();

        Assert.Same(session1, session2);
    }

    // ── BF-BATCH-TESTASSET: ChannelCommand drawer diagnostic ─────────────────

    /// <summary>
    /// BF-TA-01 (drawer diagnostic): Given a <see cref="BlueprintDetailsWindow"/> backed by the
    /// real registry from <see cref="BlueprintEditorBootstrap.CreateNodeDrawerRegistry"/>,
    /// selecting a <see cref="ChannelCommandNode"/> in the asset graph must resolve a NON-NULL
    /// session whose drawer is <see cref="ChannelCommandNodeDrawer"/>.
    ///
    /// If this test PASSES, the live "Details: No node selected" is a selection / wrong-node-id
    /// issue, NOT a drawer registration bug.
    /// </summary>
    [Fact]
    public void BlueprintDetails_ChannelCommandNode_ResolvesChannelCommandDrawer()
    {
        // Arrange — real drawer registry from bootstrap.
        var channelCatalog    = BuiltInChannelCommandCatalog.Instance;
        var eventCatalog      = BuiltInEngineEventCatalog.Instance;
        var editService       = new NullEditService();
        var predicateCompiler = new NullPredicateCompiler();
        var eqsTemplates      = new EqsTemplateRegistry();
        var registry = BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
            channelCatalog, eventCatalog, editService, predicateCompiler, eqsTemplates);

        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var node    = new ChannelCommandNode
        {
            Id          = nodeId,
            ChannelType = "LocomotionChannel",
            ActionId    = "MoveTo",
        };
        var graph = new Graph { Id = graphId, Name = "Main" };
        graph.Nodes.Add(node);

        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "TestBP" };
        asset.Graphs.Add(graph);

        var store  = new EditorSelectionStore();
        var window = new BlueprintDetailsWindow(store, registry);
        window.Retarget(asset);
        store.ActiveAsset        = new FakeEditableAsset(asset.AssetId);
        store.ActiveSubSelection = new BlueprintNodeSelection(graphId, nodeId);

        // Act
        var session = window.ResolveSession();

        // Assert — session is non-null and backed by ChannelCommandNodeDrawer.
        Assert.NotNull(session);
        Assert.Equal(typeof(ChannelCommandNodeDrawer), window.ResolvedDrawerKind);
    }

    // ── inner fakes ───────────────────────────────────────────────────────────

    /// <summary>
    /// Stub drawer that handles exactly <typeparamref name="T"/> and returns a
    /// <see cref="StubSession"/>.
    /// </summary>
    private sealed class StubDrawer<T> : IBlueprintNodeDrawer where T : Node
    {
        public bool Handles(Node node) => node is T;

        public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
            => new StubSession();
    }

    private sealed class StubSession : INodeEditSession
    {
        public bool IsDirty => false;
        public void Draw() { }
        public void ResetDirty() { }
        public void Dispose() { }
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

    // ── stubs for BF-TA-01 ────────────────────────────────────────────────────

    private sealed class NullEditService : IEditService
    {
        public void MarkDirty(BlueprintAsset asset) { }
    
        /// <summary>
        /// BP-11: no undo stack here, but recording still performs the edit and marks dirty —
        /// the same two observable effects the real EditService has.
        /// </summary>
        public void RecordPropertyEdit(BlueprintAsset asset, string description, Action apply, Action undo)
        {
            apply();
            MarkDirty(asset);
        }

        public void NotifyStructureChanged(BlueprintAsset asset) { }
}

    private sealed class NullPredicateCompiler : Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler
    {
        public Func<Fdp.Core.EntityRepository, Fdp.Core.Entity, bool> CompileComponentPredicate(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto predicate) => (_, _) => true;

        public Func<Fdp.Core.EntityRepository, Fdp.Core.Entity, bool> CompileEntityPredicate(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto predicate) => (_, _) => true;

        public IReadOnlyList<Type> ExtractMandatoryComponents(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto predicate) => Array.Empty<Type>();
    }
}
