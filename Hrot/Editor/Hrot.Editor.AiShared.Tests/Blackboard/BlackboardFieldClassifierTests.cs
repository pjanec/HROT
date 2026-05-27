using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Fbt.Kernel;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

// ---------------------------------------------------------------------------
// Fixture struct types for classifier tests
// ---------------------------------------------------------------------------

public struct KnownPrimStruct { public int Count; public float Speed; }

public enum TestEnum { A, B, C }

public struct KnownEnumStruct { public TestEnum MyEnum; }

[BlackboardDtoStruct]
public struct AnnotatedDtoStruct { public int V; }

public struct ExoticStruct { public object? Ref; }

public struct VectorStruct { public Vector3 Dir; }

public struct StructWithEnum { public TestEnum Kind; }

// ---------------------------------------------------------------------------
// Helper to build FieldParseResult fixtures
// ---------------------------------------------------------------------------

public sealed class BlackboardFieldClassifierTests
{
    // Convenience factory for parse results with typical single-line, no-attribute, no-init shape.
    private static FieldParseResult SimpleResult(
        string name,
        string? comment          = null,
        bool isSingleLine        = true,
        bool hasAttribute        = false,
        bool hasInitializer      = false) =>
        new(name, comment, (0, 1), isSingleLine, hasAttribute, hasInitializer);

    private static FieldInfo GetField<T>(string name) =>
        typeof(T).GetField(name, BindingFlags.Public | BindingFlags.Instance)!;

    private static readonly IReadOnlySet<Type> EmptyKnown =
        new HashSet<Type>();

    private static IReadOnlySet<Type> KnownWith(params Type[] types) =>
        new HashSet<Type>(types);

    // -------------------------------------------------------------------------
    // Condition 1+6: single-line declaration
    // -------------------------------------------------------------------------

