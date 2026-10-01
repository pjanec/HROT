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
using Hrot.Editor.AiShared.Inspector.ActionBinding;
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
        => _map[fqn] = new ActionSchemaEntry(fqn, dtoType, ActionHosting.BTree, BlackboardAccess.ReadWrite);

    public ActionSchemaEntry? Lookup(string fqn) => _map.GetValueOrDefault(fqn);
    public void Rebuild() { }
}

/// <summary>
/// BB1D integration tests: the production dispatcher (<see cref="BTreeSelectionBridgeHelper.BuildFacetDispatcher(BehaviorTreeAsset?)"/>)
/// and the production drawers (<see cref="BTreePickerDrawerFactory.BuildDrawers"/>), wired together, offer ONLY the
/// type-compatible variables for the selected node's binding.
///
/// <para>⭐ <c>CE-417</c> slice 4b: BB1D's gap was a side channel (<c>BTreeFacetFqnContext</c>) that the dispatcher wrote and
/// the drawer read, and that broke when the two were not handed the same instance. The side channel is gone — the
/// facet's binding carries its own method — so the claim is now that the dispatcher's facet, handed to the factory's
/// binding drawer, filters; and that one node's selection cannot leak into another's list.</para>
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

    // ── Core integration test: the dispatcher's binding filters the drawer's list ──

    private static ActionBindingSources BindingSources(IReadOnlyDictionary<Type, Fdp.Presentation.Editing.IImGuiFieldDrawer> drawers)
        => drawers[typeof(BehaviorActionBindingFacet)].Should().BeOfType<ActionBindingDrawer>().Subject.Sources;

    /// <summary>
    /// CRITICAL: The BB1D wiring test. Build the dispatcher and the drawers the way <c>AiFacetPickerBinder</c> does, drive
    /// <c>GetFacet</c> on the action node, and assert the binding drawer offers ONLY the T-typed variable.
    /// </summary>
    [Fact]
    public void Dispatcher_BTree_FacetCarriesTheMethod_DrawerFiltersToTType()
    {
        const string fqn = "Ns.FloatAction";

        // Asset with two blackboard vars: one float (compatible with T=float) and one int (incompatible).
        var asset = MakeBTreeAsset(fqn,
            new BlackboardVariableEntry("floatVar", typeof(float), null),
            new BlackboardVariableEntry("intVar",   typeof(int),   null));

        var exporter = new StubExporterForBB1D();
        exporter.Register(fqn, typeof(float));

        var dispatcher = BTreeSelectionBridgeHelper.BuildFacetDispatcher(asset);
        var drawerMap  = BTreePickerDrawerFactory.BuildDrawers(asset, EmptyRegistry(), exporter);
        dispatcher.Should().NotBeNull("dispatcher must be built from a non-null asset");

        var actionNode = asset.Nodes.First(n => n.KernelType == NodeType.Action);
        var facet = dispatcher!.GetFacet(new BTreeNodeSelection(actionNode.VisualId));

        facet.Should().BeOfType<BTreeActionFacet>();
        var binding = ((BTreeActionFacet)facet!).Action;
        binding.MethodFqn.Should().Be(fqn);
        binding.SiteId.Should().Be(actionNode.VisualId.ToString(), "Promote names the variable after the node");

        // The key assertion: only float var is returned, not int var.
        var items = BindingSources(drawerMap).GetVariables(binding);
        items.Should().ContainSingle("only the float variable is compatible with the float DtoType");
        items[0].Should().Be("floatVar", "only the float-typed variable must be returned");
    }

    /// <summary>A binding with no method yet offers every variable — there is no type to filter by.</summary>
    [Fact]
    public void NoMethodYet_BTree_DrawerReturnsAllVars()
    {
        var asset = MakeBTreeAsset("Ns.FloatAction",
            new BlackboardVariableEntry("floatVar", typeof(float), null),
            new BlackboardVariableEntry("intVar",   typeof(int),   null));
        var exporter = new StubExporterForBB1D();
        exporter.Register("Ns.FloatAction", typeof(float));

        BindingSources(BTreePickerDrawerFactory.BuildDrawers(asset, EmptyRegistry(), exporter))
            .GetVariables(default).Should().HaveCount(2, "no method means no filtering");
    }

    /// <summary>
    /// ⭐ What the old side channel could get wrong: the list for one node must not depend on which node was selected
    /// BEFORE it. Two action nodes with different parameter types, selected in turn, each filter by their own method.
    /// </summary>
    [Fact]
    public void TwoNodes_BTree_EachFiltersByItsOwnMethod_RegardlessOfSelectionOrder()
    {
        var blob = new BehaviorTreeBlob
        {
            TreeName        = "BB1DTwo",
            Nodes           = new[]
            {
                new NodeDefinition { Type = NodeType.Root,     ChildCount = 1, SubtreeOffset = 4 },
                new NodeDefinition { Type = NodeType.Sequence, ChildCount = 2, SubtreeOffset = 3 },
                new NodeDefinition { Type = NodeType.Action,   ChildCount = 0, SubtreeOffset = 1, RawPayloadIndex = 0 },
                new NodeDefinition { Type = NodeType.Action,   ChildCount = 0, SubtreeOffset = 1, RawPayloadIndex = 1 },
            },
            MethodNames     = new[] { "Ns.FloatAction", "Ns.IntAction" },
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };
        var asset = BehaviorTreeAssetProjector.Project(
            blob, null, null, Guid.NewGuid(), "BB1DTwo", "/bb1d.cs", false, "", "");
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("floatVar", typeof(float), null),
            new BlackboardVariableEntry("intVar",   typeof(int),   null),
        });
        var exporter = new StubExporterForBB1D();
        exporter.Register("Ns.FloatAction", typeof(float));
        exporter.Register("Ns.IntAction",   typeof(int));

        var dispatcher = BTreeSelectionBridgeHelper.BuildFacetDispatcher(asset)!;
        var sources    = BindingSources(BTreePickerDrawerFactory.BuildDrawers(asset, EmptyRegistry(), exporter));
        var actions    = asset.Nodes.Where(n => n.KernelType == NodeType.Action).ToArray();

        var first  = ((BTreeActionFacet)dispatcher.GetFacet(new BTreeNodeSelection(actions[0].VisualId))!).Action;
        var second = ((BTreeActionFacet)dispatcher.GetFacet(new BTreeNodeSelection(actions[1].VisualId))!).Action;

        sources.GetVariables(first).Should().Equal(new[] { "floatVar" }, "the first node's list is its own, read after the second was selected");
        sources.GetVariables(second).Should().Equal(new[] { "intVar" });
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

        var mapper = new BTreeFacetMapper(asset);
        var facet  = mapper.GetFacet(new BTreeNodeSelection(actionNode.VisualId));

        // BTree-only accessor (HSM types tested in AiShared.Tests).
        Func<object?, string?> accessor = f => f switch
        {
            BTreeActionFacet af    => af.Action.ExpressionTargetField,
            BTreeConditionFacet cf => cf.Condition.ExpressionTargetField,
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
            BTreeActionFacet af    => af.Action.ExpressionTargetField,
            BTreeConditionFacet cf => cf.Condition.ExpressionTargetField,
            _                      => null,
        };

        accessor(new BTreeWaitFacet { Duration = 1.0f }).Should().BeNull("wait facet has no ETF");
        accessor(null).Should().BeNull("null returns null");
    }
}
