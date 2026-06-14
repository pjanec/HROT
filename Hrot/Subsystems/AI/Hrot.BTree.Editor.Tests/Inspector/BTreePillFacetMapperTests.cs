using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Selection;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Inspector;

/// <summary>
/// DEC-04 tests for BTreePillSelection, BTreeFacetMapper pill dispatch,
/// BTreeSelectionBridgeHelper attachment mapping, and BTreePillAttachmentModel.HostProperties.
/// All tests are headless — no ImGui context.
/// </summary>
public sealed class BTreePillFacetMapperTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName        = "test",
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

    /// <summary>
    /// Adds a node and a pill via the command sink, returns the pill's VisualId.
    /// </summary>
    private static (BehaviorTreeAsset asset, BTreeCommandSink sink, Guid nodeVisualId, Guid pillVisualId)
        BuildWithRepeaterPill(int count = 3)
    {
        var asset = MakeAsset();
        var sink  = new BTreeCommandSink(asset, new StubGraph());

        var nodeId = NodeId.NewId();
        var attId  = AttachmentId.NewId();

        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), System.Numerics.Vector2.Zero, null));
        var props = new Dictionary<string, object?>
        {
            ["decoratorType"] = NodeType.Repeater,
            ["intParam"]      = count,
        };
        sink.Apply(new GraphCommand.AddAttachment(
            attId, nodeId, AttachmentCategory.Decorator, "R", $"x{count}", null, 0, props));

        return (asset, sink, nodeId.Value, attId.Value);
    }

    private static (BehaviorTreeAsset asset, BTreeCommandSink sink, Guid nodeVisualId, Guid pillVisualId)
        BuildWithCooldownPill(float duration = 2.5f)
    {
        var asset = MakeAsset();
        var sink  = new BTreeCommandSink(asset, new StubGraph());

        var nodeId = NodeId.NewId();
        var attId  = AttachmentId.NewId();

        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), System.Numerics.Vector2.Zero, null));
        var props = new Dictionary<string, object?>
        {
            ["decoratorType"] = NodeType.Cooldown,
            ["floatParam"]    = duration,
        };
        sink.Apply(new GraphCommand.AddAttachment(
            attId, nodeId, AttachmentCategory.Decorator, "C", $"{duration}s", null, 0, props));

        return (asset, sink, nodeId.Value, attId.Value);
    }

    // ── BTreePillSelection record ─────────────────────────────────────────────

    [Fact]
    public void BTreePillSelection_HasCorrectPillVisualId()
    {
        var id  = Guid.NewGuid();
        var sel = new BTreePillSelection(id);
        sel.PillVisualId.Should().Be(id);
    }

    // ── BTreeSelectionBridgeHelper.MapSelection with attachments ──────────────

    [Fact]
    public void MapSelection_SingleAttachmentSelected_ReturnsBTreePillSelection()
    {
        var (asset, _, _, pillVisualId) = BuildWithRepeaterPill();
        var attId = new AttachmentId(pillVisualId);

        var sel = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfAttachment(attId));

        var result = BTreeSelectionBridgeHelper.MapSelection(sel, asset);

        result.Should().BeOfType<BTreePillSelection>();
        var ps = (BTreePillSelection)result!;
        ps.PillVisualId.Should().Be(pillVisualId);
    }

    [Fact]
    public void MapSelection_SingleNodeSelected_StillReturnsBTreeNodeSelection()
    {
        var (asset, _, nodeVisualId, _) = BuildWithRepeaterPill();
        var nodeId = new NodeId(nodeVisualId);

        var sel = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfNode(nodeId));

        var result = BTreeSelectionBridgeHelper.MapSelection(sel, asset);

        result.Should().BeOfType<BTreeNodeSelection>();
    }

    // ── BTreeFacetMapper.GetFacet for pills ───────────────────────────────────

    [Fact]
    public void GetFacet_RepeaterPill_ReturnsBTreeRepeaterFacet_WithCorrectCount()
    {
        var (asset, _, _, pillVisualId) = BuildWithRepeaterPill(count: 5);
        var mapper = new BTreeFacetMapper(asset);
        var sel    = new BTreePillSelection(pillVisualId);

        var facet = mapper.GetFacet(sel);

        facet.Should().NotBeNull();
        facet.Should().BeOfType<BTreeRepeaterFacet>();
        ((BTreeRepeaterFacet)facet!).Count.Should().Be(5);
    }

    [Fact]
    public void GetFacet_CooldownPill_ReturnsBTreeCooldownFacet_WithCorrectDuration()
    {
        var (asset, _, _, pillVisualId) = BuildWithCooldownPill(duration: 3.0f);
        var mapper = new BTreeFacetMapper(asset);
        var sel    = new BTreePillSelection(pillVisualId);

        var facet = mapper.GetFacet(sel);

        facet.Should().NotBeNull();
        facet.Should().BeOfType<BTreeCooldownFacet>();
        ((BTreeCooldownFacet)facet!).Duration.Should().BeApproximately(3.0f, 0.001f);
    }

    [Fact]
    public void GetFacet_UnknownPillVisualId_ReturnsNull()
    {
        var asset  = MakeAsset();
        var mapper = new BTreeFacetMapper(asset);
        var sel    = new BTreePillSelection(Guid.NewGuid());

        var facet = mapper.GetFacet(sel);

        facet.Should().BeNull("unknown pill id has no matching pill");
    }

    // ── BTreeFacetMapper.ApplyFacet for pills ─────────────────────────────────

    [Fact]
    public void ApplyFacet_RepeaterPill_WritesCountBack()
    {
        var (asset, _, _, pillVisualId) = BuildWithRepeaterPill(count: 3);
        var mapper = new BTreeFacetMapper(asset);
        var sel    = new BTreePillSelection(pillVisualId);

        var newFacet = new BTreeRepeaterFacet { Count = 7, Comment = null, VisualId = pillVisualId.ToString() };
        mapper.ApplyFacet(sel, newFacet);

        asset.FindPill(pillVisualId)!.IntParam.Should().Be(7);
    }

    [Fact]
    public void ApplyFacet_RepeaterPill_MarksDirty()
    {
        var (asset, _, _, pillVisualId) = BuildWithRepeaterPill(count: 1);
        asset.ClearDirty(); // reset after initial add
        var mapper = new BTreeFacetMapper(asset);
        var sel    = new BTreePillSelection(pillVisualId);

        mapper.ApplyFacet(sel, new BTreeRepeaterFacet { Count = 4, Comment = null, VisualId = pillVisualId.ToString() });

        asset.IsDirty.Should().BeTrue("ApplyFacet must mark dirty");
    }

    [Fact]
    public void ApplyFacet_CooldownPill_WritesDurationBack()
    {
        var (asset, _, _, pillVisualId) = BuildWithCooldownPill(duration: 1.0f);
        var mapper = new BTreeFacetMapper(asset);
        var sel    = new BTreePillSelection(pillVisualId);

        mapper.ApplyFacet(sel, new BTreeCooldownFacet { Duration = 9.9f, Comment = null, VisualId = pillVisualId.ToString() });

        asset.FindPill(pillVisualId)!.FloatParam.Should().BeApproximately(9.9f, 0.001f);
    }

    [Fact]
    public void ApplyFacet_RepeaterPill_CommentPersists()
    {
        var (asset, _, _, pillVisualId) = BuildWithRepeaterPill(count: 1);
        var mapper = new BTreeFacetMapper(asset);
        var sel    = new BTreePillSelection(pillVisualId);

        mapper.ApplyFacet(sel, new BTreeRepeaterFacet { Count = 2, Comment = "my note", VisualId = pillVisualId.ToString() });

        asset.FindPill(pillVisualId)!.Comment.Should().Be("my note");
    }

    // ── BTreePillAttachmentModel.HostProperties ────────────────────────────────

    [Fact]
    public void BTreePillAttachmentModel_HostProperties_ContainsDecoratorType()
    {
        var (asset, _, nodeVisualId, pillVisualId) = BuildWithRepeaterPill(count: 3);
        var nodeId = new NodeId(nodeVisualId);

        // BTreeGraphModel exposes attachment via GetAttachmentsForNode.
        var graphModel = new BTreeGraphModel(asset);
        var attachments = graphModel.GetAttachmentsForNode(nodeId);
        var model = attachments.FirstOrDefault(a => a.Id.Value == pillVisualId);
        model.Should().NotBeNull();

        model!.HostProperties.Should().NotBeNull();
        model.HostProperties!.ContainsKey("decoratorType").Should().BeTrue();
        model.HostProperties["decoratorType"].Should().Be(NodeType.Repeater);
    }

    [Fact]
    public void BTreePillAttachmentModel_HostProperties_ContainsIntParam()
    {
        var (asset, _, nodeVisualId, pillVisualId) = BuildWithRepeaterPill(count: 5);
        var nodeId = new NodeId(nodeVisualId);

        var graphModel = new BTreeGraphModel(asset);
        var model = graphModel.GetAttachmentsForNode(nodeId)
            .First(a => a.Id.Value == pillVisualId);

        model.HostProperties!["intParam"].Should().Be(5);
    }

    // ── stub helpers ──────────────────────────────────────────────────────────

    private sealed class StubGraph : IGraphModel
    {
        public GraphId Id => GraphId.NewId();
        public string DisplayName => "test";
        public GraphKindDescriptor Kind => new("test", "test", false, false);
        public IReadOnlyCollection<INodeModel>    Nodes    => Array.Empty<INodeModel>();
        public IReadOnlyCollection<ILinkModel>    Links    => Array.Empty<ILinkModel>();
        public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();
        public INodeModel?  FindNode(NodeId id)  => null;
        public IPinModel?   FindPin(PinId id)    => null;
        public ILinkModel?  FindLink(LinkId id)  => null;
#pragma warning disable CS0067
        public event Action<GraphChangeNotification>? Changed;
#pragma warning restore CS0067
    }
}
