using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.NodeDrawers;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// CA-02 (Slice 1a) — headless tests for <see cref="GetComponentNodeDrawer"/>/
/// <see cref="GetComponentNodeSession"/>. Mirrors <c>SharedNodeDrawersTests</c>: no ImGui calls —
/// all mutation logic is exercised through the internal test hooks, which
/// <see cref="GetComponentNodeSession.Draw"/> calls internally (Windows-verifiable only).
/// </summary>
public sealed class ComponentNodeDrawersTests
{
    private struct HealthTestComponent
    {
        public int Health;
        public float Armor;
    }

    private struct AmmoTestComponent
    {
        public int Ammo;
    }

    private static BlueprintAsset MakeAsset() =>
        new() { AssetId = Guid.NewGuid(), Name = "TestBP" };

    private static readonly string HealthFqn = typeof(HealthTestComponent).FullName!;
    private static readonly string AmmoFqn   = typeof(AmmoTestComponent).FullName!;

    private static readonly IComponentTypeProvider DefaultTypeProvider =
        new FakeComponentTypeProvider(HealthFqn, AmmoFqn, "My.Namespace.OtherComponent");

    private static BlueprintNodeDrawerRegistry CreateTestDrawerRegistry(IEditService editService)
    {
        var channelCatalog    = BuiltInChannelCommandCatalog.Instance;
        var eventCatalog      = BuiltInEngineEventCatalog.Instance;
        var predicateCompiler = new TestPredicateCompiler();
        var eqsTemplates      = new EqsTemplateRegistry();

        return BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
            channelCatalog, eventCatalog, editService, predicateCompiler, eqsTemplates);
    }

    // ── Handles ────────────────────────────────────────────────────────────────

    [Fact]
    public void Drawer_Handles_GetComponentNode_True()
    {
        var drawer = new GetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        Assert.True(drawer.Handles(new GetComponentNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Drawer_Handles_OtherNodeTypes_False()
    {
        var drawer = new GetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        Assert.False(drawer.Handles(new GetSharedNode  { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new SetVariableNode{ Id = Guid.NewGuid() }));
    }

    // ── CreateSession ─────────────────────────────────────────────────────────

    [Fact]
    public void Drawer_CreateSession_ReturnsNonNull_InitiallyNotDirty()
    {
        var drawer = new GetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        using var session = drawer.CreateSession(new GetComponentNode { Id = Guid.NewGuid() }, MakeAsset());

        Assert.NotNull(session);
        Assert.False(session.IsDirty);
    }

    // ── Mutation via test hooks ───────────────────────────────────────────────

    [Fact]
    public void Session_SetComponentTypeFqnForTest_UpdatesNode_BakesFields_MarksDirty()
    {
        var spy    = new SpyEditService();
        var asset  = MakeAsset();
        var node   = new GetComponentNode { Id = Guid.NewGuid() };
        var drawer = new GetComponentNodeDrawer(spy, DefaultTypeProvider);

        var session = (GetComponentNodeSession)drawer.CreateSession(node, asset);
        session.SetComponentTypeFqnForTest(HealthFqn);

        Assert.Equal(HealthFqn, node.ComponentTypeFqn);
        Assert.NotNull(node.Fields);
        Assert.Equal(2, node.Fields!.Count);
        Assert.Contains(node.Fields, f => f.Name == "Health");
        Assert.Contains(node.Fields, f => f.Name == "Armor");
        Assert.True(session.IsDirty);
        Assert.Equal(1, spy.MarkDirtyCallCount);
        Assert.Same(asset, spy.LastMarkedAsset);
    }

    [Fact]
    public void Session_SwitchingComponentType_RebakesFieldsForNewType()
    {
        var node   = new GetComponentNode { Id = Guid.NewGuid() };
        var drawer = new GetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        var session = (GetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        session.SetComponentTypeFqnForTest(HealthFqn);
        Assert.Equal(2, node.Fields!.Count);

        session.SetComponentTypeFqnForTest(AmmoFqn);
        Assert.Equal(AmmoFqn, node.ComponentTypeFqn);
        Assert.Single(node.Fields!);
        Assert.Equal("Ammo", node.Fields![0].Name);
    }

    [Fact]
    public void Session_SettingSameValue_DoesNotMarkDirtyAgain()
    {
        var spy    = new SpyEditService();
        var node   = new GetComponentNode { Id = Guid.NewGuid(), ComponentTypeFqn = HealthFqn };
        var drawer = new GetComponentNodeDrawer(spy, DefaultTypeProvider);

        var session = (GetComponentNodeSession)drawer.CreateSession(node, MakeAsset());
        session.SetComponentTypeFqnForTest(HealthFqn); // same value -- no-op

        Assert.False(session.IsDirty);
        Assert.Equal(0, spy.MarkDirtyCallCount);
    }

    [Fact]
    public void Session_ResetDirty_ClearsDirtyFlag()
    {
        var node   = new GetComponentNode { Id = Guid.NewGuid() };
        var drawer = new GetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);

        var session = (GetComponentNodeSession)drawer.CreateSession(node, MakeAsset());
        session.SetComponentTypeFqnForTest(HealthFqn);
        Assert.True(session.IsDirty);

        session.ResetDirty();
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Session_UnresolvableComponentType_BakesNullFields()
    {
        // Picking (or preserving) a component type the provider knows about by name but that
        // reflection cannot resolve to a loaded CLR type must not throw -- Fields collapses to null
        // (no field pins), mirroring GetShared's Fields=null legacy/unresolved shape.
        var node   = new GetComponentNode { Id = Guid.NewGuid() };
        var drawer = new GetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        var session = (GetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        session.SetComponentTypeFqnForTest("My.Namespace.OtherComponent");

        Assert.Equal("My.Namespace.OtherComponent", node.ComponentTypeFqn);
        Assert.Null(node.Fields);
    }

    // ── Component Type picker ────────────────────────────────────────────────

    [Fact]
    public void Session_GetAvailableComponentTypesForTest_SurfacesProviderList()
    {
        var node   = new GetComponentNode { Id = Guid.NewGuid() };
        var drawer = new GetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        var session = (GetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        Assert.Equal(DefaultTypeProvider.GetComponentTypeFqns(), session.GetAvailableComponentTypesForTest());
    }

    [Fact]
    public void Session_GetFilteredComponentTypesForTest_IsCaseInsensitiveSubstringMatch()
    {
        var node   = new GetComponentNode { Id = Guid.NewGuid() };
        var drawer = new GetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        var session = (GetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        var filtered = session.GetFilteredComponentTypesForTest("ammo");

        Assert.Equal(new[] { AmmoFqn }, filtered);
    }

    [Fact]
    public void Session_CurrentComponentTypeFqn_NotInProviderList_IsFlaggedUnlisted()
    {
        var node   = new GetComponentNode { Id = Guid.NewGuid(), ComponentTypeFqn = "Some.Renamed.Or.Unloaded.Component" };
        var drawer = new GetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        var session = (GetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        Assert.True(session.IsCurrentComponentTypeFqnUnlistedForTest());
    }

    [Fact]
    public void Session_CurrentComponentTypeFqn_InProviderList_IsNotFlaggedUnlisted()
    {
        var node   = new GetComponentNode { Id = Guid.NewGuid(), ComponentTypeFqn = HealthFqn };
        var drawer = new GetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        var session = (GetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        Assert.False(session.IsCurrentComponentTypeFqnUnlistedForTest());
    }

    [Fact]
    public void Session_UnlistedComponentTypeFqn_OpenThenNoChange_IsPreservedNotBlanked()
    {
        const string unlistedFqn = "Legacy.Namespace.RetiredComponent";
        var node   = new GetComponentNode { Id = Guid.NewGuid(), ComponentTypeFqn = unlistedFqn };
        var drawer = new GetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);

        var session = (GetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        Assert.True(session.IsCurrentComponentTypeFqnUnlistedForTest());
        Assert.Equal(unlistedFqn, node.ComponentTypeFqn); // untouched
        Assert.False(session.IsDirty);                     // opening alone must not dirty the session
    }

    // ── Managed-field caveat surfacing (GetCurrentFieldsForTest) ─────────────

    private struct ManagedFieldTestComponent
    {
        public int Ammo;
        public string Label;
    }

    [Fact]
    public void Session_GetCurrentFieldsForTest_FlagsManagedFieldsForCaveatDisplay()
    {
        var fqn = typeof(ManagedFieldTestComponent).FullName!;
        var node   = new GetComponentNode { Id = Guid.NewGuid(), ComponentTypeFqn = fqn };
        var drawer = new GetComponentNodeDrawer(new SpyEditService(), new FakeComponentTypeProvider(fqn));
        var session = (GetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        var fields = session.GetCurrentFieldsForTest();

        Assert.Equal(2, fields.Count);
        Assert.Contains(fields, f => f.Name == "Label" && f.IsManaged);
        Assert.Contains(fields, f => f.Name == "Ammo"  && !f.IsManaged);
    }

    // ── CA-05 (Slice 1b): picking a MANAGED component bakes IsManaged=true ──

    private sealed class ManagedTestComponentClass
    {
        public int Health;
    }

    [Fact]
    public void Session_PickingManagedComponent_BakesIsManagedTrue()
    {
        var fqn    = typeof(ManagedTestComponentClass).FullName!;
        var node   = new GetComponentNode { Id = Guid.NewGuid() };
        var drawer = new GetComponentNodeDrawer(new SpyEditService(), new FakeComponentTypeProvider(fqn));
        var session = (GetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        session.SetComponentTypeFqnForTest(fqn);

        Assert.True(node.IsManaged);
        Assert.NotNull(node.Fields);
        Assert.Contains(node.Fields!, f => f.Name == "Health");
    }

    [Fact]
    public void Session_PickingUnmanagedComponent_BakesIsManagedFalse()
    {
        var node   = new GetComponentNode { Id = Guid.NewGuid() };
        var session = (GetComponentNodeSession)new GetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider)
            .CreateSession(node, MakeAsset());

        session.SetComponentTypeFqnForTest(HealthFqn);

        Assert.False(node.IsManaged);
    }

    [Fact]
    public void Session_SwitchingFromManagedToUnmanagedComponent_UpdatesIsManaged()
    {
        var managedFqn = typeof(ManagedTestComponentClass).FullName!;
        var node   = new GetComponentNode { Id = Guid.NewGuid() };
        var drawer = new GetComponentNodeDrawer(new SpyEditService(), new FakeComponentTypeProvider(managedFqn, HealthFqn));
        var session = (GetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        session.SetComponentTypeFqnForTest(managedFqn);
        Assert.True(node.IsManaged);

        session.SetComponentTypeFqnForTest(HealthFqn);
        Assert.False(node.IsManaged);
    }

    // ── Registration in CreateNodeDrawerRegistry ─────────────────────────────

    [Fact]
    public void DrawerRegistry_Contains_GetComponentNodeDrawer()
    {
        var registry = CreateTestDrawerRegistry(new SpyEditService());
        var drawer   = registry.GetDrawerFor(new GetComponentNode { Id = Guid.NewGuid() });

        Assert.NotNull(drawer);
        Assert.IsType<GetComponentNodeDrawer>(drawer);
    }

    // ── Data-driven canvas refresh (mirrors SetSharedSession_StructuralEdit_FiresOnStructureChanged) ──

    [Fact]
    public void Session_StructuralEdit_FiresOnStructureChanged()
    {
        int structureChanged = 0;
        var editService = new EditService
        {
            Context = new EditServiceContext(
                new Hrot.Blueprints.Editor.GraphEditor.CommandHistory(),
                _ => { },
                onStructureChanged: _ => structureChanged++),
        };
        var asset   = MakeAsset();
        var node    = new GetComponentNode { Id = Guid.NewGuid() };
        var drawer  = new GetComponentNodeDrawer(editService, DefaultTypeProvider);
        var session = (GetComponentNodeSession)drawer.CreateSession(node, asset);

        session.SetComponentTypeFqnForTest(HealthFqn);

        Assert.True(structureChanged >= 1, "structural edit should notify the derived views to re-project");
    }

    // ── CA-04 (Slice W1): SetComponentNodeDrawer / SetComponentNodeSession ──────
    // Mirrors the GetComponentNodeDrawer/Session tests above exactly (same picker mechanics, no
    // expand toggle -- always multi-pin), just against SetComponentNode + SetComponentNodeDrawer.

    [Fact]
    public void SetDrawer_Handles_SetComponentNode_True()
    {
        var drawer = new SetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        Assert.True(drawer.Handles(new SetComponentNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void SetDrawer_Handles_OtherNodeTypes_False()
    {
        var drawer = new SetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        Assert.False(drawer.Handles(new GetComponentNode { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new SetSharedNode    { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void SetDrawer_CreateSession_ReturnsNonNull_InitiallyNotDirty()
    {
        var drawer = new SetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        using var session = drawer.CreateSession(new SetComponentNode { Id = Guid.NewGuid() }, MakeAsset());

        Assert.NotNull(session);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void SetSession_SetComponentTypeFqnForTest_UpdatesNode_BakesFields_MarksDirty()
    {
        var spy    = new SpyEditService();
        var asset  = MakeAsset();
        var node   = new SetComponentNode { Id = Guid.NewGuid() };
        var drawer = new SetComponentNodeDrawer(spy, DefaultTypeProvider);

        var session = (SetComponentNodeSession)drawer.CreateSession(node, asset);
        session.SetComponentTypeFqnForTest(HealthFqn);

        Assert.Equal(HealthFqn, node.ComponentTypeFqn);
        Assert.NotNull(node.Fields);
        Assert.Equal(2, node.Fields!.Count);
        Assert.Contains(node.Fields, f => f.Name == "Health");
        Assert.Contains(node.Fields, f => f.Name == "Armor");
        Assert.True(session.IsDirty);
        Assert.Equal(1, spy.MarkDirtyCallCount);
        Assert.Same(asset, spy.LastMarkedAsset);
    }

    [Fact]
    public void SetSession_SwitchingComponentType_RebakesFieldsForNewType()
    {
        var node   = new SetComponentNode { Id = Guid.NewGuid() };
        var drawer = new SetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        var session = (SetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        session.SetComponentTypeFqnForTest(HealthFqn);
        Assert.Equal(2, node.Fields!.Count);

        session.SetComponentTypeFqnForTest(AmmoFqn);
        Assert.Equal(AmmoFqn, node.ComponentTypeFqn);
        Assert.Single(node.Fields!);
        Assert.Equal("Ammo", node.Fields![0].Name);
    }

    [Fact]
    public void SetSession_SettingSameValue_DoesNotMarkDirtyAgain()
    {
        var spy    = new SpyEditService();
        var node   = new SetComponentNode { Id = Guid.NewGuid(), ComponentTypeFqn = HealthFqn };
        var drawer = new SetComponentNodeDrawer(spy, DefaultTypeProvider);

        var session = (SetComponentNodeSession)drawer.CreateSession(node, MakeAsset());
        session.SetComponentTypeFqnForTest(HealthFqn); // same value -- no-op

        Assert.False(session.IsDirty);
        Assert.Equal(0, spy.MarkDirtyCallCount);
    }

    [Fact]
    public void SetSession_ResetDirty_ClearsDirtyFlag()
    {
        var node   = new SetComponentNode { Id = Guid.NewGuid() };
        var drawer = new SetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);

        var session = (SetComponentNodeSession)drawer.CreateSession(node, MakeAsset());
        session.SetComponentTypeFqnForTest(HealthFqn);
        Assert.True(session.IsDirty);

        session.ResetDirty();
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void SetSession_UnresolvableComponentType_BakesNullFields()
    {
        var node   = new SetComponentNode { Id = Guid.NewGuid() };
        var drawer = new SetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        var session = (SetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        session.SetComponentTypeFqnForTest("My.Namespace.OtherComponent");

        Assert.Equal("My.Namespace.OtherComponent", node.ComponentTypeFqn);
        Assert.Null(node.Fields);
    }

    [Fact]
    public void SetSession_GetAvailableComponentTypesForTest_SurfacesProviderList()
    {
        var node   = new SetComponentNode { Id = Guid.NewGuid() };
        var drawer = new SetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        var session = (SetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        Assert.Equal(DefaultTypeProvider.GetComponentTypeFqns(), session.GetAvailableComponentTypesForTest());
    }

    [Fact]
    public void SetSession_GetFilteredComponentTypesForTest_IsCaseInsensitiveSubstringMatch()
    {
        var node   = new SetComponentNode { Id = Guid.NewGuid() };
        var drawer = new SetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        var session = (SetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        var filtered = session.GetFilteredComponentTypesForTest("ammo");

        Assert.Equal(new[] { AmmoFqn }, filtered);
    }

    [Fact]
    public void SetSession_CurrentComponentTypeFqn_NotInProviderList_IsFlaggedUnlisted()
    {
        var node   = new SetComponentNode { Id = Guid.NewGuid(), ComponentTypeFqn = "Some.Renamed.Or.Unloaded.Component" };
        var drawer = new SetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        var session = (SetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        Assert.True(session.IsCurrentComponentTypeFqnUnlistedForTest());
    }

    [Fact]
    public void SetSession_CurrentComponentTypeFqn_InProviderList_IsNotFlaggedUnlisted()
    {
        var node   = new SetComponentNode { Id = Guid.NewGuid(), ComponentTypeFqn = HealthFqn };
        var drawer = new SetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);
        var session = (SetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        Assert.False(session.IsCurrentComponentTypeFqnUnlistedForTest());
    }

    [Fact]
    public void SetSession_UnlistedComponentTypeFqn_OpenThenNoChange_IsPreservedNotBlanked()
    {
        const string unlistedFqn = "Legacy.Namespace.RetiredComponent";
        var node   = new SetComponentNode { Id = Guid.NewGuid(), ComponentTypeFqn = unlistedFqn };
        var drawer = new SetComponentNodeDrawer(new SpyEditService(), DefaultTypeProvider);

        var session = (SetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        Assert.True(session.IsCurrentComponentTypeFqnUnlistedForTest());
        Assert.Equal(unlistedFqn, node.ComponentTypeFqn); // untouched
        Assert.False(session.IsDirty);                     // opening alone must not dirty the session
    }

    [Fact]
    public void SetSession_GetCurrentFieldsForTest_FlagsManagedFieldsForCaveatDisplay()
    {
        var fqn = typeof(ManagedFieldTestComponent).FullName!;
        var node   = new SetComponentNode { Id = Guid.NewGuid(), ComponentTypeFqn = fqn };
        var drawer = new SetComponentNodeDrawer(new SpyEditService(), new FakeComponentTypeProvider(fqn));
        var session = (SetComponentNodeSession)drawer.CreateSession(node, MakeAsset());

        var fields = session.GetCurrentFieldsForTest();

        Assert.Equal(2, fields.Count);
        Assert.Contains(fields, f => f.Name == "Label" && f.IsManaged);
        Assert.Contains(fields, f => f.Name == "Ammo"  && !f.IsManaged);
    }

    [Fact]
    public void SetDrawerRegistry_Contains_SetComponentNodeDrawer()
    {
        var registry = CreateTestDrawerRegistry(new SpyEditService());
        var drawer   = registry.GetDrawerFor(new SetComponentNode { Id = Guid.NewGuid() });

        Assert.NotNull(drawer);
        Assert.IsType<SetComponentNodeDrawer>(drawer);
    }

    [Fact]
    public void SetSession_StructuralEdit_FiresOnStructureChanged()
    {
        int structureChanged = 0;
        var editService = new EditService
        {
            Context = new EditServiceContext(
                new Hrot.Blueprints.Editor.GraphEditor.CommandHistory(),
                _ => { },
                onStructureChanged: _ => structureChanged++),
        };
        var asset   = MakeAsset();
        var node    = new SetComponentNode { Id = Guid.NewGuid() };
        var drawer  = new SetComponentNodeDrawer(editService, DefaultTypeProvider);
        var session = (SetComponentNodeSession)drawer.CreateSession(node, asset);

        session.SetComponentTypeFqnForTest(HealthFqn);

        Assert.True(structureChanged >= 1, "structural edit should notify the derived views to re-project");
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

    /// <summary>Fake <see cref="IComponentTypeProvider"/> returning a fixed, sorted FQN list.</summary>
    private sealed class FakeComponentTypeProvider : IComponentTypeProvider
    {
        private readonly IReadOnlyList<string> _fqns;

        public FakeComponentTypeProvider(params string[] fqns)
            => _fqns = fqns.OrderBy(s => s, StringComparer.Ordinal).ToList();

        public IReadOnlyList<string> GetComponentTypeFqns() => _fqns;
    }
}
