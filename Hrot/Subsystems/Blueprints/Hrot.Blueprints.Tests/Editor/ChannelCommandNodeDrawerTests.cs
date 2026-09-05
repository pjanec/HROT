using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Headless tests for <see cref="ChannelCommandNodeDrawer"/> and
/// <see cref="ChannelCommandNodeSession"/> (AN5 — immutable action selection).
/// The drawer now renders ChannelType/ActionId as READ-ONLY labels; no mutation path exists.
/// No ImGui calls — all logic exercised through the public session interface.
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
        var drawer = new ChannelCommandNodeDrawer(MakeCatalog());
        Assert.True(drawer.Handles(new ChannelCommandNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Drawer_Handles_OtherNodeTypes_False()
    {
        var drawer = new ChannelCommandNodeDrawer(MakeCatalog());
        Assert.False(drawer.Handles(new FunctionCallNode { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new WhenNode         { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new BranchNode       { Id = Guid.NewGuid() }));
    }

    // ── CC-02: CreateSession ──────────────────────────────────────────────────

    [Fact]
    public void Drawer_CreateSession_ReturnsNonNull()
    {
        var drawer = new ChannelCommandNodeDrawer(MakeCatalog());
        using var session = drawer.CreateSession(MakeNode(), MakeAsset());
        Assert.NotNull(session);
    }

    [Fact]
    public void Drawer_CreateSession_InitiallyNotDirty()
    {
        var drawer = new ChannelCommandNodeDrawer(MakeCatalog());
        using var session = drawer.CreateSession(MakeNode(), MakeAsset());
        Assert.False(session.IsDirty);
    }

    // ── CC-03: Session is always read-only — no action mutation (AN5) ────────

    /// <summary>
    /// AN5: IsDirty is always false because the session has no mutation path.
    /// No SelectActionForTest hook exists; the action is baked at node creation
    /// via the per-action palette (D-B decision).
    /// </summary>
    [Fact]
    public void Session_IsDirty_IsAlwaysFalse()
    {
        var drawer  = new ChannelCommandNodeDrawer(MakeCatalog());
        var session = drawer.CreateSession(MakeNode(), MakeAsset());
        // IsDirty is false initially.
        Assert.False(session.IsDirty);
        // ResetDirty is a no-op but must not throw.
        session.ResetDirty();
        Assert.False(session.IsDirty);
    }

    /// <summary>
    /// AN5: The session type does NOT expose a SelectActionForTest hook (mutation removed).
    /// Verify the internal type has no such method.
    /// </summary>
    [Fact]
    public void Session_HasNoSelectActionForTestMutationHook()
    {
        var drawer  = new ChannelCommandNodeDrawer(MakeCatalog());
        var session = drawer.CreateSession(MakeNode(), MakeAsset());
        var sessionType = session.GetType();

        // The mutation hook must not exist on the session (read-only session, AN5).
        var mutationMethod = sessionType.GetMethod(
            "SelectActionForTest",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.Null(mutationMethod);
    }

    // ── CC-04: Pre-configured node — NodePinSchema projects param pins ────────

    [Fact]
    public void Session_ConfiguredMoveTo_NodePinSchema_ProjectsMoveToParams()
    {
        var catalog = MakeCatalog();
        // Node pre-configured with MoveTo — simulates the AN4 palette baking ChannelType+ActionId
        // at create-time, as intended by D-B.
        var entries   = catalog.GetEntries();
        var moveToEntry = entries.FirstOrDefault(e => e.Name == "MoveTo");
        Assert.NotNull(moveToEntry);

        var shortType = moveToEntry.ChannelTypeFqn.Contains('.')
            ? moveToEntry.ChannelTypeFqn[(moveToEntry.ChannelTypeFqn.LastIndexOf('.') + 1)..]
            : moveToEntry.ChannelTypeFqn;

        var node  = MakeNode(channelType: shortType, actionId: moveToEntry.Name);
        var asset = MakeAsset();
        var drawer  = new ChannelCommandNodeDrawer(catalog);

        // Creating a session must not change the node's baked fields.
        using var session = drawer.CreateSession(node, asset);
        Assert.Equal(shortType,          node.ChannelType);
        Assert.Equal(moveToEntry.Name,   node.ActionId);
        Assert.False(session.IsDirty);

        // NodePinSchema should project parameter data-IN pins for MoveTo
        var pins = Hrot.Blueprints.Editor.Host.NodePinSchema.GetCanonicalPins(node, channelCommands: catalog);
        var dataInPins = pins.Where(p => !p.IsExec && p.Direction == "In").ToList();

        Assert.True(dataInPins.Count > 0,
            "A node pre-configured with MoveTo must have at least one data-IN param pin.");
    }

    // ── CC-05: ResetDirty ─────────────────────────────────────────────────────

    [Fact]
    public void Session_ResetDirty_IsNoOp_RemainsClean()
    {
        var node   = MakeNode();
        var asset  = MakeAsset();
        var drawer = new ChannelCommandNodeDrawer(MakeCatalog());

        var session = drawer.CreateSession(node, asset);
        // Already false before reset.
        Assert.False(session.IsDirty);
        session.ResetDirty();
        // Still false after reset (no-op on a read-only session).
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

    // ── helpers ───────────────────────────────────────────────────────────────

    private static BlueprintNodeDrawerRegistry CreateTestDrawerRegistry()
    {
        var channelCatalog    = BuiltInChannelCommandCatalog.Instance;
        var eventCatalog      = BuiltInEngineEventCatalog.Instance;
        var editService       = new NullEditService();
        var predicateCompiler = new TestPredicateCompiler();
        var eqsTemplates      = new EqsTemplateRegistry();

        return BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
            channelCatalog, eventCatalog, editService, predicateCompiler, eqsTemplates);
    }

    // ── Test stubs ────────────────────────────────────────────────────────────

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
