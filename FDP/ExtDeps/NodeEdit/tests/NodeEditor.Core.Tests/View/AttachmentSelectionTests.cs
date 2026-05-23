using FluentAssertions;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.Core.Tests.View;

public class AttachmentSelectionTests
{
    [Fact]
    public void OfAttachment_KindIsAttachment()
    {
        var id = AttachmentId.NewId();
        SelectionEntry.OfAttachment(id).Kind.Should().Be(SelectionEntryKind.Attachment);
    }

    [Fact]
    public void OfAttachment_AttachmentPropertySet()
    {
        var id = AttachmentId.NewId();
        SelectionEntry.OfAttachment(id).Attachment.Should().Be(id);
    }

    [Fact]
    public void SelectionState_Attachments_FiltersCorrectly()
    {
        var sel = new SelectionState();
        var nodeId = NodeId.NewId();
        var attachId = AttachmentId.NewId();

        sel.Add(SelectionEntry.OfNode(nodeId));
        sel.Add(SelectionEntry.OfAttachment(attachId));

        var attachments = sel.Attachments.ToList();
        attachments.Should().HaveCount(1);
        attachments[0].Should().Be(attachId);
    }

    [Fact]
    public void Toggle_Attachment()
    {
        var sel = new SelectionState();
        var id = AttachmentId.NewId();
        var entry = SelectionEntry.OfAttachment(id);

        sel.Toggle(entry);
        sel.Contains(entry).Should().BeTrue();

        sel.Toggle(entry);
        sel.Contains(entry).Should().BeFalse();
    }
}
