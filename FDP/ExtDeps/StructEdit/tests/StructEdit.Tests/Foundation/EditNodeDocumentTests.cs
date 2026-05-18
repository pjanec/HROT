using FluentAssertions;
using StructEdit.Core;

namespace StructEdit.Tests.Foundation;

public class EditNodeTests
{
    [Fact]
    public void EditNode_Construction_AllPropertiesReturnProvidedValues()
    {
        var id = new EditNodeId(5);
        var metadata = new EditNodeMetadata { Min = 0, Max = 100, Unit = "m" };
        var node = new EditNode(
            id: id,
            name: "Speed",
            jsonPath: "$.Speed",
            kind: EditNodeKind.Scalar,
            clrType: typeof(float),
            binding: null,
            children: null,
            metadata: metadata,
            isReadOnly: true);

        node.Id.Should().Be(id);
        node.Name.Should().Be("Speed");
        node.JsonPath.Should().Be("$.Speed");
        node.Kind.Should().Be(EditNodeKind.Scalar);
        node.ClrType.Should().Be(typeof(float));
        node.Metadata.Min.Should().Be(0);
        node.Metadata.Max.Should().Be(100);
        node.Metadata.Unit.Should().Be("m");
        node.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void EditNode_Children_DefaultsToEmpty()
    {
        var node = new EditNode(
            id: new EditNodeId(1),
            name: "X",
            jsonPath: "$.X",
            kind: EditNodeKind.Scalar,
            clrType: typeof(int));

        node.Children.Should().NotBeNull();
        node.Children.Count.Should().Be(0);
    }

    [Fact]
    public void EditNode_IsReadOnly_DefaultsFalse()
    {
        var node = new EditNode(
            id: new EditNodeId(1),
            name: "X",
            jsonPath: "$.X",
            kind: EditNodeKind.Scalar,
            clrType: typeof(int));

        node.IsReadOnly.Should().BeFalse();
    }
}

public class EditDocumentTests
{
    [Fact]
    public void EditDocument_StoresAllProvidedValues()
    {
        var root = new EditNode(new EditNodeId(0), "Root", "$", EditNodeKind.Struct, typeof(object));
        var scope = EditScope.WholeComponent;
        var doc = new EditDocument(root, typeof(object), scope);

        doc.Root.Should().BeSameAs(root);
        doc.RootComponentType.Should().Be(typeof(object));
        doc.Scope.Should().BeSameAs(scope);
    }

    [Fact]
    public void EditDocument_StoresScope_SameReference()
    {
        var root = new EditNode(new EditNodeId(0), "Root", "$", EditNodeKind.Struct, typeof(string));
        var scope = EditScope.ForField("$.Name");
        var doc = new EditDocument(root, typeof(string), scope);

        doc.Scope.Should().BeSameAs(scope);
    }
}

public class EditNodeMetadataTests
{
    [Fact]
    public void EditNodeMetadata_Empty_AllNullablePropertiesAreNull()
    {
        var m = EditNodeMetadata.Empty;
        m.Min.Should().BeNull();
        m.Max.Should().BeNull();
        m.Unit.Should().BeNull();
        m.FixedLength.Should().BeNull();
        m.DisplayName.Should().BeNull();
    }

    [Fact]
    public void EditNodeMetadata_WithValues_RetainsValues()
    {
        var m = new EditNodeMetadata
        {
            Min = -10,
            Max = 10,
            Unit = "kg",
            FixedLength = 8,
            DisplayName = "Weight",
        };

        m.Min.Should().Be(-10);
        m.Max.Should().Be(10);
        m.Unit.Should().Be("kg");
        m.FixedLength.Should().Be(8);
        m.DisplayName.Should().Be("Weight");
    }
}
