using StructEdit.Core;
using StructEdit.Core.Attributes;
using StructEdit.Core.Memory;
using StructEdit.Reflection;
using System.Runtime.CompilerServices;

namespace StructEdit.Tests.Reflection;

// ── Test fixtures ─────────────────────────────────────────────────────────────

file struct ScalarStruct { public int X; }

file enum TestStatus { Active, Inactive }
file struct EnumStruct { public TestStatus Status; }

file struct BoolStruct { public bool Active; }

[InlineArray(3)]
file struct TestFloat3 { private float _e0; }
file struct InlineArrayStruct { public TestFloat3 Data; }

file unsafe struct FixedBufStruct { public fixed byte Buf[8]; }

file class DynArrayClass { public List<int> Items { get; set; } = new(); }

file record TestRecord(int X, float Y);

file struct TwoFieldStruct { public int X; public int Y; }
file struct ThreeFieldStruct { public int X; public int Y; public int Z; }

file struct MetaStruct
{
    [EditRange(0, 100)] public int X;
}

file struct SeqIdStruct { public int A; public int B; public int C; }

file struct Vector3Struct { public float X; public float Y; public float Z; }
file struct NestedStruct { public Vector3Struct Pos; }

file class CircularClass { public CircularClass? Next { get; set; } }

// ── TASK-T002 additional fixtures ─────────────────────────────────────────────

