using FluentAssertions;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using System;
using Xunit;

namespace NodeEditor.Core.Tests.View;

/// <summary>Smoke tests for HoverKind.Attachment and HoverInfo.Attachment field.</summary>
public sealed class AttachmentHitTestTests
{
    [Fact]
    public void HoverInfo_WithAttachmentKind_StoresId()
    {
        var id = AttachmentId.NewId();
        var info = new HoverInfo { Kind = HoverKind.Attachment, Attachment = id };

        info.Kind.Should().Be(HoverKind.Attachment);
        info.Attachment.Should().Be(id);
    }

    [Fact]
    public void HoverInfo_None_HasEmptyAttachment()
    {
        var info = HoverInfo.None;

        info.Kind.Should().Be(HoverKind.None);
        info.Attachment.Should().Be(AttachmentId.Empty);
    }

    [Fact]
    public void HoverKind_HasAttachmentValue()
    {
        var names = Enum.GetNames<HoverKind>();
        names.Should().Contain("Attachment");
    }
}
