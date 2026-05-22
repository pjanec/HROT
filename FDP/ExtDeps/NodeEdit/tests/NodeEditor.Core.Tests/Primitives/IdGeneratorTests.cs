using NodeEditor.Primitives;
using FluentAssertions;
using Xunit;

namespace NodeEditor.Core.Tests.Primitives;

public class IdGeneratorTests
{
    [Fact]
    public void Deterministic_SameInput_ReturnsSameGuid()
    {
        var a = IdGenerator.Deterministic("hello");
        var b = IdGenerator.Deterministic("hello");
        a.Should().Be(b);
    }

    [Fact]
    public void Deterministic_DifferentInput_ReturnsDifferentGuid()
    {
        var a = IdGenerator.Deterministic("hello");
        var b = IdGenerator.Deterministic("world");
        a.Should().NotBe(b);
    }

    [Fact]
    public void NewNodeId_ReturnsNonEmpty()
    {
        var id = IdGenerator.NewNodeId();
        id.Should().NotBe(NodeId.Empty);
    }
}
