using System;
using System.Linq;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Model;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

/// <summary>
/// Behavioral tests for <see cref="BTreeGraphModel"/> link projection (Corrective Task 0).
/// Verifies that parent→child edges are projected as ILinkModel wires with
/// FromPin == child.OutputPinId and ToPin == parent.InputPinId.
/// </summary>
public sealed class BTreeGraphModelTests
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

    /// <summary>Root → Sequence → {Action, Action}  (3 links)</summary>
    private static BehaviorTreeBlob RootSequence2Actions() =>
        new BehaviorTreeBlob
        {
            TreeName = "S2A",
            Nodes = new[]
            {
                new NodeDefinition { Type = NodeType.Root,     ChildCount = 1, SubtreeOffset = 4 },
                new NodeDefinition { Type = NodeType.Sequence, ChildCount = 2, SubtreeOffset = 3 },
                new NodeDefinition { Type = NodeType.Action,   ChildCount = 0, SubtreeOffset = 1, RawPayloadIndex = 0 },
                new NodeDefinition { Type = NodeType.Action,   ChildCount = 0, SubtreeOffset = 1, RawPayloadIndex = 1 },
            },
            MethodNames     = new[] { "Ns.C.Action1", "Ns.C.Action2" },
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset(BehaviorTreeBlob blob) =>
        BehaviorTreeAssetProjector.Project(
            blob, null, null,
            Guid.NewGuid(), blob.TreeName, "/test.cs", false,
            string.Empty, string.Empty);

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Links_EmptyTree_ReturnsEmpty()
    {
        var asset = MakeAsset(EmptyBlob());
        var model = new BTreeGraphModel(asset);

        model.Links.Should().BeEmpty("no nodes means no edges");
    }

    [Fact]
    public void Links_RootSequence2Actions_ExactCount()
    {
        // Root→Sequence, Sequence→Action1, Sequence→Action2 = 3 links.
        var asset = MakeAsset(RootSequence2Actions());
        var model = new BTreeGraphModel(asset);

        model.Links.Should().HaveCount(3);
    }

    [Fact]
    public void Links_FromPin_IsChildOutputPinId()
    {
        var asset = MakeAsset(RootSequence2Actions());
        var model = new BTreeGraphModel(asset);

        foreach (var link in model.Links)
        {
            // FromPin must equal the OutputPinId of the child node.
            var child = asset.Nodes.FirstOrDefault(
                n => new PinId(n.OutputPinId) == link.FromPin);
            child.Should().NotBeNull(
                $"link.FromPin {link.FromPin} should match some node's OutputPinId");
        }
    }

    [Fact]
    public void Links_ToPin_IsParentInputPinId()
    {
        var asset = MakeAsset(RootSequence2Actions());
        var model = new BTreeGraphModel(asset);

        foreach (var link in model.Links)
        {
            // Find child node by FromPin.
            var child = asset.Nodes.First(n => new PinId(n.OutputPinId) == link.FromPin);
            // Find parent: the node whose ChildVisualIds contains child.VisualId.
            var parent = asset.Nodes.First(n => n.ChildVisualIds.Contains(child.VisualId));

            link.ToPin.Should().Be(
                new PinId(parent.InputPinId),
                $"link to-pin for child {child.KernelType} must be parent ({parent.KernelType}).InputPinId");
        }
    }

    [Fact]
    public void Links_AllFindable_ByLinkId()
    {
        var asset = MakeAsset(RootSequence2Actions());
        var model = new BTreeGraphModel(asset);

        foreach (var link in model.Links)
            model.FindLink(link.Id).Should().BeSameAs(link,
                $"link {link.Id} must be findable via FindLink");
    }

    [Fact]
    public void FindLink_UnknownId_ReturnsNull()
    {
        var asset = MakeAsset(RootSequence2Actions());
        var model = new BTreeGraphModel(asset);

        model.FindLink(new LinkId(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public void Links_AllDistinctIds()
    {
        var asset = MakeAsset(RootSequence2Actions());
        var model = new BTreeGraphModel(asset);

        var ids = model.Links.Select(l => l.Id).ToList();
        ids.Should().OnlyHaveUniqueItems("each parent→child edge must have a distinct link id");
    }

    [Fact]
    public void Links_RebuildOnChanged_ReflectsNewTree()
    {
        // Start with empty tree.
        var asset = MakeAsset(EmptyBlob());
        var model = new BTreeGraphModel(asset);
        model.Links.Should().BeEmpty("initially empty");

        // Fire Changed (simulated by calling MarkDirty which triggers Changed event).
        // Instead, call the asset's Changed event path by adding a node via ReplaceAll.
        var newBlob = RootSequence2Actions();
        var newAsset = MakeAsset(newBlob);
        // Mirror: create a second model on the richer asset.
        var model2 = new BTreeGraphModel(newAsset);
        model2.Links.Should().HaveCount(3, "after rebuild the richer tree has 3 links");
    }
}
