using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Selection;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Inspector;

/// <summary>
/// Fix 1 — DelegateShape guard: headless tests proving that
/// <see cref="BlackboardFieldPickerDrawer.HasNoCompatibleVariables"/> and the Promote
/// affordance are suppressed for <see cref="BTreeActionDelegateShape.FourParamFull"/>
/// (whole-blackboard) actions, and still work normally for
/// <see cref="BTreeActionDelegateShape.ThreeParamReusable"/> actions.
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
        actionNode.Action!.DelegateShape = shape;
        actionVisualId = actionNode.VisualId;
        return asset;
    }

    private static BlackboardVariableEntry Var(string name, Type t) =>
        new BlackboardVariableEntry(name, t, null);

    // ── HasNoCompatibleVariables for ThreeParamReusable ───────────────────────

    [Fact]
    public void HasNoCompatibleVariables_True_WhenThreeParamReusable_AndNoMatchingVars()
    {
        var asset    = MakeAsset(Var("intVar", typeof(int)));  // int var, but action needs float
        var entry    = new ActionSchemaEntry("Ns.FloatAction", typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new BTreeFacetFqnContext
        {
            CurrentActionFqn     = "Ns.FloatAction",
            CurrentDelegateShape = BTreeActionDelegateShape.ThreeParamReusable,
        };
        var drawer = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn, ctx);

        drawer.HasNoCompatibleVariables.Should().BeTrue(
            "ThreeParamReusable with no matching vars should show the Promote affordance");
    }

    [Fact]
    public void HasNoCompatibleVariables_False_WhenThreeParamReusable_AndMatchingVarExists()
    {
        var asset    = MakeAsset(Var("floatVar", typeof(float)));
        var entry    = new ActionSchemaEntry("Ns.FloatAction", typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new BTreeFacetFqnContext
        {
            CurrentActionFqn     = "Ns.FloatAction",
            CurrentDelegateShape = BTreeActionDelegateShape.ThreeParamReusable,
        };
        var drawer = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn, ctx);

        drawer.HasNoCompatibleVariables.Should().BeFalse(
            "matching var exists — Promote affordance should not appear");
    }

    // ── HasNoCompatibleVariables suppressed for FourParamFull ────────────────

    [Fact]
    public void HasNoCompatibleVariables_False_WhenFourParamFull_EvenWithNoMatchingVars()
    {
        var asset    = MakeAsset(Var("intVar", typeof(int)));  // no float match
        var entry    = new ActionSchemaEntry("Ns.WanderAction", typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new BTreeFacetFqnContext
        {
            CurrentActionFqn     = "Ns.WanderAction",
            CurrentDelegateShape = BTreeActionDelegateShape.FourParamFull,
        };
        var drawer = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn, ctx);

        drawer.HasNoCompatibleVariables.Should().BeFalse(
            "FourParamFull operates on the full blackboard — no per-DTO binding, so Promote must be suppressed");
    }

    [Fact]
    public void HasNoCompatibleVariables_False_WhenFourParamFull_EvenWithZeroVarsInAsset()
    {
        var asset    = MakeAsset();   // no vars at all
        var entry    = new ActionSchemaEntry("Ns.WanderAction", typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new BTreeFacetFqnContext
        {
            CurrentActionFqn     = "Ns.WanderAction",
            CurrentDelegateShape = BTreeActionDelegateShape.FourParamFull,
        };
        var drawer = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn, ctx);

        drawer.HasNoCompatibleVariables.Should().BeFalse(
            "FourParamFull should never trigger Promote regardless of blackboard contents");
    }

    // ── Mapper sets CurrentDelegateShape ─────────────────────────────────────

    [Fact]
    public void Mapper_SetsCurrentDelegateShape_ForFourParamFullAction()
    {
        const string fqn = "Ns.WanderAction";
        var asset  = MakeAssetWithAction(fqn, BTreeActionDelegateShape.FourParamFull, out var nodeVisualId);
        var ctx    = new BTreeFacetFqnContext();
        var mapper = new BTreeFacetMapper(asset, ctx);

        mapper.GetFacet(new BTreeNodeSelection(nodeVisualId));

        ctx.CurrentDelegateShape.Should().Be(BTreeActionDelegateShape.FourParamFull,
            "mapper must propagate DelegateShape from the node's Action payload to the context");
    }

    [Fact]
    public void Mapper_SetsCurrentDelegateShape_ForThreeParamReusableAction()
    {
        const string fqn = "Ns.FloatAction";
        var asset  = MakeAssetWithAction(fqn, BTreeActionDelegateShape.ThreeParamReusable, out var nodeVisualId);
        var ctx    = new BTreeFacetFqnContext();
        var mapper = new BTreeFacetMapper(asset, ctx);

        mapper.GetFacet(new BTreeNodeSelection(nodeVisualId));

        ctx.CurrentDelegateShape.Should().Be(BTreeActionDelegateShape.ThreeParamReusable,
            "mapper must propagate ThreeParamReusable to context");
    }

    [Fact]
    public void Mapper_ClearsCurrentDelegateShape_ForNonActionNode()
    {
        // Build a tree with root + sequence so we can select a non-action node.
        var blob = new BehaviorTreeBlob
        {
            TreeName        = "T",
            Nodes           = new[]
            {
                new NodeDefinition { Type = NodeType.Root,     ChildCount = 1, SubtreeOffset = 2 },
                new NodeDefinition { Type = NodeType.Sequence, ChildCount = 0, SubtreeOffset = 1 },
            },
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };
        var asset   = BehaviorTreeAssetProjector.Project(
            blob, null, null, Guid.NewGuid(), "T", "/t.cs", false, "", "");
        var seqNode = asset.Nodes.First(n => n.KernelType == NodeType.Sequence);

        var ctx = new BTreeFacetFqnContext
        {
            CurrentActionFqn     = "SomePrevious.Fqn",
            CurrentDelegateShape = BTreeActionDelegateShape.FourParamFull,
        };
        var mapper = new BTreeFacetMapper(asset, ctx);

        mapper.GetFacet(new BTreeNodeSelection(seqNode.VisualId));

        ctx.CurrentDelegateShape.Should().BeNull(
            "mapper must clear CurrentDelegateShape when a non-action/condition node is selected");
        ctx.CurrentActionFqn.Should().BeNull(
            "mapper must also clear CurrentActionFqn for non-action nodes");
    }
}