file class StringPropClass { public string? Label { get; set; } }
file class InnerRefClass { public int Value { get; set; } }
file class ClassRefHolder { public InnerRefClass? Inner { get; set; } }
file class ClassBoolPropClass { public bool IsEnabled { get; set; } }
file class MetaPropertyClass
{
    [EditRange(0.0, 10.0)]
    public float Speed { get; set; }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class BufferHelper
{
    public static NativeStructEditBuffer NativeFor<T>(T value) where T : unmanaged
    {
        var ops = RuntimeTypeOpsFactory.Get(typeof(T));
        return new NativeStructEditBuffer(typeof(T), value, ops);
    }
}

// ── TASK-R003: Document builder tests ─────────────────────────────────────────

public class DocumentBuilderTests
{
    private static readonly ReflectionEditDocumentBuilder Builder = new();

    // 1 — Scalar primitive
    [Fact]
    public void Build_ScalarPrimitive_RootIsStructWithScalarChild()
    {
        using var buf = BufferHelper.NativeFor(new ScalarStruct { X = 7 });
        var doc = Builder.Build(buf, typeof(ScalarStruct), EditScope.WholeComponent, null);

        Assert.Equal(EditNodeKind.Struct, doc.Root.Kind);
        var child = Assert.Single(doc.Root.Children);
        Assert.Equal("X", child.Name);
        Assert.Equal(EditNodeKind.Scalar, child.Kind);
    }

    // 2 — Enum field
    [Fact]
    public void Build_EnumField_ChildKindIsEnum()
    {
        using var buf = BufferHelper.NativeFor(new EnumStruct { Status = TestStatus.Active });
        var doc = Builder.Build(buf, typeof(EnumStruct), EditScope.WholeComponent, null);

        var child = Assert.Single(doc.Root.Children);
        Assert.Equal(EditNodeKind.Enum, child.Kind);
    }

    // 3 — Boolean field
    [Fact]
    public void Build_BoolField_ChildKindIsBoolean()
    {
        using var buf = BufferHelper.NativeFor(new BoolStruct { Active = true });
        var doc = Builder.Build(buf, typeof(BoolStruct), EditScope.WholeComponent, null);

        var child = Assert.Single(doc.Root.Children);
        Assert.Equal(EditNodeKind.Boolean, child.Kind);
    }

    // 4 — InlineArray field
    [Fact]
    public void Build_InlineArrayField_ChildKindIsInlineArrayWithCount3()
    {
        using var buf = BufferHelper.NativeFor(new InlineArrayStruct());
        var doc = Builder.Build(buf, typeof(InlineArrayStruct), EditScope.WholeComponent, null);

        var child = Assert.Single(doc.Root.Children);
        Assert.Equal(EditNodeKind.InlineArray, child.Kind);
        var container = (IContainerBinding)child.Binding!;
        Assert.Equal(3, container.Count);
    }

    // 5 — FixedBuffer field
    [Fact]
    public unsafe void Build_FixedBufferField_ChildKindIsFixedBufferWithCount8()
    {
        using var buf = BufferHelper.NativeFor(new FixedBufStruct());
        var doc = Builder.Build(buf, typeof(FixedBufStruct), EditScope.WholeComponent, null);

        var child = Assert.Single(doc.Root.Children);
        Assert.Equal(EditNodeKind.FixedBuffer, child.Kind);
        var container = (IContainerBinding)child.Binding!;
        Assert.Equal(8, container.Count);
    }

    // 6 — DynamicArray field
    [Fact]
    public void Build_DynamicArrayField_ChildKindIsDynamicArray()
    {
        var buffer = new ManagedObjectEditBuffer(typeof(DynArrayClass), new DynArrayClass());
        var doc = Builder.Build(buffer, typeof(DynArrayClass), EditScope.WholeComponent, null);

        var child = Assert.Single(doc.Root.Children);
        Assert.Equal(EditNodeKind.DynamicArray, child.Kind);
    }

    // 7 — Record type
    [Fact]
    public void Build_Record_TwoScalarChildren()
    {
        var buffer = new ManagedObjectEditBuffer(typeof(TestRecord), new TestRecord(1, 2.0f));
        var doc = Builder.Build(buffer, typeof(TestRecord), EditScope.WholeComponent, null);

        Assert.Equal(EditNodeKind.Record, doc.Root.Kind);
        Assert.Equal(2, doc.Root.Children.Count);
        Assert.All(doc.Root.Children, c => Assert.Equal(EditNodeKind.Scalar, c.Kind));
    }

    // 8 — Scope single field
    [Fact]
    public void Build_ScopeSingleField_OnlyXVisible()
    {
        using var buf = BufferHelper.NativeFor(new TwoFieldStruct { X = 1, Y = 2 });
        var scope = EditScope.ForField("$.X");
        var doc = Builder.Build(buf, typeof(TwoFieldStruct), scope, null);

        Assert.Equal("X", doc.Root.Name);
        Assert.Empty(doc.Root.Children);
        Assert.Equal(EditNodeKind.Scalar, doc.Root.Kind);
    }

    // 9 — Scope multi-field
    [Fact]
    public void Build_ScopeMultiField_XAndYVisibleZAbsent()
    {
        using var buf = BufferHelper.NativeFor(new ThreeFieldStruct { X = 1, Y = 2, Z = 3 });
        var scope = EditScope.ForFields("$.X", "$.Y");
        var doc = Builder.Build(buf, typeof(ThreeFieldStruct), scope, null);

        Assert.Equal(EditNodeKind.SelectionRoot, doc.Root.Kind);
        Assert.Equal(2, doc.Root.Children.Count);
        Assert.Contains(doc.Root.Children, c => c.Name == "X");
        Assert.Contains(doc.Root.Children, c => c.Name == "Y");
        Assert.DoesNotContain(doc.Root.Children, c => c.Name == "Z");
    }

    // 10 — EditNodeMetadata from attribute
    [Fact]
    public void Build_MetadataAttribute_MinAndMaxSet()
    {
        using var buf = BufferHelper.NativeFor(new MetaStruct { X = 50 });
        var doc = Builder.Build(buf, typeof(MetaStruct), EditScope.WholeComponent, null);

        var child = Assert.Single(doc.Root.Children);
        Assert.Equal(0.0, child.Metadata.Min);
        Assert.Equal(100.0, child.Metadata.Max);
    }

    // 11 — EditNodeId sequential (DFS post-order: children before parent)
    [Fact]
    public void Build_ThreeFields_ChildIdsAre1_2_3()
    {
        using var buf = BufferHelper.NativeFor(new SeqIdStruct { A = 1, B = 2, C = 3 });
        var doc = Builder.Build(buf, typeof(SeqIdStruct), EditScope.WholeComponent, null);

        var ids = doc.Root.Children.Select(c => c.Id.Value).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, ids);
    }

    // 12 — Nested struct
    [Fact]
    public void Build_NestedStruct_PosIsStructWithThreeScalarChildren()
    {
        using var buf = BufferHelper.NativeFor(new NestedStruct());
        var doc = Builder.Build(buf, typeof(NestedStruct), EditScope.WholeComponent, null);

        var pos = Assert.Single(doc.Root.Children);
        Assert.Equal("Pos", pos.Name);
        Assert.Equal(EditNodeKind.Struct, pos.Kind);
        Assert.Equal(3, pos.Children.Count);
        Assert.All(pos.Children, c => Assert.Equal(EditNodeKind.Scalar, c.Kind));
    }

    // 13 — Circular reference detection
    [Fact]
    public void Build_CircularReference_CircularFieldIsUnsupported()
    {
        var instance = new CircularClass();
        instance.Next = instance; // self-reference
        var buffer = new ManagedObjectEditBuffer(typeof(CircularClass), instance);
        var doc = Builder.Build(buffer, typeof(CircularClass), EditScope.WholeComponent, null);

        var nextChild = Assert.Single(doc.Root.Children);
        Assert.Equal("Next", nextChild.Name);
        Assert.Equal(EditNodeKind.Unsupported, nextChild.Kind);
    }

    // 14 — IncludeParentsForContext
    [Fact]
    public void Build_IncludeParentsForContext_PosReadOnlyAndXEditable()
    {
        using var buf = BufferHelper.NativeFor(new NestedStruct());
        var scope = new EditScope
        {
            IncludedPaths = new[] { EditPath.Parse("$.Pos.X") },
            IncludeChildren = true,
            IncludeParentsForContext = true,
        };
        var doc = Builder.Build(buf, typeof(NestedStruct), scope, null);

        // root Struct (NestedStruct) → child Pos (read-only) → child X (editable)
        var pos = Assert.Single(doc.Root.Children);
        Assert.Equal("Pos", pos.Name);
        Assert.True(pos.IsReadOnly);

        var x = Assert.Single(pos.Children);
        Assert.Equal("X", x.Name);
        Assert.False(x.IsReadOnly);
    }

    // 15 — Scope IncludeChildren=false
    [Fact]
    public void Build_ScopeIncludeChildrenFalse_PosNodePresentButChildrenAbsent()
    {
        using var buf = BufferHelper.NativeFor(new NestedStruct());
        var scope = new EditScope
        {
            IncludedPaths = new[] { EditPath.Parse("$.Pos") },
            IncludeChildren = false,
            IncludeParentsForContext = false,
        };
        var doc = Builder.Build(buf, typeof(NestedStruct), scope, null);

        Assert.Equal("Pos", doc.Root.Name);
        Assert.Equal(EditNodeKind.Struct, doc.Root.Kind);
        Assert.Empty(doc.Root.Children);
    }

    // ── TASK-T002: Additional builder coverage ─────────────────────────────

    // T002-16: String property → kind is String
    [Fact]
    public void Build_StringProperty_KindIsString()
    {
        var buf = new ManagedObjectEditBuffer(typeof(StringPropClass), new StringPropClass { Label = "hello" });
        var doc = Builder.Build(buf, typeof(StringPropClass), EditScope.WholeComponent, null);

        var child = Assert.Single(doc.Root.Children);
        Assert.Equal("Label", child.Name);
        Assert.Equal(EditNodeKind.String, child.Kind);
    }

    // T002-17: Class reference property → kind is Class
    [Fact]
    public void Build_ClassReferenceProperty_KindIsClass()
    {
        var buf = new ManagedObjectEditBuffer(typeof(ClassRefHolder),
            new ClassRefHolder { Inner = new InnerRefClass { Value = 1 } });
        var doc = Builder.Build(buf, typeof(ClassRefHolder), EditScope.WholeComponent, null);

        var child = Assert.Single(doc.Root.Children);
        Assert.Equal("Inner", child.Name);
        Assert.Equal(EditNodeKind.Class, child.Kind);
    }

    // T002-18: Record class → root kind is Record
    [Fact]
    public void Build_RecordClass_RootKindIsRecord()
    {
        var buf = new ManagedObjectEditBuffer(typeof(TestRecord), new TestRecord(1, 2.0f));
        var doc = Builder.Build(buf, typeof(TestRecord), EditScope.WholeComponent, null);

        Assert.Equal(EditNodeKind.Record, doc.Root.Kind);
    }

    // T002-19: Boolean property on a class → kind is Boolean
    [Fact]
    public void Build_BoolProperty_OnClass_KindIsBoolean()
    {
        var buf = new ManagedObjectEditBuffer(typeof(ClassBoolPropClass), new ClassBoolPropClass { IsEnabled = true });
        var doc = Builder.Build(buf, typeof(ClassBoolPropClass), EditScope.WholeComponent, null);

        var child = Assert.Single(doc.Root.Children);
        Assert.Equal("IsEnabled", child.Name);
        Assert.Equal(EditNodeKind.Boolean, child.Kind);
    }

    // T002-20: EditRange attribute on a property → Metadata.Min and Max populated
    [Fact]
    public void Build_EditRangeOnProperty_MetadataMinMaxPopulated()
    {
        var buf = new ManagedObjectEditBuffer(typeof(MetaPropertyClass), new MetaPropertyClass { Speed = 5.0f });
        var doc = Builder.Build(buf, typeof(MetaPropertyClass), EditScope.WholeComponent, null);

        var child = Assert.Single(doc.Root.Children);
        Assert.Equal(0.0, child.Metadata.Min);
        Assert.Equal(10.0, child.Metadata.Max);
    }
}
