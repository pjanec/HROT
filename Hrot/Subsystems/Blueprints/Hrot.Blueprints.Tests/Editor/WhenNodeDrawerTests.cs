using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class WhenNodeDrawerTests
{
    // Minimal stub implementations for injected dependencies (not called in headless tests)
    private sealed class NullChannelCatalog : IChannelCommandCatalog
    {
        public IReadOnlyList<ChannelCommandCatalogEntry> GetEntries() => [];
    }
    private sealed class NullEventCatalog : IEngineEventCatalog
    {
        public IReadOnlyList<EngineEventCatalogEntry> GetEntries() => [];
    }
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
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto root)
            => (_, _) => false;

        public IReadOnlyList<Type> ExtractMandatoryComponents(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto root)
            => [];
    }

    private static WhenNodeDrawer MakeDrawer() => new(
        new NullChannelCatalog(),
        new NullEventCatalog(),
        new NullEditService(),
        new NullPredicateCompiler());

    private static BlueprintAsset MakeInstanceAsset() => new()
    {
        AssetId  = Guid.NewGuid(),
        Name     = "TestBp",
        Dispatch = BlueprintDispatchKind.Instance,
    };

    [Fact]
    public void Drawer_HandlesWhenNode()
    {
        var drawer = MakeDrawer();
        Assert.True(drawer.Handles(new WhenNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Drawer_HandlesWhenNode_ExcludesOtherTypes()
    {
        var drawer = MakeDrawer();
        Assert.False(drawer.Handles(new BranchNode { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new ReadEqsResultNode { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new SpawnEqsSensorNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Drawer_CreateSession_ReturnsNonNull()
    {
        var drawer = MakeDrawer();
        var node   = new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.ValueChanged };
        var asset  = MakeInstanceAsset();
        using var session = drawer.CreateSession(node, asset);
        Assert.NotNull(session);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Drawer_ModeChange_MarksDirty()
    {
        var drawer = MakeDrawer();
        var node   = new WhenNode { Id = Guid.NewGuid(), Mode = WhenMode.ValueChanged };
        var asset  = MakeInstanceAsset();
        var session = (WhenNodeSession)drawer.CreateSession(node, asset);

        Assert.False(session.IsDirty);
        session.SetModeForTest(WhenMode.EqsResult);

        Assert.True(session.IsDirty);
        Assert.Equal(WhenMode.EqsResult, node.Mode);
    }

    [Fact]
    public void Drawer_DispatchGuard_SessionCreated_ForNonInstance()
    {
        // Session must be creatable even for non-Instance assets (guard shown in Draw(),
        // which is not called in headless tests).
        var drawer = MakeDrawer();
        var node   = new WhenNode { Id = Guid.NewGuid() };
        var asset  = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Dispatch = BlueprintDispatchKind.AiPrimitive,
        };
        // Should NOT throw
        using var session = drawer.CreateSession(node, asset);
        Assert.NotNull(session);
    }
}