    [Fact]
    public void Classify_MultiLineDeclare_ReturnsReadOnly_MultiLineReason()
    {
        var parse  = SimpleResult("Count", isSingleLine: false);
        var fi     = GetField<KnownPrimStruct>("Count");
        var result = BlackboardFieldClassifier.Classify(parse, fi, EmptyKnown);

        Assert.Equal(FieldClassification.ReadOnlyPassthrough, result.Classification);
        Assert.Contains("multi-line", result.ReadOnlyReason, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Condition 4: has attribute
    // -------------------------------------------------------------------------

    [Fact]
    public void Classify_HasAttribute_ReturnsReadOnly_AttributeReason()
    {
        var parse  = SimpleResult("Count", hasAttribute: true);
        var fi     = GetField<KnownPrimStruct>("Count");
        var result = BlackboardFieldClassifier.Classify(parse, fi, EmptyKnown);

        Assert.Equal(FieldClassification.ReadOnlyPassthrough, result.Classification);
        Assert.Contains("attribute", result.ReadOnlyReason, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Condition 5: has initializer
    // -------------------------------------------------------------------------

    [Fact]
    public void Classify_HasInitializer_ReturnsReadOnly_InitializerReason()
    {
        var parse  = SimpleResult("Count", hasInitializer: true);
        var fi     = GetField<KnownPrimStruct>("Count");
        var result = BlackboardFieldClassifier.Classify(parse, fi, EmptyKnown);

        Assert.Equal(FieldClassification.ReadOnlyPassthrough, result.Classification);
        Assert.Contains("initializer", result.ReadOnlyReason, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Condition 2: unknown type
    // -------------------------------------------------------------------------

    [Fact]
    public void Classify_UnknownType_ReturnsReadOnly_TypeReason()
    {
        var parse  = SimpleResult("Ref");
        var fi     = GetField<ExoticStruct>("Ref");
        var result = BlackboardFieldClassifier.Classify(parse, fi, EmptyKnown);

        Assert.Equal(FieldClassification.ReadOnlyPassthrough, result.Classification);
        Assert.Contains("unknown type", result.ReadOnlyReason, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Happy-path cases (EditorManaged)
    // -------------------------------------------------------------------------

    [Fact]
    public void Classify_PrimitiveField_ReturnsEditorManaged()
    {
        var parse  = SimpleResult("Count");
        var fi     = GetField<KnownPrimStruct>("Count");
        var result = BlackboardFieldClassifier.Classify(parse, fi, EmptyKnown);

        Assert.Equal(FieldClassification.EditorManaged, result.Classification);
        Assert.Null(result.ReadOnlyReason);
    }

    [Fact]
    public void Classify_FloatField_ReturnsEditorManaged()
    {
        var parse  = SimpleResult("Speed");
        var fi     = GetField<KnownPrimStruct>("Speed");
        var result = BlackboardFieldClassifier.Classify(parse, fi, EmptyKnown);

        Assert.Equal(FieldClassification.EditorManaged, result.Classification);
    }

    [Fact]
    public void Classify_EnumField_ReturnsEditorManaged()
    {
        var parse  = SimpleResult("MyEnum");
        var fi     = GetField<KnownEnumStruct>("MyEnum");
        var result = BlackboardFieldClassifier.Classify(parse, fi, EmptyKnown);

        Assert.Equal(FieldClassification.EditorManaged, result.Classification);
    }

    [Fact]
    public void Classify_BlackboardDtoStructField_ReturnsEditorManaged()
    {
        // AnnotatedDtoStruct carries [BlackboardDtoStruct]
        // We need a host struct that has an AnnotatedDtoStruct field.
        // Use reflection to create the FieldInfo dynamically for this test.
        var hostType = typeof(DtoHostStruct);
        var fi       = hostType.GetField("Nested", BindingFlags.Public | BindingFlags.Instance)!;
        Assert.NotNull(fi);

        var parse  = SimpleResult("Nested");
        var result = BlackboardFieldClassifier.Classify(parse, fi, EmptyKnown);

        Assert.Equal(FieldClassification.EditorManaged, result.Classification);
    }

    [Fact]
    public void Classify_TypeInSchemaKnownSet_ReturnsEditorManaged()
    {
        // ExoticStruct is not normally known, but if it's in knownTypes it becomes EditorManaged.
        var parse    = SimpleResult("Ref");
        var fi       = GetField<ExoticSchemaStruct>("SchemaField");
        var known    = KnownWith(typeof(AnnotatedDtoStruct));
        var nestedFi = typeof(SchemaKnownHost).GetField("Value", BindingFlags.Public | BindingFlags.Instance)!;
        var parse2   = SimpleResult("Value");

        var result = BlackboardFieldClassifier.Classify(parse2, nestedFi, known);

        Assert.Equal(FieldClassification.EditorManaged, result.Classification);
    }

    [Fact]
    public void Classify_VectorField_ReturnsEditorManaged()
    {
        var fi     = GetField<VectorStruct>("Dir");
        var parse  = SimpleResult("Dir");
        var result = BlackboardFieldClassifier.Classify(parse, fi, EmptyKnown);

        Assert.Equal(FieldClassification.EditorManaged, result.Classification);
    }

    [Fact]
    public void Classify_WithDocComment_StillEditorManaged()
    {
        // Condition 3: a /// comment is allowed and must NOT force ReadOnly.
        var parse  = SimpleResult("Count", comment: "/// A count.\n");
        var fi     = GetField<KnownPrimStruct>("Count");
        var result = BlackboardFieldClassifier.Classify(parse, fi, EmptyKnown);

        Assert.Equal(FieldClassification.EditorManaged, result.Classification);
    }

    // -------------------------------------------------------------------------
    // Condition ordering: multi-line checked before attribute
    // -------------------------------------------------------------------------

    [Fact]
    public void Classify_MultiLineAndAttribute_ReturnsReadOnly_MultiLineCheckedFirst()
    {
        // When multiple conditions are violated, the first one reported matters only in that
        // we expect ReadOnlyPassthrough regardless of ordering.
        var parse  = SimpleResult("Count", isSingleLine: false, hasAttribute: true);
        var fi     = GetField<KnownPrimStruct>("Count");
        var result = BlackboardFieldClassifier.Classify(parse, fi, EmptyKnown);

        Assert.Equal(FieldClassification.ReadOnlyPassthrough, result.Classification);
    }
}

// ---------------------------------------------------------------------------
// Additional fixture structs declared at namespace scope
// ---------------------------------------------------------------------------

public struct DtoHostStruct { public AnnotatedDtoStruct Nested; }
public struct ExoticSchemaStruct { public ExoticStruct SchemaField; }
public struct SchemaKnownHost { public AnnotatedDtoStruct Value; }
