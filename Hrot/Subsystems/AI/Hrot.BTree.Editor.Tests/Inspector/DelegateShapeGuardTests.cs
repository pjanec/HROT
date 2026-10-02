using System;
using Hrot.Editor.AiShared;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector.ActionBinding;
using Hrot.Editor.AiShared.Selection;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Inspector;

/// <summary>
/// Fix 1 — DelegateShape guard: headless tests proving that the binding drawer's "no compatible variables" state and
/// the Promote affordance are suppressed for <see cref="BTreeActionDelegateShape.NoParams"/> (whole-blackboard)
/// actions, and still work normally for <see cref="BTreeActionDelegateShape.ThreeParamReusable"/> actions.
/// ⭐ <c>CE-417</c> slice 4b: the shape reaches the drawer ON the binding facet (<c>TargetsWholeBlackboard</c>), set by the
/// mapper — not through the retired <c>BTreeFacetFqnContext</c>.
/// </summary>
public sealed class DelegateShapeGuardTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed class StubExporter : IActionSchemaExporter
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

    private static BehaviorTreeAsset MakeAsset(params BlackboardVariableEntry[] vars)
    {
        var blob = new BehaviorTreeBlob
        {
            TreeName        = "T",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
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

    private static BehaviorTreeAsset MakeAssetWithAction(
        string fqn,
        BTreeActionDelegateShape shape,
        out Guid actionVisualId)
    {
        var blob = new BehaviorTreeBlob
        {
            TreeName        = "T",
            Nodes           = new[]
            {
                new NodeDefinition { Type = NodeType.Root,   ChildCount = 1, SubtreeOffset = 2 },
                new NodeDefinition { Type = NodeType.Action, ChildCount = 0, SubtreeOffset = 1, RawPayloadIndex = 0 },
            },
            MethodNames     = new[] { fqn },
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };
        var asset = BehaviorTreeAssetProjector.Project(
            blob, null, null, Guid.NewGuid(), "T", "/t.cs", false, "", "");

        // Patch the DelegateShape on the projected node's payload.
        var actionNode = asset.Nodes.First(n => n.KernelType == NodeType.Action);
        actionNode.DelegateShape = shape;
        actionVisualId = actionNode.VisualId;
        return asset;
    }

    private static BlackboardVariableEntry Var(string name, Type t) =>
        new BlackboardVariableEntry(name, t, null);

    private static ActionBindingSources Sources(BehaviorTreeAsset asset, IActionSchemaExporter exporter)
        => new(asset, _ => Array.Empty<string>(), exporter);

    private static BehaviorActionBindingFacet Binding(string fqn, bool wholeBlackboard) =>
        new() { MethodFqn = fqn, SiteId = Guid.NewGuid().ToString(), TargetsWholeBlackboard = wholeBlackboard };

    private static readonly ActionSchemaEntry FloatAction =
        new("Ns.FloatAction", typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite);
    private static readonly ActionSchemaEntry WanderAction =
        new("Ns.WanderAction", typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite);

    // ── HasNoCompatibleVariables for ThreeParamReusable ───────────────────────

    [Fact]
    public void HasNoCompatibleVariables_True_WhenThreeParamReusable_AndNoMatchingVars()
    {
        var asset = MakeAsset(Var("intVar", typeof(int)));  // int var, but action needs float

        Sources(asset, new StubExporter(FloatAction)).HasNoCompatibleVariables(Binding("Ns.FloatAction", false))
            .Should().BeTrue("ThreeParamReusable with no matching vars should show the Promote affordance");
    }

    [Fact]
    public void HasNoCompatibleVariables_False_WhenThreeParamReusable_AndMatchingVarExists()
    {
        var asset = MakeAsset(Var("floatVar", typeof(float)));

        Sources(asset, new StubExporter(FloatAction)).HasNoCompatibleVariables(Binding("Ns.FloatAction", false))
            .Should().BeFalse("matching var exists — Promote affordance should not appear");
    }

    // ── HasNoCompatibleVariables suppressed for a binding with no variable (FourParamFull → NoParams, CE-504) ──

    [Fact]
    public void HasNoCompatibleVariables_False_WhenFourParamFull_EvenWithNoMatchingVars()
    {
        var asset = MakeAsset(Var("intVar", typeof(int)));  // no float match

        Sources(asset, new StubExporter(WanderAction)).HasNoCompatibleVariables(Binding("Ns.WanderAction", true))
            .Should().BeFalse(
                "FourParamFull operates on the full blackboard — no per-DTO binding, so Promote must be suppressed");
    }

    [Fact]
    public void HasNoCompatibleVariables_False_WhenFourParamFull_EvenWithZeroVarsInAsset()
    {
        var asset = MakeAsset();   // no vars at all

        Sources(asset, new StubExporter(WanderAction)).HasNoCompatibleVariables(Binding("Ns.WanderAction", true))
            .Should().BeFalse("FourParamFull should never trigger Promote regardless of blackboard contents");
    }

    [Fact]
    public void Promote_CreatesNothing_WhenFourParamFull()
    {
        var asset = MakeAsset();

        Sources(asset, new StubExporter(WanderAction)).Promote(Binding("Ns.WanderAction", true)).Should().BeNull();
        asset.BlackboardVariables.Should().BeEmpty("a whole-blackboard binding has no per-binding variable to create");
    }

    // ── Mapper puts the shape on the binding facet ───────────────────────────

    [Fact]
    public void Mapper_MarksTheBinding_NoVariable_ForAParamLessAction()
    {
        var asset  = MakeAssetWithAction("Ns.WanderAction", BTreeActionDelegateShape.NoParams, out var nodeVisualId);

        var facet = (BTreeActionFacet)new BTreeFacetMapper(asset).GetFacet(new BTreeNodeSelection(nodeVisualId))!;

        facet.Action.TargetsWholeBlackboard.Should().BeTrue(
            "mapper must carry the node's NoParams shape (CE-504: FourParamFull retired) to the binding the drawer draws");
    }

    [Fact]
    public void Mapper_DoesNotMarkTheBinding_ForThreeParamReusableAction()
    {
        var asset  = MakeAssetWithAction("Ns.FloatAction", BTreeActionDelegateShape.ThreeParamReusable, out var nodeVisualId);

        var facet = (BTreeActionFacet)new BTreeFacetMapper(asset).GetFacet(new BTreeNodeSelection(nodeVisualId))!;

        facet.Action.TargetsWholeBlackboard.Should().BeFalse("ThreeParamReusable binds its own variable");
    }
}
