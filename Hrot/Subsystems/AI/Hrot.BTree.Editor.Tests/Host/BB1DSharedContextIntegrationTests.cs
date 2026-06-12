using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using Fdp.Toolkit.Behavior;
using FluentAssertions;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Selection;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Host;

// ── Stub exporter ─────────────────────────────────────────────────────────────

file sealed class StubExporterForBB1D : IActionSchemaExporter
{
    private readonly Dictionary<string, ActionSchemaEntry> _map = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ActionSchemaEntry> All => _map;
    public event Action? Changed { add { } remove { } }

    public void Register(string fqn, Type dtoType)
        => _map[fqn] = new ActionSchemaEntry(fqn, dtoType, ActionHosting.BTree, BlackboardAccess.ReadWrite, null);

    public ActionSchemaEntry? Lookup(string fqn) => _map.GetValueOrDefault(fqn);
    public void Rebuild() { }
}

/// <summary>
/// BB1D integration tests: verify that a SINGLE <see cref="BTreeFacetFqnContext"/>
/// shared between <see cref="BTreeSelectionBridgeHelper.BuildFacetDispatcher(BehaviorTreeAsset?, BTreeFacetFqnContext?)"/>
/// (writer) and <see cref="BTreePickerDrawerFactory.BuildDrawers"/> (reader) causes the
/// <see cref="BlackboardFieldPickerDrawer"/> to return ONLY the type-compatible variables.
///
/// <para>This is the test that would have caught the BB1D gap: if the context is NOT shared,
/// the drawer receives a null or disconnected context and shows ALL variables instead of the
/// filtered set.</para>
/// </summary>
public sealed class BB1DSharedContextIntegrationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a BTree asset with one Action node bound to <paramref name="methodFqn"/>
    /// and populates the blackboard with variables of the given types.
    /// </summary>
    private static BehaviorTreeAsset MakeBTreeAsset(
        string methodFqn,
        params BlackboardVariableEntry[] vars)
    {
        var blob = new BehaviorTreeBlob
        {
            TreeName        = "BB1DTest",
            Nodes           = new[]
            {
                new NodeDefinition { Type = NodeType.Root,   ChildCount = 1, SubtreeOffset = 2 },
                new NodeDefinition { Type = NodeType.Action, ChildCount = 0, SubtreeOffset = 1, RawPayloadIndex = 0 },
            },
            MethodNames     = new[] { methodFqn },
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };
        var asset = BehaviorTreeAssetProjector.Project(
            blob, null, null, Guid.NewGuid(), "BB1DTest", "/bb1d.cs", false, "", "");
        if (vars.Length > 0)
            asset.SetBlackboardVariables(vars);
        return asset;
    }

    private static BehaviorRegistry EmptyRegistry() => new BehaviorRegistry();

    // ── Core integration test: shared context causes type-filtering ───────────

    /// <summary>
    /// CRITICAL: The BB1D wiring test.
    ///
    /// Build the BTree dispatcher via BuildFacetDispatcher(asset, ctx) and the drawers via
    /// BuildDrawers(asset, registry, exporter, ctx) using the SAME ctx instance.
    /// Drive dispatcher.GetFacet(actionNodeSelection) which writes CurrentActionFqn to ctx.
    /// Then assert the BlackboardFieldPickerDrawer returns ONLY the T-typed variable,
    /// not the U-typed one.
    ///
    /// If the context is NOT shared (old code before BB1D), the drawer has no FQN context
    /// and falls back to returning ALL variables — the test would fail.
    /// </summary>
    [Fact]
    public void SharedContext_BTree_DispatcherWritesFqn_DrawerFiltersToTType()
    {
        const string fqn = "Ns.FloatAction";

        // Asset with two blackboard vars: one float (compatible with T=float) and one int (incompatible).
        var asset = MakeBTreeAsset(fqn,
            new BlackboardVariableEntry("floatVar", typeof(float), null),
            new BlackboardVariableEntry("intVar",   typeof(int),   null));

        var exporter = new StubExporterForBB1D();
        exporter.Register(fqn, typeof(float));

        var registry = EmptyRegistry();

        // ONE shared context — this is the key to BB1D.
        var sharedCtx = new BTreeFacetFqnContext();

        // Build dispatcher (writer) and drawers (reader) with the SAME sharedCtx.
        var dispatcher = BTreeSelectionBridgeHelper.BuildFacetDispatcher(asset, sharedCtx);
        var drawerMap  = BTreePickerDrawerFactory.BuildDrawers(asset, registry, exporter, sharedCtx);

        dispatcher.Should().NotBeNull("dispatcher must be built from a non-null asset");

        // Find the action node and drive the dispatcher.
        var actionNode = asset.Nodes.First(n => n.KernelType == NodeType.Action);
        var facet = dispatcher!.GetFacet(new BTreeNodeSelection(actionNode.VisualId));

        // Dispatcher must have written the FQN to the shared context.
        sharedCtx.CurrentActionFqn.Should().Be(fqn,
            "dispatcher.GetFacet must write the action FQN to the shared context");
        sharedCtx.CurrentNodeVisualId.Should().NotBeNullOrEmpty(
            "dispatcher.GetFacet must write the VisualId to the shared context");

        // The facet must be a BTreeActionFacet with the correct FQN.
        facet.Should().BeOfType<BTreeActionFacet>();
        ((BTreeActionFacet)facet!).MethodFqn.Should().Be(fqn);

        // Now verify the drawer filters: extract the BlackboardFieldPickerDrawer from the composite.
        var composite = drawerMap[typeof(string)] as CompositeStringDrawer;
        composite.Should().NotBeNull("the drawer map must contain a CompositeStringDrawer for string");

        // Build a synthetic EditNode with BlackboardFieldPickerAttribute to resolve the drawer.
        var editNode = MakeNodeWithAttr(new BlackboardFieldPickerAttribute());
        var bbDrawer = composite!.Resolve(editNode) as BlackboardFieldPickerDrawer;
        bbDrawer.Should().NotBeNull("composite must dispatch to BlackboardFieldPickerDrawer for BlackboardFieldPickerAttribute");

        // The key assertion: only float var is returned, not int var.
        var items = bbDrawer!.GetItems();
        items.Should().ContainSingle(
            "only the float variable is compatible with the float DtoType — the shared context wrote the action FQN so the drawer can filter");
        items[0].Should().Be("floatVar",
            "only the float-typed variable must be returned");
    }

    /// <summary>
    /// Verify that WITHOUT a shared context (null) the drawer falls back to ALL variables
    /// (this is the pre-BB1D behavior).  This documents the failure mode and ensures
    /// the context-less path still works.
    /// </summary>
    [Fact]
    public void NoContext_BTree_DrawerReturnsAllVars()
    {
        const string fqn = "Ns.FloatAction";

        var asset = MakeBTreeAsset(fqn,
            new BlackboardVariableEntry("floatVar", typeof(float), null),
            new BlackboardVariableEntry("intVar",   typeof(int),   null));

        var exporter = new StubExporterForBB1D();
        exporter.Register(fqn, typeof(float));

        var registry = EmptyRegistry();

        // No shared context — old behavior.
        var drawerMap = BTreePickerDrawerFactory.BuildDrawers(asset, registry, exporter, null);

        var composite = drawerMap[typeof(string)] as CompositeStringDrawer;
        var editNode  = MakeNodeWithAttr(new BlackboardFieldPickerAttribute());
        var bbDrawer  = composite!.Resolve(editNode) as BlackboardFieldPickerDrawer;

        // Without context, fqnAccessor is null → all vars returned (no filtering).
        var items = bbDrawer!.GetItems();
        items.Should().HaveCount(2, "without context all variables are shown (no filtering)");
    }

    /// <summary>
    /// Context is cleared when a non-action node is selected (e.g. Root or Sequence node).
    /// The drawer should then show all variables.
    /// </summary>
    [Fact]
    public void SharedContext_BTree_ClearedForNonActionNode_DrawerShowsAllVars()
    {
        const string fqn = "Ns.FloatAction";

        var asset = MakeBTreeAsset(fqn,
            new BlackboardVariableEntry("floatVar", typeof(float), null),
            new BlackboardVariableEntry("intVar",   typeof(int),   null));

        var exporter = new StubExporterForBB1D();
        exporter.Register(fqn, typeof(float));

        var registry  = EmptyRegistry();
        var sharedCtx = new BTreeFacetFqnContext();

        var dispatcher = BTreeSelectionBridgeHelper.BuildFacetDispatcher(asset, sharedCtx);
        var drawerMap  = BTreePickerDrawerFactory.BuildDrawers(asset, registry, exporter, sharedCtx);

        // First select the action node to prime the FQN.
        var actionNode = asset.Nodes.First(n => n.KernelType == NodeType.Action);
        dispatcher!.GetFacet(new BTreeNodeSelection(actionNode.VisualId));
        sharedCtx.CurrentActionFqn.Should().Be(fqn);

        // Now select the root node — dispatcher must clear the FQN context.
        var rootNode = asset.Nodes.First(n => n.KernelType == NodeType.Root);
        dispatcher.GetFacet(new BTreeNodeSelection(rootNode.VisualId));
        sharedCtx.CurrentActionFqn.Should().BeNull(
            "selecting a non-action node must clear CurrentActionFqn");

        // Drawer must now show all vars (no filtering).
        var composite = drawerMap[typeof(string)] as CompositeStringDrawer;
        var editNode  = MakeNodeWithAttr(new BlackboardFieldPickerAttribute());
        var bbDrawer  = composite!.Resolve(editNode) as BlackboardFieldPickerDrawer;
        bbDrawer!.GetItems().Should().HaveCount(2, "cleared FQN context returns all variables");
    }

    /// <summary>
    /// Accessor helper for BTree-only facets: BTreeActionFacet returns the bound variable
    /// name; BTreeWaitFacet and null both return null.
    /// This mirrors the BTree-specific subset of the EditorSubsystem.ResolveExpressionTargetField helper.
    /// </summary>
    [Fact]
    public void AccessorHelper_BTreeActionFacet_ReturnsBoundVarName()
    {
        const string fqn = "Ns.TestAction";

        var asset = MakeBTreeAsset(fqn);
        var actionNode = asset.Nodes.First(n => n.KernelType == NodeType.Action);
        actionNode.Action!.ExpressionTargetField = "myAutoVar";

        var ctx    = new BTreeFacetFqnContext { CurrentActionFqn = fqn };
        var mapper = new BTreeFacetMapper(asset, ctx);
        var facet  = mapper.GetFacet(new BTreeNodeSelection(actionNode.VisualId));

        // BTree-only accessor (HSM types tested in AiShared.Tests).
        Func<object?, string?> accessor = f => f switch
        {
            BTreeActionFacet af    => af.ExpressionTargetField,
            BTreeConditionFacet cf => cf.ExpressionTargetField,
            _                      => null,
        };

        accessor(facet).Should().Be("myAutoVar",
            "accessor must return ExpressionTargetField from a BTreeActionFacet");
    }

    [Fact]
    public void AccessorHelper_NonActionFacet_ReturnsNull()
    {
        // BTree-only accessor (HSM types tested in AiShared.Tests).
        Func<object?, string?> accessor = f => f switch
        {
            BTreeActionFacet af    => af.ExpressionTargetField,
            BTreeConditionFacet cf => cf.ExpressionTargetField,
            _                      => null,
        };

        accessor(new BTreeWaitFacet { Duration = 1.0f }).Should().BeNull("wait facet has no ETF");
        accessor(null).Should().BeNull("null returns null");
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private static StructEdit.Core.EditNode MakeNodeWithAttr(params Attribute[] attrs)
    {
        var meta = new StructEdit.Core.EditNodeMetadata { CustomAttributes = attrs };
        return new StructEdit.Core.EditNode(
            id:       new StructEdit.Core.EditNodeId(0),
            name:     "Field",
            jsonPath: "$.Field",
            kind:     StructEdit.Core.EditNodeKind.String,
            clrType:  typeof(string),
            metadata: meta);
    }
}
