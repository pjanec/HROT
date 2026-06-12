using System;
using System.Linq;
using System.Numerics;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Model;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Model;

/// <summary>
/// Headless tests verifying that <see cref="BTreeNodeModel.Category"/>
/// is projected from <see cref="BTreeEditorNode.KernelType"/> per the EB-B mapping table.
/// </summary>
public sealed class BTreeNodeCategoryTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName        = "Empty",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset() =>
        new BehaviorTreeAsset(
            Guid.NewGuid(), "TestTree", "/TestTree.cs", true,
            "BB", "Ctx", EmptyBlob());

    // ── tests ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData((int)NodeType.Root,            (int)NodeCategory.FlowControl)]
    [InlineData((int)NodeType.Sequence,         (int)NodeCategory.FlowControl)]
    [InlineData((int)NodeType.Selector,         (int)NodeCategory.FlowControl)]
    [InlineData((int)NodeType.ObserverSelector, (int)NodeCategory.FlowControl)]
    [InlineData((int)NodeType.Parallel,         (int)NodeCategory.FlowControl)]
    [InlineData((int)NodeType.Action,           (int)NodeCategory.Function)]
    [InlineData((int)NodeType.Wait,             (int)NodeCategory.Function)]
    [InlineData((int)NodeType.Condition,        (int)NodeCategory.Pure)]
    [InlineData((int)NodeType.Subtree,          (int)NodeCategory.Macro)]
    public void Category_MapsFromKernelType(int kernelTypeInt, int expectedCategoryInt)
    {
        var kernelType = (NodeType)kernelTypeInt;
        var expected   = (NodeCategory)expectedCategoryInt;

        var asset = MakeAsset();
        var node = new BTreeEditorNode
        {
            VisualId     = Guid.NewGuid(),
            KernelType   = kernelType,
            DisplayLabel = kernelType.ToString(),
            Position     = Vector2.Zero,
        };
        asset.AddNode(node);

        var graph = new BTreeGraphModel(asset);
        var model = graph.Nodes.Should().ContainSingle().Subject;

        model.Category.Should().Be(expected);
    }
}
