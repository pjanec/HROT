using System;
using System.Numerics;
using FluentAssertions;
using Hrot.BTree.Editor.Blackboard;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

public sealed class BlackboardSchemaBuilderTests
{
    // ---- Test struct definitions ----

    private struct EmptyBlackboard { }

#pragma warning disable CS0649, CS0169
    private struct SimpleBlackboard
    {
        public bool Flag;
        public int Count;
        public float Range;
        public Vector3 Position;
        public TestEnum Mode;
        public SubStruct Nested;
        private int _hidden;
        public static int SharedCounter;
    }
#pragma warning restore CS0649, CS0169

    private enum TestEnum { A, B, C }

#pragma warning disable CS0649
    private struct SubStruct { public int X; }
#pragma warning restore CS0649

    // ---- Tests ----

    [Fact]
    public void Build_empty_struct_returns_schema_with_no_fields()
    {
        var schema = BlackboardSchemaBuilder.Build(typeof(EmptyBlackboard));

        schema.StructType.Should().Be(typeof(EmptyBlackboard));
        schema.Fields.Should().BeEmpty();
    }

    [Fact]
    public void Build_struct_with_bool_field_classifies_as_bool()
    {
        var schema = BlackboardSchemaBuilder.Build(typeof(SimpleBlackboard));

        schema.Fields.Should().ContainSingle(f => f.Name == "Flag")
            .Which.Kind.Should().Be(BlackboardFieldKind.Bool);
    }

    [Fact]
    public void Build_struct_with_int_field_classifies_as_numeric()
    {
        var schema = BlackboardSchemaBuilder.Build(typeof(SimpleBlackboard));

        schema.Fields.Should().ContainSingle(f => f.Name == "Count")
            .Which.Kind.Should().Be(BlackboardFieldKind.Numeric);
    }

    [Fact]
    public void Build_struct_with_float_field_classifies_as_numeric()
    {
        var schema = BlackboardSchemaBuilder.Build(typeof(SimpleBlackboard));

        schema.Fields.Should().ContainSingle(f => f.Name == "Range")
            .Which.Kind.Should().Be(BlackboardFieldKind.Numeric);
    }

    [Fact]
    public void Build_struct_with_vector3_field_classifies_as_vector()
    {
        var schema = BlackboardSchemaBuilder.Build(typeof(SimpleBlackboard));

        schema.Fields.Should().ContainSingle(f => f.Name == "Position")
            .Which.Kind.Should().Be(BlackboardFieldKind.Vector);
    }

    [Fact]
    public void Build_struct_with_enum_field_classifies_as_enum()
    {
        var schema = BlackboardSchemaBuilder.Build(typeof(SimpleBlackboard));

        schema.Fields.Should().ContainSingle(f => f.Name == "Mode")
            .Which.Kind.Should().Be(BlackboardFieldKind.Enum);
    }

    [Fact]
    public void Build_struct_with_nested_struct_classifies_as_struct()
    {
        var schema = BlackboardSchemaBuilder.Build(typeof(SimpleBlackboard));

        schema.Fields.Should().ContainSingle(f => f.Name == "Nested")
            .Which.Kind.Should().Be(BlackboardFieldKind.Struct);
    }

    [Fact]
    public void Build_struct_preserves_field_names_and_types()
    {
        var schema = BlackboardSchemaBuilder.Build(typeof(SimpleBlackboard));

        var countField = schema.Fields.Should().ContainSingle(f => f.Name == "Count").Which;
        countField.FieldType.Should().Be(typeof(int));

        var posField = schema.Fields.Should().ContainSingle(f => f.Name == "Position").Which;
        posField.FieldType.Should().Be(typeof(Vector3));
    }

    [Fact]
    public void Build_only_includes_public_instance_fields()
    {
        var schema = BlackboardSchemaBuilder.Build(typeof(SimpleBlackboard));

        // Private field _hidden and static field SharedCounter must not appear.
        schema.Fields.Should().NotContain(f => f.Name == "_hidden");
        schema.Fields.Should().NotContain(f => f.Name == "SharedCounter");

        // The 6 public instance fields must all be present.
        schema.Fields.Should().HaveCount(6);
    }
}
