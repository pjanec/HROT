using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Selection;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using StructEdit.Core;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Host;

// ── Stub exporter ─────────────────────────────────────────────────────────────

file sealed class StubExporterForHsmBB1D : IActionSchemaExporter
{
    private readonly Dictionary<string, ActionSchemaEntry> _map = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ActionSchemaEntry> All => _map;
    public event Action? Changed { add { } remove { } }

    public void Register(string fqn, Type dtoType)
        => _map[fqn] = new ActionSchemaEntry(fqn, dtoType, ActionHosting.Hsm, BlackboardAccess.ReadWrite, null);

    public ActionSchemaEntry? Lookup(string fqn) => _map.GetValueOrDefault(fqn);
    public void Rebuild() { }
}

/// <summary>
/// BB1D integration tests: verify that a SINGLE <see cref="HsmFacetFqnContext"/>
/// shared between <see cref="HsmSelectionBridgeHelper.BuildFacetDispatcher(HsmAsset?, HsmFacetFqnContext?)"/>
/// (writer) and <see cref="HsmPickerDrawerFactory.BuildDrawers"/> (reader) causes the
/// <see cref="HsmBlackboardFieldPickerDrawer"/> to return ONLY the type-compatible variables.
///
/// <para>This is the HSM counterpart of the BTree BB1D integration test.
/// If the context is NOT shared, the drawer has no FQN context and shows ALL variables
/// — this test would fail.</para>
/// </summary>
public sealed class BB1DHsmSharedContextIntegrationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (HsmDefinitionBlob blob, MachineMetadata meta) Compile(HsmBuilder b)
    {
        var graph = b.Build();
        HsmNormalizer.Normalize(graph);
        var flat = HsmFlattener.Flatten(graph);
        return (HsmEmitter.Emit(flat), HsmEmitter.BuildMachineMetadata(graph));
    }

    /// <summary>
    /// Builds an HSM with states "Idle" → "Active" connected by a transition with an
    /// action function bound to <paramref name="actionFqn"/>, plus blackboard variables.
    /// </summary>
    private static HsmAsset MakeHsmAssetWithTransitionAction(
        string actionFqn,
        params BlackboardVariableEntry[] vars)
    {
        var b = new HsmBuilder("BB1D");
        b.Event("Fire", 1);
        b.State("Active").Final();
        b.State("Idle").Initial().On("Fire").GoTo("Active").Action(actionFqn);
        var (blob, meta) = Compile(b);
        var asset = HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), "BB1D", "", false, "");
        if (vars.Length > 0)
            asset.SetBlackboardVariables(vars);
        return asset;
    }

    private static EditNode MakeNodeWithAttr(params Attribute[] attrs)
    {
        var meta = new EditNodeMetadata { CustomAttributes = attrs };
        return new EditNode(
            id:       new EditNodeId(0),
            name:     "Field",
            jsonPath: "$.Field",
            kind:     EditNodeKind.String,
            clrType:  typeof(string),
            metadata: meta);
    }

    // ── Core integration test: shared context causes type-filtering ───────────

    /// <summary>
    /// CRITICAL: The BB1D HSM wiring test.
    ///
    /// Build the HSM dispatcher via BuildFacetDispatcher(asset, ctx) and the drawers via
    /// BuildDrawers(asset, exporter, ctx) using the SAME ctx instance.
    /// Drive dispatcher.GetFacet(transitionSelection) which writes CurrentActionFqn to ctx.
    /// Then assert the HsmBlackboardFieldPickerDrawer returns ONLY the float-typed variable,
    /// not the int-typed one.
    ///
    /// If the context is NOT shared (old code before BB1D), the drawer has no FQN context
    /// and falls back to returning ALL variables — the test would fail.
    /// </summary>
    [Fact]
    public void SharedContext_Hsm_DispatcherWritesFqn_DrawerFiltersToTType()
    {
        const string fqn = "Ns.HsmFloatAction";

        var asset = MakeHsmAssetWithTransitionAction(fqn,
            new BlackboardVariableEntry("floatVar", typeof(float), null),
            new BlackboardVariableEntry("intVar",   typeof(int),   null));

        var exporter = new StubExporterForHsmBB1D();
        exporter.Register(fqn, typeof(float));

        // ONE shared context — this is the key to BB1D.
        var sharedCtx = new HsmFacetFqnContext();

        // Build dispatcher (writer) and drawers (reader) with the SAME sharedCtx.
        var dispatcher = HsmSelectionBridgeHelper.BuildFacetDispatcher(asset, sharedCtx);
        var drawerMap  = HsmPickerDrawerFactory.BuildDrawers(asset, exporter, sharedCtx);

        dispatcher.Should().NotBeNull("dispatcher must be built from a non-null asset");

        // Find the transition from Idle → Active (the one with the action).
        var transition = asset.AllTransitions
            .FirstOrDefault(t => !string.IsNullOrEmpty(t.ActionFunction));
        transition.Should().NotBeNull("the test asset must have a transition with an action function");

        // Drive the dispatcher with a transition selection.
        var facet = dispatcher!.GetFacet(new HsmTransitionSelection(transition!.VisualId));

        // Dispatcher must have written the FQN to the shared context.
        sharedCtx.CurrentActionFqn.Should().Be(fqn,
            "dispatcher.GetFacet must write the transition action FQN to the shared context");
        sharedCtx.CurrentVisualId.Should().NotBeNullOrEmpty(
            "dispatcher.GetFacet must write the VisualId to the shared context");

        // The facet must be a TransitionFacet with the correct action.
        facet.Should().BeOfType<TransitionFacet>();
        ((TransitionFacet)facet!).ActionFunction.Should().Be(fqn);

        // Verify the drawer filters: extract the HsmBlackboardFieldPickerDrawer from the composite.
        var composite = drawerMap[typeof(string)] as HsmCompositeStringDrawer;
        composite.Should().NotBeNull("the drawer map must contain an HsmCompositeStringDrawer for string");

        var editNode  = MakeNodeWithAttr(new HsmBlackboardFieldPickerAttribute());
        var bbDrawer  = composite!.Resolve(editNode) as HsmBlackboardFieldPickerDrawer;
        bbDrawer.Should().NotBeNull("composite must dispatch to HsmBlackboardFieldPickerDrawer");

        // The key assertion: only float var is returned, not int var.
        var items = bbDrawer!.GetItems();
        items.Should().ContainSingle("only the float variable is compatible with the float DtoType");
        items[0].Should().Be("floatVar",
            "only the float-typed variable must be returned via the shared context");
    }

    /// <summary>
    /// Verify that WITHOUT a shared context (null) the drawer falls back to ALL variables.
    /// This documents the pre-BB1D failure mode.
    /// </summary>
    [Fact]
    public void NoContext_Hsm_DrawerReturnsAllVars()
    {
        const string fqn = "Ns.HsmFloatAction";

        var asset = MakeHsmAssetWithTransitionAction(fqn,
            new BlackboardVariableEntry("floatVar", typeof(float), null),
            new BlackboardVariableEntry("intVar",   typeof(int),   null));

        var exporter = new StubExporterForHsmBB1D();
        exporter.Register(fqn, typeof(float));

        // No shared context — old behavior.
        var drawerMap = HsmPickerDrawerFactory.BuildDrawers(asset, exporter, null);

        var composite = drawerMap[typeof(string)] as HsmCompositeStringDrawer;
        var editNode  = MakeNodeWithAttr(new HsmBlackboardFieldPickerAttribute());
        var bbDrawer  = composite!.Resolve(editNode) as HsmBlackboardFieldPickerDrawer;

        // Without context, all vars returned.
        bbDrawer!.GetItems().Should().HaveCount(2, "without context all variables are shown");
    }

    /// <summary>
    /// After selecting a non-transition element (e.g. a state), the context FQN is cleared
    /// and the drawer falls back to showing all variables.
    /// </summary>
    [Fact]
    public void SharedContext_Hsm_ClearedForStateSelection_DrawerShowsAllVars()
    {
        const string fqn = "Ns.HsmFloatAction";

        var asset = MakeHsmAssetWithTransitionAction(fqn,
            new BlackboardVariableEntry("floatVar", typeof(float), null),
            new BlackboardVariableEntry("intVar",   typeof(int),   null));

        var exporter  = new StubExporterForHsmBB1D();
        exporter.Register(fqn, typeof(float));

        var sharedCtx  = new HsmFacetFqnContext();
        var dispatcher = HsmSelectionBridgeHelper.BuildFacetDispatcher(asset, sharedCtx);
        var drawerMap  = HsmPickerDrawerFactory.BuildDrawers(asset, exporter, sharedCtx);

        // Prime the FQN by selecting the transition.
        var transition = asset.AllTransitions.First(t => !string.IsNullOrEmpty(t.ActionFunction));
        dispatcher!.GetFacet(new HsmTransitionSelection(transition.VisualId));
        sharedCtx.CurrentActionFqn.Should().Be(fqn);

        // Now select a state — the dispatcher must clear the FQN.
        var idleState = asset.AllStates.First(s => s.Name == "Idle");
        dispatcher.GetFacet(new HsmStateSelection(idleState.StableId));
        sharedCtx.CurrentActionFqn.Should().BeNull(
            "selecting a state must clear CurrentActionFqn in the shared context");

        // Drawer must now show all vars.
        var composite = drawerMap[typeof(string)] as HsmCompositeStringDrawer;
        var editNode  = MakeNodeWithAttr(new HsmBlackboardFieldPickerAttribute());
        var bbDrawer  = composite!.Resolve(editNode) as HsmBlackboardFieldPickerDrawer;
        bbDrawer!.GetItems().Should().HaveCount(2, "cleared FQN returns all variables");
    }

    /// <summary>
    /// BuildFacetDispatcher(asset: null, ctx) returns null and does not throw.
    /// </summary>
    [Fact]
    public void BuildFacetDispatcher_WithContext_NullAsset_ReturnsNull()
    {
        var ctx = new HsmFacetFqnContext();
        HsmSelectionBridgeHelper.BuildFacetDispatcher(null, ctx).Should().BeNull();
    }
}
