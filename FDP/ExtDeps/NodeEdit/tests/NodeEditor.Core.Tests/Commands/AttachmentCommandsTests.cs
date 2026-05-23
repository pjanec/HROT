using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using FluentAssertions;
using Xunit;

namespace NodeEditor.Core.Tests.Commands;

public class AttachmentCommandsTests
{
    [Fact]
    public void AddAttachment_Roundtrip()
    {
        var newId      = AttachmentId.NewId();
        var hostNodeId = NodeId.NewId();
        var props      = new Dictionary<string, object?> { ["speed"] = 3 };

        var cmd = new GraphCommand.AddAttachment(
            newId,
            hostNodeId,
            AttachmentCategory.Decorator,
            "!",
            "Inverter",
            "Inverts result",
            0,
            props);

        cmd.NewId.Should().Be(newId);
        cmd.HostNodeId.Should().Be(hostNodeId);
        cmd.Category.Should().Be(AttachmentCategory.Decorator);
        cmd.Glyph.Should().Be("!");
        cmd.Label.Should().Be("Inverter");
        cmd.Tooltip.Should().Be("Inverts result");
        cmd.StackIndex.Should().Be(0);
        cmd.HostProperties.Should().BeSameAs(props);
    }

    [Fact]
    public void RemoveAttachments_Roundtrip()
    {
        var id1 = AttachmentId.NewId();
        var id2 = AttachmentId.NewId();
        var ids = new List<AttachmentId> { id1, id2 };

        var cmd = new GraphCommand.RemoveAttachments(ids);

        cmd.AttachmentIds.Should().BeEquivalentTo(ids);
    }

    [Fact]
    public void SetAttachmentProperty_Roundtrip()
    {
        var id = AttachmentId.NewId();

        var cmd = new GraphCommand.SetAttachmentProperty(id, "count", 5);

        cmd.Id.Should().Be(id);
        cmd.Key.Should().Be("count");
        cmd.Value.Should().Be(5);
    }

    [Fact]
    public void ReorderAttachments_Roundtrip()
    {
        var hostNodeId = NodeId.NewId();
        var a1 = AttachmentId.NewId();
        var a2 = AttachmentId.NewId();
        var order = new List<AttachmentId> { a2, a1 };

        var cmd = new GraphCommand.ReorderAttachments(hostNodeId, order);

        cmd.HostNodeId.Should().Be(hostNodeId);
        cmd.NewOrder.Should().BeEquivalentTo(order, o => o.WithStrictOrdering());
    }

    [Fact]
    public void MoveAttachment_Roundtrip()
    {
        var id          = AttachmentId.NewId();
        var newHostId   = NodeId.NewId();

        var cmd = new GraphCommand.MoveAttachment(id, newHostId, 2);

        cmd.Id.Should().Be(id);
        cmd.NewHostNodeId.Should().Be(newHostId);
        cmd.NewStackIndex.Should().Be(2);
    }
}
