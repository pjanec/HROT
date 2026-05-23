using System.Collections.Generic;
using FluentAssertions;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.Core.Tests.Interfaces;

/// <summary>Tests for GraphChangeNotification.AffectedAttachments (TASK-NEA-03).</summary>
public sealed class GraphChangeNotificationTests
{
    [Fact]
    public void AffectedAttachments_is_null_when_omitted()
    {
        var notif = new GraphChangeNotification(GraphChangeKind.Wholesale, null, null, null, null);

        notif.AffectedAttachments.Should().BeNull();
    }

    [Fact]
    public void AffectedAttachments_holds_provided_set()
    {
        var id  = AttachmentId.NewId();
        var set = new HashSet<AttachmentId> { id };
        var notif = new GraphChangeNotification(GraphChangeKind.AttachmentsModified, null, null, set, null);

        notif.AffectedAttachments.Should().NotBeNull();
        notif.AffectedAttachments!.Should().Contain(id);
    }

    [Fact]
    public void AffectedAttachments_is_null_for_node_move_notification()
    {
        var nodeId = IdGenerator.NewNodeId();
        var notif = new GraphChangeNotification(
            GraphChangeKind.NodesMoved,
            new HashSet<NodeId> { nodeId },
            null,
            null,
            null);

        notif.AffectedAttachments.Should().BeNull();
    }

    [Fact]
    public void AffectedAttachments_supports_multiple_ids()
    {
        var id1 = AttachmentId.NewId();
        var id2 = AttachmentId.NewId();
        var set = new HashSet<AttachmentId> { id1, id2 };
        var notif = new GraphChangeNotification(GraphChangeKind.AttachmentsAdded, null, null, set, null);

        notif.AffectedAttachments!.Should().HaveCount(2);
        notif.AffectedAttachments.Should().Contain(id1);
        notif.AffectedAttachments.Should().Contain(id2);
    }

    [Fact]
    public void AffectedAttachments_is_independent_of_AffectedNodes()
    {
        var nodeId   = IdGenerator.NewNodeId();
        var attachId = AttachmentId.NewId();
        var notif = new GraphChangeNotification(
            GraphChangeKind.Wholesale,
            new HashSet<NodeId> { nodeId },
            null,
            new HashSet<AttachmentId> { attachId },
            null);

        notif.AffectedNodes.Should().Contain(nodeId);
        notif.AffectedAttachments!.Should().Contain(attachId);
    }
}
