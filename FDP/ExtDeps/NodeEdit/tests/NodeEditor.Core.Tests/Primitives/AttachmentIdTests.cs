using NodeEditor.Primitives;
using FluentAssertions;
using Xunit;

namespace NodeEditor.Core.Tests.Primitives;

public class AttachmentIdTests
{
    [Fact]
    public void Empty_IsDefault()
    {
        AttachmentId.Empty.Should().Be(default(AttachmentId));
    }

    [Fact]
    public void NewId_GeneratesUniqueId()
    {
        var a = AttachmentId.NewId();
        var b = AttachmentId.NewId();
        a.Should().NotBe(b);
    }

    [Fact]
    public void Equality_SameGuid_Equal()
    {
        var guid = Guid.NewGuid();
        var a = new AttachmentId(guid);
        var b = new AttachmentId(guid);
        a.Should().Be(b);
    }

    [Fact]
    public void Equality_DifferentGuid_NotEqual()
    {
        var a = new AttachmentId(Guid.NewGuid());
        var b = new AttachmentId(Guid.NewGuid());
        a.Should().NotBe(b);
    }
}
