using System;
using System.Collections.Generic;
using Fbt;
using FluentAssertions;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Selection;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Host;

/// <summary>
/// ⭐⭐⭐ <c>CE-504</c> slice 1 (C-1) — the editor derives a node's call shape from the method it binds.
/// 📄 <c>docs/blueprints/DESIGN_BTree_Node_Call_Shapes.md</c> §2 S6, §5.
/// </summary>
public sealed class BTreeCallShapeEditorTests
{
    private const string Stateful = "Hrot.AI.Behaviors.Brains.DemoCounterNodes.Action_AdvanceCursor";
    private const string Plain    = "Hrot.AI.Behaviors.Brains.DemoCounterNodes.Action_IncrementCounter";
    private const string Wander   = "Hrot.AI.Behaviors.Brains.CgfNodes.Action_Wander";

    // ⚠ The reflection source reads LOADED assemblies; touching a type guarantees Hrot.AI.Behaviors is one of them.
    private static readonly Type Anchor = typeof(Hrot.AI.Behaviors.Brains.DemoCounterNodes);

    private static BehaviorTreeBlob EmptyBlob() => new()
    {
        TreeName = "T", Nodes = Array.Empty<NodeDefinition>(), MethodNames = Array.Empty<string>(),
        FloatParams = Array.Empty<float>(), IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
    };

    private static BehaviorTreeAsset AssetWith(string? method, out BTreeEditorNode node)
    {
        _ = Anchor;
        var asset = new BehaviorTreeAsset(Guid.NewGuid(), "T", "/T.cs", true, "BB", "Ctx", EmptyBlob());
        var root  = new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = NodeType.Root };
        node = new BTreeEditorNode
        {
            VisualId = Guid.NewGuid(), KernelType = NodeType.Action, DisplayLabel = "A",
            Action = new BehaviorActionBinding { MethodFqn = method },
        };
        root.ChildVisualIds.Add(node.VisualId);
        asset.ReplaceAll(new List<BTreeEditorNode> { root, node }, new List<BTreeEditorPill>(), EmptyBlob());
        return asset;
    }

    /// <summary>
    /// 🔴🔴 <b>The defect C-1 fixes (S6).</b> A pick never moved the shape, so a stateful method picked in the inspector stayed
    /// <c>ThreeParamReusable</c> and the generator skipped the node (<c>BTREE0002</c>).
    /// <para>✅ Red-proof: drop the derive arm from <c>BTreeFacetMapper.ApplyShape</c> ⇒ this rail reddens.</para>
    /// </summary>
    [Fact]
    public void InspectorPick_OfAStatefulMethod_MakesTheNodeStateful()
    {
        var asset  = AssetWith(Plain, out var node);
        var mapper = new BTreeFacetMapper(asset);
        var sel    = new BTreeNodeSelection(node.VisualId);

        var facet = (BTreeActionFacet)mapper.GetFacet(sel)!;
        facet.Action.MethodFqn = Stateful;
        mapper.ApplyFacet(sel, facet);

        node.DelegateShape.Should().Be(BTreeActionDelegateShape.ThreeParamReusableStateful);
    }

    [Fact]
    public void Open_DerivesEachShapeFromItsMethod()
    {
        var asset = AssetWith(Wander, out var node);
        node.DelegateShape.Should().Be(BTreeActionDelegateShape.ThreeParamReusable, "the default a file now loads with");

        BTreeCallShapeResolver.Resolve(asset, BTreeCallShapes.LoadedAssemblySignatures()).Should().Be(1);

        node.DelegateShape.Should().Be(BTreeActionDelegateShape.FourParamFull);
    }

    [Fact]
    public void Open_KeepsTheShape_OfAMethodItCannotResolve()
    {
        var asset = AssetWith("No.Such.Type.Method", out var node);
        node.DelegateShape = BTreeActionDelegateShape.ThreeParamReusableStateful;

        BTreeCallShapeResolver.Resolve(asset, BTreeCallShapes.LoadedAssemblySignatures()).Should().Be(0);

        node.DelegateShape.Should().Be(BTreeActionDelegateShape.ThreeParamReusableStateful,
            "an editor without the method loaded must not reset the shape to a guess");
    }
}
