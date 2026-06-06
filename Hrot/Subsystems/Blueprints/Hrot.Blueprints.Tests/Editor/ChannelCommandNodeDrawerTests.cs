using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Headless tests for <see cref="ChannelCommandNodeDrawer"/> and
/// <see cref="ChannelCommandNodeSession"/> (BF-BATCH-0607-FIX-B).
/// No ImGui calls — all mutation logic exercised through internal test hooks.
/// </summary>
public sealed class ChannelCommandNodeDrawerTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static ChannelCommandNode MakeNode(string channelType = "", string actionId = "") => new()
    {
        Id          = Guid.NewGuid(),
        ChannelType = channelType,
        ActionId    = actionId,
    };

    private static BlueprintAsset MakeAsset() =>
        new() { AssetId = Guid.NewGuid(), Name = "TestBP" };

    /// <summary>Catalog with MoveTo and AimAndFire entries.</summary>
    private static IChannelCommandCatalog MakeCatalog() => BuiltInChannelCommandCatalog.Instance;

    // ── CC-01: Handles ────────────────────────────────────────────────────────

    [Fact]
    public void Drawer_Handles_ChannelCommandNode_True()
    {
        var drawer = new ChannelCommandNodeDrawer(MakeCatalog(), new SpyEditService());
        Assert.True(drawer.Handles(new ChannelCommandNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Drawer_Handles_OtherNodeTypes_False()
    {
        var drawer = new ChannelCommandNodeDrawer(MakeCatalog(), new SpyEditService());
        Assert.False(drawer.Handles(new FunctionCallNode { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new WhenNode         { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new BranchNode       { Id = Guid.NewGuid() }));
    }

    // ── CC-02: CreateSession ──────────────────────────────────────────────────

    [Fact]
    public void Drawer_CreateSession_ReturnsNonNull()
    {
        var drawer = new ChannelCommandNodeDrawer(MakeCatalog(), new SpyEditService());
        using var session = drawer.CreateSession(MakeNode(), MakeAsset());
        Assert.NotNull(session);
    }

    [Fact]
    public void Drawer_CreateSession_InitiallyNotDirty()
    {
        var drawer = new ChannelCommandNodeDrawer(MakeCatalog(), new SpyEditService());
        using var session = drawer.CreateSession(MakeNode(), MakeAsset());
        Assert.False(session.IsDirty);
    }

    // ── CC-03: SelectActionForTest sets ChannelType + ActionId ───────────────

    [Fact]
    public void Session_SelectActionForTest_SetsChannelTypeAndActionId()
    {
        var node    = MakeNode();
        var asset   = MakeAsset();
        var catalog = MakeCatalog();
        var drawer  = new ChannelCommandNodeDrawer(catalog, new SpyEditService());

        var session = (ChannelCommandNodeSession)drawer.CreateSession(node, asset);
        // Select the first entry (MoveTo)
        session.SelectActionForTest(0);

        var entry = catalog.GetEntries()[0];
        // ChannelType is stored as the short class name (LastSegment of ChannelTypeFqn).
        var expectedShortType = entry.ChannelTypeFqn.Contains('.')
            ? entry.ChannelTypeFqn[(entry.ChannelTypeFqn.LastIndexOf('.') + 1)..]
            : entry.ChannelTypeFqn;
        Assert.Equal(expectedShortType, node.ChannelType);
        Assert.Equal(entry.Name,        node.ActionId);
    }

    [Fact]
    public void Session_SelectActionForTest_MarksDirty()
    {
        var node   = MakeNode();
        var asset  = MakeAsset();
        var drawer = new ChannelCommandNodeDrawer(MakeCatalog(), new SpyEditService());

        var session = (ChannelCommandNodeSession)drawer.CreateSession(node, asset);
        session.SelectActionForTest(0);

        Assert.True(session.IsDirty);
    }

    [Fact]
    public void Session_SelectActionForTest_CallsMarkDirtyOnEditService()
    {
        var spy    = new SpyEditService();
        var node   = MakeNode();
        var asset  = MakeAsset();
        var drawer = new ChannelCommandNodeDrawer(MakeCatalog(), spy);

        var session = (ChannelCommandNodeSession)drawer.CreateSession(node, asset);
        session.SelectActionForTest(0);

        Assert.Equal(1, spy.MarkDirtyCallCount);
        Assert.Same(asset, spy.LastMarkedAsset);
    }

    // ── CC-04: After setting ActionId, NodePinSchema projects param pins ──────

    [Fact]
    public void Session_SelectMoveTo_NodePinSchema_ProjectsMoveToParams()
    {
        var catalog = MakeCatalog();
        var node    = MakeNode();
        var asset   = MakeAsset();
        var drawer  = new ChannelCommandNodeDrawer(catalog, new SpyEditService());

        var session = (ChannelCommandNodeSession)drawer.CreateSession(node, asset);
        // Find the MoveTo entry
        var entries = catalog.GetEntries();
        var moveToIdx = -1;
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Name == "MoveTo") { moveToIdx = i; break; }

        Assert.True(moveToIdx >= 0, "MoveTo entry must be in the catalog");
        session.SelectActionForTest(moveToIdx);

        // NodePinSchema should now project parameter data-IN pins for MoveTo
        var pins = Hrot.Blueprints.Editor.Host.NodePinSchema.GetCanonicalPins(node, channelCommands: catalog);
        var dataInPins = pins.Where(p => !p.IsExec && p.Direction == "In").ToList();

        Assert.True(dataInPins.Count > 0,
            "After setting MoveTo, NodePinSchema must project at least one data-IN param pin.");
    }

    // ── CC-05: ResetDirty ─────────────────────────────────────────────────────

    [Fact]
    public void Session_ResetDirty_ClearsDirtyFlag()
    {
        var node   = MakeNode();
        var asset  = MakeAsset();
        var drawer = new ChannelCommandNodeDrawer(MakeCatalog(), new SpyEditService());

        var session = (ChannelCommandNodeSession)drawer.CreateSession(node, asset);
        session.SelectActionForTest(0);
        Assert.True(session.IsDirty);

        session.ResetDirty();

        Assert.False(session.IsDirty);
    }

    // ── CC-06: Registration in CreateNodeDrawerRegistry ──────────────────────

    [Fact]
    public void DrawerRegistry_Contains_ChannelCommandNodeDrawer()
    {
        var registry = CreateTestDrawerRegistry();
        var drawer   = registry.GetDrawerFor(new ChannelCommandNode { Id = Guid.NewGuid() });

        Assert.NotNull(drawer);
        Assert.IsType<ChannelCommandNodeDrawer>(drawer);
    }

    [Fact]
    public void DrawerRegistry_TryGet_ChannelCommandNode_Succeeds()
    {
        var registry = CreateTestDrawerRegistry();
        Assert.True(registry.TryGet(typeof(ChannelCommandNode), out var drawer));
        Assert.NotNull(drawer);
    }

    // ── CC-07: Out-of-range index is a no-op ─────────────────────────────────

    [Fact]
    public void Session_SelectActionForTest_OutOfRange_IsNoOp()
    {
        var node   = MakeNode();
        var asset  = MakeAsset();
        var drawer = new ChannelCommandNodeDrawer(MakeCatalog(), new SpyEditService());

        var session = (ChannelCommandNodeSession)drawer.CreateSession(node, asset);
        session.SelectActionForTest(-1);

        Assert.Equal("", node.ChannelType);
        Assert.Equal("", node.ActionId);
        Assert.False(session.IsDirty);
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
    }

    private sealed class TestPredicateCompiler : Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler
    {
        public Func<Fdp.Core.EntityRepository, Fdp.Core.Entity, bool> CompileComponentPredicate(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto predicate) => (_, _) => true;

        public Func<Fdp.Core.EntityRepository, Fdp.Core.Entity, bool> CompileEntityPredicate(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto predicate) => (_, _) => true;

        public IReadOnlyList<Type> ExtractMandatoryComponents(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto predicate) => Array.Empty<Type>();
    }
}
