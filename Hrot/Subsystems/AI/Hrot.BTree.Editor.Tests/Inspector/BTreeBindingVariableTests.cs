using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using FluentAssertions;
using Fdp.Toolkit.Behavior;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector.ActionBinding;
using Hrot.Editor.AiShared.Selection;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Inspector;

// ── Stubs ─────────────────────────────────────────────────────────────────────

file sealed class StubExporter : IActionSchemaExporter
{
    private readonly Dictionary<string, ActionSchemaEntry> _map;

    public IReadOnlyDictionary<string, ActionSchemaEntry> All => _map;
    public event Action? Changed { add { } remove { } }

    public StubExporter(params ActionSchemaEntry[] entries)
    {
        _map = new Dictionary<string, ActionSchemaEntry>(StringComparer.Ordinal);
        foreach (var e in entries) _map[e.Fqn] = e;
    }

    public ActionSchemaEntry? Lookup(string fqn) => _map.GetValueOrDefault(fqn);
    public void Rebuild() { }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

/// <summary>
/// B-1 / B-2 on the BTree host: the binding's target-variable list is filtered by the binding's OWN method, and
/// "Promote" creates that binding's variable. ⭐ <c>CE-417</c> slice 4b: the subject is the ONE shared binding drawer the
/// BTree factory registers (it was <c>BlackboardFieldPickerDrawer</c> fed by a <c>BTreeFacetFqnContext</c> side channel).
/// </summary>
public sealed class BTreeBindingVariableTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static BehaviorTreeAsset MakeAsset(params BlackboardVariableEntry[] vars)
        => MakeAsset(withActionMethod: null, vars);

    /// <param name="withActionMethod">When set, the tree is Root → one Action leaf bound to this method.</param>
    private static BehaviorTreeAsset MakeAsset(string? withActionMethod, params BlackboardVariableEntry[] vars)
    {
        var blob = new BehaviorTreeBlob
        {
            TreeName        = "T",
            Nodes           = withActionMethod is null
                ? Array.Empty<NodeDefinition>()
                : new[]
                {
                    new NodeDefinition { Type = NodeType.Root,   ChildCount = 1, SubtreeOffset = 2 },
                    new NodeDefinition { Type = NodeType.Action, ChildCount = 0, SubtreeOffset = 1, RawPayloadIndex = 0 },
                },
            MethodNames     = withActionMethod is null ? Array.Empty<string>() : new[] { withActionMethod },
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };
        var asset = BehaviorTreeAssetProjector.Project(
            blob, null, null, Guid.NewGuid(), "T", "/t.cs", false, "", "");
        if (vars.Length > 0)
            asset.SetBlackboardVariables(vars);
        return asset;
    }

    private static BlackboardVariableEntry Var(string name, Type t) =>
        new BlackboardVariableEntry(name, t, null);

    /// <summary>The sources behind the drawer the PRODUCTION factory registers for the binding facet.</summary>
    private static ActionBindingSources Sources(BehaviorTreeAsset asset, IActionSchemaExporter? exporter = null)
    {
        var drawers = BTreePickerDrawerFactory.BuildDrawers(asset, new BehaviorRegistry(), exporter);
        return drawers[typeof(BehaviorActionBindingFacet)].Should().BeOfType<ActionBindingDrawer>().Subject.Sources;
    }

    private static BehaviorActionBindingFacet Binding(string? fqn, Guid? site = null) =>
        new() { MethodFqn = fqn, SiteId = (site ?? Guid.NewGuid()).ToString() };

    private static readonly ActionSchemaEntry FloatAction =
        new("Ns.FloatAction", typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite);

    // ── B-1: type filtering ───────────────────────────────────────────────────

    [Fact]
    public void Variables_AreOnlyTheCompatibleOnes_ForAKnownMethod()
    {
        var asset = MakeAsset(Var("floatVar", typeof(float)), Var("intVar", typeof(int)));

        Sources(asset, new StubExporter(FloatAction)).GetVariables(Binding("Ns.FloatAction"))
            .Should().ContainSingle().Which.Should().Be("floatVar",
                "only variables matching the action's DtoType (float) should be returned");
    }

    [Fact]
    public void Variables_AreAll_ForAnUnknownMethod()
    {
        var asset = MakeAsset(Var("a", typeof(float)), Var("b", typeof(int)));

        Sources(asset, new StubExporter()).GetVariables(Binding("Unknown.Action"))
            .Should().HaveCount(2, "unknown FQN falls back to showing all variables");
    }

    [Fact]
    public void Variables_AreAll_WhenNoMethodIsPicked()
    {
        var asset = MakeAsset(Var("x", typeof(bool)), Var("y", typeof(float)));

        Sources(asset, new StubExporter(FloatAction)).GetVariables(Binding(null))
            .Should().HaveCount(2, "no method yet falls back to showing all variables");
    }

    [Fact]
    public void Variables_AreAll_WhenNoExporterIsConfigured()
    {
        var asset = MakeAsset(Var("Speed", typeof(float)));

        Sources(asset).GetVariables(Binding("Ns.FloatAction"))
            .Should().Contain("Speed", "without an exporter the drawer shows all variables");
    }

    [Fact]
    public void NoMatchingVariable_IsTheNoCompatibleState()
    {
        var asset   = MakeAsset(Var("intVar", typeof(int)));
        var sources = Sources(asset, new StubExporter(FloatAction));

        sources.GetVariables(Binding("Ns.FloatAction")).Should().BeEmpty("the int variable does not match float DtoType");
        sources.HasNoCompatibleVariables(Binding("Ns.FloatAction")).Should().BeTrue(
            "FQN is known but no variable matches DtoType");
    }

    [Fact]
    public void AnUnknownMethod_IsNotTheNoCompatibleState()
    {
        var asset = MakeAsset(Var("intVar", typeof(int)));

        Sources(asset, new StubExporter()).HasNoCompatibleVariables(Binding("Unknown.Action")).Should().BeFalse(
            "unknown FQN should not trigger the 'promote' affordance");
    }

    // ── the mapper's facet carries the method the drawer filters by (was: FqnContext threading) ──

    [Fact]
    public void TheMappersFacet_CarriesTheMethod_TheDrawerFiltersBy()
    {
        var asset = MakeAsset("Ns.FloatAction", Var("floatVar", typeof(float)), Var("intVar", typeof(int)));
        var node  = asset.Nodes.Single(n => n.KernelType == NodeType.Action);

        var facet = (BTreeActionFacet)new BTreeFacetMapper(asset).GetFacet(new BTreeNodeSelection(node.VisualId))!;

        facet.Action.MethodFqn.Should().Be("Ns.FloatAction");
        facet.Action.SiteId.Should().Be(node.VisualId.ToString(), "Promote names the variable after the node");
        Sources(asset, new StubExporter(FloatAction)).GetVariables(facet.Action)
            .Should().ContainSingle().Which.Should().Be("floatVar");
    }

    // ── B-2: Promote ─────────────────────────────────────────────────────────

    [Fact]
    public void Promote_CreatesAutoVar_WithCorrectNameAndType_AndIsAutoManaged()
    {
        var asset    = MakeAsset(); // no vars
        var visualId = Guid.NewGuid();

        var resultName = Sources(asset, new StubExporter(FloatAction)).Promote(Binding("Ns.FloatAction", visualId));

        resultName.Should().Be($"_auto_{visualId:N}");
        var created = asset.BlackboardVariables.Should().ContainSingle().Subject;
        created.Name.Should().Be($"_auto_{visualId:N}");
        created.FieldType.Should().Be(typeof(float));
        created.IsAutoManaged.Should().BeTrue();
    }

    [Fact]
    public void Promote_Idempotent_WhenVarAlreadyExists()
    {
        var asset    = MakeAsset();
        var sources  = Sources(asset, new StubExporter(
            new ActionSchemaEntry("Ns.IntAction", typeof(int), ActionHosting.BTree, BlackboardAccess.ReadWrite)));
        var visualId = Guid.NewGuid();

        var name1 = sources.Promote(Binding("Ns.IntAction", visualId));
        var name2 = sources.Promote(Binding("Ns.IntAction", visualId)); // same id

        name1.Should().Be(name2, "same visualId produces the same name");
        asset.BlackboardVariables.Should().HaveCount(1, "second promote is idempotent");
    }

    [Fact]
    public void Promote_TwoDifferentVisualIds_CreatesTwoVars()
    {
        var asset   = MakeAsset();
        var sources = Sources(asset, new StubExporter(
            new ActionSchemaEntry("Ns.BoolAction", typeof(bool), ActionHosting.BTree, BlackboardAccess.ReadWrite)));

        sources.Promote(Binding("Ns.BoolAction"));
        sources.Promote(Binding("Ns.BoolAction"));

        asset.BlackboardVariables.Should().HaveCount(2);
    }

    [Fact]
    public void Promote_ReturnsNull_WhenFqnNotResolvable()
    {
        var asset = MakeAsset();

        Sources(asset, new StubExporter()).Promote(Binding("Unknown.Action"))
            .Should().BeNull("unknown FQN means no DtoType to create a var for");
        asset.BlackboardVariables.Should().BeEmpty();
    }

    [Fact]
    public void Promote_ReturnsNull_WhenNoMethodIsPicked()
    {
        var asset = MakeAsset();

        Sources(asset, new StubExporter(FloatAction)).Promote(Binding(null))
            .Should().BeNull("no method means promote cannot determine the type");
    }
}
