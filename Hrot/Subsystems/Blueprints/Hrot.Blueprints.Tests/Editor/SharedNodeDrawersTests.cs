using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.NodeDrawers;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Slice 2a-3 — headless tests for <see cref="GetSharedNodeDrawer"/>/<see cref="SetSharedNodeDrawer"/>
/// and their sessions. Mirrors <c>FunctionCallNodeDrawerTests</c>: no ImGui calls — all mutation
/// logic is exercised through the internal test hooks (<c>SetVariableIdForTest</c> /
/// <c>SetSharedTypeIdForTest</c>), which <see cref="GetSharedNodeSession.Draw"/> /
/// <see cref="SetSharedNodeSession.Draw"/> call internally. <c>Draw()</c> itself (the raw
/// ImGui.InputText plumbing) is NOT exercised here — it is Windows-verifiable only.
/// </summary>
public sealed class SharedNodeDrawersTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static BlueprintAsset MakeAsset() =>
        new() { AssetId = Guid.NewGuid(), Name = "TestBP" };

    private static BlueprintNodeDrawerRegistry CreateTestDrawerRegistry(IEditService editService)
    {
        var channelCatalog    = BuiltInChannelCommandCatalog.Instance;
        var eventCatalog      = BuiltInEngineEventCatalog.Instance;
        var predicateCompiler = new TestPredicateCompiler();
        var eqsTemplates      = new EqsTemplateRegistry();

        return BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
            channelCatalog, eventCatalog, editService, predicateCompiler, eqsTemplates);
    }

    // ── GetShared: Handles ───────────────────────────────────────────────────

    [Fact]
    public void GetSharedDrawer_Handles_GetSharedNode_True()
    {
        var drawer = new GetSharedNodeDrawer(new SpyEditService());
        Assert.True(drawer.Handles(new GetSharedNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void GetSharedDrawer_Handles_OtherNodeTypes_False()
    {
        var drawer = new GetSharedNodeDrawer(new SpyEditService());
        Assert.False(drawer.Handles(new SetSharedNode  { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new GetVariableNode{ Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new FunctionCallNode{ Id = Guid.NewGuid() }));
    }

    // ── SetShared: Handles ───────────────────────────────────────────────────

    [Fact]
    public void SetSharedDrawer_Handles_SetSharedNode_True()
    {
        var drawer = new SetSharedNodeDrawer(new SpyEditService());
        Assert.True(drawer.Handles(new SetSharedNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void SetSharedDrawer_Handles_OtherNodeTypes_False()
    {
        var drawer = new SetSharedNodeDrawer(new SpyEditService());
        Assert.False(drawer.Handles(new GetSharedNode  { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new SetVariableNode{ Id = Guid.NewGuid() }));
    }

    // ── CreateSession ─────────────────────────────────────────────────────────

    [Fact]
    public void GetSharedDrawer_CreateSession_ReturnsNonNull_InitiallyNotDirty()
    {
        var drawer = new GetSharedNodeDrawer(new SpyEditService());
        using var session = drawer.CreateSession(new GetSharedNode { Id = Guid.NewGuid() }, MakeAsset());

        Assert.NotNull(session);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void SetSharedDrawer_CreateSession_ReturnsNonNull_InitiallyNotDirty()
    {
        var drawer = new SetSharedNodeDrawer(new SpyEditService());
        using var session = drawer.CreateSession(new SetSharedNode { Id = Guid.NewGuid() }, MakeAsset());

        Assert.NotNull(session);
        Assert.False(session.IsDirty);
    }

    // ── GetShared: mutation via test hooks ───────────────────────────────────

    [Fact]
    public void GetSharedSession_SetVariableIdForTest_UpdatesNode_MarksDirty()
    {
        var spy    = new SpyEditService();
        var asset  = MakeAsset();
        var node   = new GetSharedNode { Id = Guid.NewGuid() };
        var drawer = new GetSharedNodeDrawer(spy);

        var session = (GetSharedNodeSession)drawer.CreateSession(node, asset);
        session.SetVariableIdForTest("RallyPoint");

        Assert.Equal("RallyPoint", node.VariableId);
        Assert.True(session.IsDirty);
        Assert.Equal(1, spy.MarkDirtyCallCount);
        Assert.Same(asset, spy.LastMarkedAsset);
    }

    [Fact]
    public void GetSharedSession_SetSharedTypeIdForTest_UpdatesNode_MarksDirty()
    {
        var spy    = new SpyEditService();
        var asset  = MakeAsset();
        var node   = new GetSharedNode { Id = Guid.NewGuid() };
        var drawer = new GetSharedNodeDrawer(spy);

        var session = (GetSharedNodeSession)drawer.CreateSession(node, asset);
        session.SetSharedTypeIdForTest("global::Hrot.AI.Behaviors.SquadRallyState");

        Assert.Equal("global::Hrot.AI.Behaviors.SquadRallyState", node.SharedTypeId);
        Assert.True(session.IsDirty);
        Assert.Equal(1, spy.MarkDirtyCallCount);
    }

    [Fact]
    public void GetSharedSession_SettingSameValue_DoesNotMarkDirtyAgain()
    {
        var spy    = new SpyEditService();
        var node   = new GetSharedNode { Id = Guid.NewGuid(), VariableId = "RallyPoint" };
        var drawer = new GetSharedNodeDrawer(spy);

        var session = (GetSharedNodeSession)drawer.CreateSession(node, MakeAsset());
        session.SetVariableIdForTest("RallyPoint"); // same value — no-op

        Assert.False(session.IsDirty);
        Assert.Equal(0, spy.MarkDirtyCallCount);
    }

    [Fact]
    public void GetSharedSession_ResetDirty_ClearsDirtyFlag()
    {
        var node   = new GetSharedNode { Id = Guid.NewGuid() };
        var drawer = new GetSharedNodeDrawer(new SpyEditService());

        var session = (GetSharedNodeSession)drawer.CreateSession(node, MakeAsset());
        session.SetVariableIdForTest("Slot");
        Assert.True(session.IsDirty);

        session.ResetDirty();
        Assert.False(session.IsDirty);
    }

    // ── SetShared: mutation via test hooks ───────────────────────────────────

    [Fact]
    public void SetSharedSession_SetVariableIdForTest_UpdatesNode_MarksDirty()
    {
        var spy    = new SpyEditService();
        var asset  = MakeAsset();
        var node   = new SetSharedNode { Id = Guid.NewGuid() };
        var drawer = new SetSharedNodeDrawer(spy);

        var session = (SetSharedNodeSession)drawer.CreateSession(node, asset);
        session.SetVariableIdForTest("RallyPoint");

        Assert.Equal("RallyPoint", node.VariableId);
        Assert.True(session.IsDirty);
        Assert.Equal(1, spy.MarkDirtyCallCount);
        Assert.Same(asset, spy.LastMarkedAsset);
    }

    [Fact]
    public void SetSharedSession_SetSharedTypeIdForTest_UpdatesNode_MarksDirty()
    {
        var spy    = new SpyEditService();
        var node   = new SetSharedNode { Id = Guid.NewGuid() };
        var drawer = new SetSharedNodeDrawer(spy);

        var session = (SetSharedNodeSession)drawer.CreateSession(node, MakeAsset());
        session.SetSharedTypeIdForTest("global::Hrot.AI.Behaviors.SquadRallyState");

        Assert.Equal("global::Hrot.AI.Behaviors.SquadRallyState", node.SharedTypeId);
        Assert.True(session.IsDirty);
        Assert.Equal(1, spy.MarkDirtyCallCount);
    }

    [Fact]
    public void SetSharedSession_TwoEdits_CallsMarkDirtyTwice()
    {
        var spy    = new SpyEditService();
        var node   = new SetSharedNode { Id = Guid.NewGuid() };
        var drawer = new SetSharedNodeDrawer(spy);

        var session = (SetSharedNodeSession)drawer.CreateSession(node, MakeAsset());
        session.SetVariableIdForTest("Slot");
        session.SetSharedTypeIdForTest("global::My.Struct");

        Assert.Equal(2, spy.MarkDirtyCallCount);
    }

    // ── Registration in CreateNodeDrawerRegistry ─────────────────────────────

    [Fact]
    public void DrawerRegistry_Contains_GetSharedNodeDrawer()
    {
        var registry = CreateTestDrawerRegistry(new SpyEditService());
        var drawer   = registry.GetDrawerFor(new GetSharedNode { Id = Guid.NewGuid() });

        Assert.NotNull(drawer);
        Assert.IsType<GetSharedNodeDrawer>(drawer);
    }

    [Fact]
    public void DrawerRegistry_Contains_SetSharedNodeDrawer()
    {
        var registry = CreateTestDrawerRegistry(new SpyEditService());
        var drawer   = registry.GetDrawerFor(new SetSharedNode { Id = Guid.NewGuid() });

        Assert.NotNull(drawer);
        Assert.IsType<SetSharedNodeDrawer>(drawer);
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

    private sealed class TestPredicateCompiler : IPredicateCompiler
    {
        public Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto predicate)
            => (_, _) => true;

        public Func<EntityRepository, Entity, bool> CompileEntityPredicate(SearchPredicateDto predicate)
            => (_, _) => true;

        public IReadOnlyList<Type> ExtractMandatoryComponents(SearchPredicateDto predicate)
            => Array.Empty<Type>();
    }
}
