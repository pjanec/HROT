using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Windows;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Behavioral tests for <see cref="BlueprintNodeDetailsView"/> (AIE-048).
/// All tests are headless — no ImGui calls; only the projection logic is exercised.
///
/// <para>⭐⭐ <b><c>S1</c> (<c>BP-399</c>, <c>2026-08-22</c>) — PORTED from
/// <c>BlueprintDetailsWindowTests</c>, unchanged in substance.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.4: the node arm's content was <b>EXTRACTED</b>
/// to a Details view and <c>BlueprintDetailsWindow</c> deleted. ⇒ ⭐ every scenario below still asserts
/// the SAME projection — a selection resolves a drawer and a session — through the view.</para>
///
/// <para>⚠ <b>Two mechanical differences, both consequences of the extraction:</b>
/// <list type="number">
///   <item>⭐ <b>The selection arrives in a <see cref="DetailsContext"/></b>, not by writing
///   <c>store.ActiveSubSelection</c> — 📌 §2: <i>"only the workspace builds a context"</i>. ⭐ The store
///   is gone from this file entirely.</item>
///   <item>⭐ <b>The asset is PULLED through a <c>Func</c></b>, so <c>SC6</c>'s <i>"Retarget clears the
///   session"</i> is now <i>"a DIFFERENT asset clears the session"</i> — ⚠ the same claim, with nothing
///   for a caller to forget to call.</item>
/// </list></para>
/// </summary>
public sealed class BlueprintNodeDetailsViewTests
{
    // ── the context, in the shape the shell hands one to a view ───────────────

    /// <summary>⭐ A context with exactly one node selected — what the descriptor's predicate admits.</summary>
    private static DetailsContext NodeSelected(Guid graphId, Guid nodeId, Guid assetId)
        => new(SelectionOrigin.GraphCanvas,
               new IAssetSubSelection[] { new BlueprintNodeSelection(graphId, nodeId) },
               Array.Empty<Fdp.Core.Entity>(),
               new FakeEditableAsset(assetId),
               "Blueprint",
               Hrot.Editor.AiShared.Variables.VariableRunState.Planning);

    /// <summary>⭐ Nothing selected.</summary>
    private static DetailsContext NothingSelected() => DetailsContext.Empty("Blueprint");

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

    /// <summary>⭐ The view over a MUTABLE asset holder, so <c>SC6</c> can swap the document the way
    /// <c>ActiveChanged</c> does — ⛔ without a <c>Retarget</c> anyone must remember to call.</summary>
    private sealed class AssetHolder
    {
        public BlueprintAsset? Asset;
        public BlueprintAsset? Get() => Asset;
    }

    private static (BlueprintNodeDetailsView view, AssetHolder assets)
        MakeView(BlueprintAsset? asset = null)
    {
        var assets = new AssetHolder { Asset = asset };
        var view   = new BlueprintNodeDetailsView(assets.Get, MakeRegistry());
        return (view, assets);
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

        var (view, _) = MakeView(asset);

        var session = view.ResolveSession(NodeSelected(graphId, nodeId, asset.AssetId));

        Assert.NotNull(session);
        Assert.Equal(typeof(StubDrawer<WhenNode>), view.ResolvedDrawerKind);
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

        var (view, _) = MakeView(asset);

        var session = view.ResolveSession(NodeSelected(graphId, nodeId, asset.AssetId));

        Assert.NotNull(session);
        Assert.Equal(typeof(StubDrawer<ReadEqsResultNode>), view.ResolvedDrawerKind);
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

        var (view, _) = MakeView(asset);

        var session = view.ResolveSession(NodeSelected(graphId, nodeId, asset.AssetId));

        Assert.NotNull(session);
        Assert.Equal(typeof(StubDrawer<SpawnEqsSensorNode>), view.ResolvedDrawerKind);
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

        var (view, _) = MakeView(asset);

        var session = view.ResolveSession(NodeSelected(graphId, nodeId, asset.AssetId));

        Assert.Null(session);
        Assert.Null(view.ResolvedDrawerKind);
    }

    // ── SC5: no selection → null session ─────────────────────────────────────

    [Fact]
    public void BlueprintDetails_NoSelection_ReturnsNullSession()
    {
        var (view, _) = MakeView(MakeAsset());

        var session = view.ResolveSession(NothingSelected());

        Assert.Null(session);
    }

    // ── SC6: a DIFFERENT asset clears the session ─────────────────────────────

    /// <remarks>⭐ <c>S1</c>: was <c>BlueprintDetails_Retarget_ClearsSession</c>. ⚠ Same claim — a
    /// document switch must not leave a session pointing into the old asset — ⛔ but there is no
    /// <c>Retarget</c> to forget: the view compares the asset it resolved against.</remarks>
    [Fact]
    public void BlueprintNodeDetails_ADifferentAsset_ClearsSession()
    {
        var asset1  = MakeAsset();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var node    = new WhenNode { Id = nodeId };
        var graph   = new Graph { Id = graphId };
        graph.Nodes.Add(node);
        asset1.Graphs.Add(graph);

        var (view, assets) = MakeView(asset1);
        var context = NodeSelected(graphId, nodeId, asset1.AssetId);

        // Establish a session.
        var session1 = view.ResolveSession(context);
        Assert.NotNull(session1);

        // The document switches — the session must be cleared.
        assets.Asset = MakeAsset();

        // Same sub-selection still present but asset changed — node not found → null.
        var session2 = view.ResolveSession(context);
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

        var (view, _) = MakeView(asset);
        var context = NodeSelected(graphId, nodeId, asset.AssetId);

        var session1 = view.ResolveSession(context);
        var session2 = view.ResolveSession(context);

        Assert.Same(session1, session2);
    }

    // ── BF-BATCH-TESTASSET: ChannelCommand drawer diagnostic ─────────────────

    /// <summary>
    /// BF-TA-01 (drawer diagnostic): Given a <see cref="BlueprintNodeDetailsView"/> backed by the
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

        var view = new BlueprintNodeDetailsView(() => asset, registry);

        // Act
        var session = view.ResolveSession(NodeSelected(graphId, nodeId, asset.AssetId));

        // Assert — session is non-null and backed by ChannelCommandNodeDrawer.
        Assert.NotNull(session);
        Assert.Equal(typeof(ChannelCommandNodeDrawer), view.ResolvedDrawerKind);
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
