using FluentAssertions;
using StructEdit.Core;

namespace StructEdit.Tests.Foundation;

public class EditPathTests
{
    [Fact]
    public void Parse_ValidPath_ReturnsEditPathWithCorrectValue()
    {
        var path = EditPath.Parse("$.Damage");
        path.Value.Should().Be("$.Damage");
    }

    [Fact]
    public void Parse_EmptyString_ThrowsArgumentException()
    {
        var act = () => EditPath.Parse("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_NullString_ThrowsArgumentException()
    {
        var act = () => EditPath.Parse(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ImplicitConversion_FromString_SetsValue()
    {
        EditPath p = "$.X";
        p.Value.Should().Be("$.X");
    }

    [Fact]
    public void Equality_SamePath_AreEqual()
    {
        var a = EditPath.Parse("$.Health");
        var b = EditPath.Parse("$.Health");
        a.Should().Be(b);
    }

    [Fact]
    public void Equality_DifferentPaths_AreNotEqual()
    {
        var a = EditPath.Parse("$.Health");
        var b = EditPath.Parse("$.Damage");
        a.Should().NotBe(b);
    }

    [Fact]
    public void Root_HasDollarValue()
    {
        EditPath.Root.Value.Should().Be("$");
    }
}

public class EditNodeKindTests
{
    [Fact]
    public void EditNodeKind_HasExactly17Values()
    {
        var values = Enum.GetValues<EditNodeKind>();
        values.Length.Should().Be(17);
    }

    [Fact]
    public void EditNodeKind_ContainsAllExpectedValues()
    {
        var values = Enum.GetValues<EditNodeKind>();
        values.Should().Contain(EditNodeKind.SelectionRoot);
        values.Should().Contain(EditNodeKind.Scalar);
        values.Should().Contain(EditNodeKind.Boolean);
        values.Should().Contain(EditNodeKind.String);
        values.Should().Contain(EditNodeKind.Enum);
        values.Should().Contain(EditNodeKind.Guid);
        values.Should().Contain(EditNodeKind.DateTime);
        values.Should().Contain(EditNodeKind.Struct);
        values.Should().Contain(EditNodeKind.Class);
        values.Should().Contain(EditNodeKind.Record);
        values.Should().Contain(EditNodeKind.InlineArray);
        values.Should().Contain(EditNodeKind.FixedBuffer);
        values.Should().Contain(EditNodeKind.DynamicArray);
        values.Should().Contain(EditNodeKind.BufferView);
        values.Should().Contain(EditNodeKind.Union);
        values.Should().Contain(EditNodeKind.Custom);
        values.Should().Contain(EditNodeKind.Unsupported);
    }
}

public class EditNodeIdTests
{
    [Fact]
    public void EditNodeId_Equality_SameValue_AreEqual()
    {
        var a = new EditNodeId(1);
        var b = new EditNodeId(1);
        a.Should().Be(b);
    }

    [Fact]
    public void EditNodeId_Equality_DifferentValue_AreNotEqual()
    {
        var a = new EditNodeId(1);
        var b = new EditNodeId(2);
        a.Should().NotBe(b);
    }
}
