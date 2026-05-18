using StructEdit.Core;
using StructEdit.Core.Memory;
using StructEdit.Reflection;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace StructEdit.Tests.Reflection;

// ── Test fixtures ─────────────────────────────────────────────────────────────

file struct V3 { public float X; public float Y; public float Z; }

file class FloatArrayComp    { public float[]? Nums { get; set; } }
file class Vec3ArrayComp     { public V3[]? Vecs { get; set; } }
file class IntListComp       { public List<int>? Items { get; set; } }

[InlineArray(4)]
file struct Float4Inline { private float _e0; }
file struct Float4Wrapper { public Float4Inline Data; }

file unsafe struct FixedFloat8 { public fixed float Buf[8]; }

file class EmptyArrayComp { public float[]? Nums { get; set; } }

// ── TASK-CE03: Array element node generation tests ───────────────────────────

public class ArrayElementNodeTests
{
    private static IComponentEditService Service()
        => new ComponentEditServiceBuilder().Build();

    // T-CE03a: primitive array nodes
    // float[] field with 3 elements. After session open, root.Children for that field
    // node has Count==3. Each child has Kind==Scalar, ClrType==float, and
    // Binding.GetBoxed() returns the correct value.
    [Fact]
    public void T_CE03a_PrimitiveArrayNodes_ChildrenAreScalarsWithCorrectValues()
    {
        var comp = new FloatArrayComp { Nums = new float[] { 1.0f, 2.0f, 3.0f } };
        using var session = Service().Open(comp, typeof(FloatArrayComp));

        var numsNode = session.Document.Root.Children.First(c => c.Name == "Nums");
        Assert.Equal(3, numsNode.Children.Count);

        for (int i = 0; i < 3; i++)
        {
            var child = numsNode.Children[i];
            Assert.Equal(EditNodeKind.Scalar, child.Kind);
            Assert.Equal(typeof(float), child.ClrType);
            Assert.Equal((float)(i + 1), child.Binding!.GetBoxed());
        }
    }

    // T-CE03b: struct array nodes
    // V3[] field with 2 elements. Each element child has Kind==Struct and 3 children (X, Y, Z).
    // Calling SetBoxed(9f) on the X child of element 0 and then session.Commit() returns
    // a component whose Vecs[0].X == 9f.
    [Fact]
    public void T_CE03b_StructArrayNodes_MutationPropagatesAfterCommit()
    {
        var comp = new Vec3ArrayComp
        {
            Vecs = new V3[] { new V3 { X = 1f, Y = 2f, Z = 3f }, new V3 { X = 4f, Y = 5f, Z = 6f } }
        };
        using var session = Service().Open(comp, typeof(Vec3ArrayComp));

        var vecsNode = session.Document.Root.Children.First(c => c.Name == "Vecs");

        // Two element children
        Assert.Equal(2, vecsNode.Children.Count);

        var elem0 = vecsNode.Children[0];
        Assert.Equal(EditNodeKind.Struct, elem0.Kind);
        Assert.Equal(3, elem0.Children.Count);

        // Find X child and mutate it
        var xChild = elem0.Children.First(c => c.Name == "X");
        xChild.Binding!.SetBoxed(9f);

        // Commit and verify
        var committed = (Vec3ArrayComp)session.Commit();
        Assert.Equal(9f, committed.Vecs![0].X);
    }

    // T-CE03c: List<T> resize + rebuild
    // List<int> field with 2 elements. Element children have Kind==Scalar.
    // After container.Resize(3) + MarkStructuralChange + RebuildDocument, rebuilt root has 3 element children.
    [Fact]
    public void T_CE03c_ListResize_RebuildProducesNewChildCount()
    {
        var comp = new IntListComp { Items = new List<int> { 10, 20 } };
        using var session = Service().Open(comp, typeof(IntListComp));

        var itemsNode = session.Document.Root.Children.First(c => c.Name == "Items");
        Assert.Equal(2, itemsNode.Children.Count);
        Assert.All(itemsNode.Children, c => Assert.Equal(EditNodeKind.Scalar, c.Kind));

        // Resize and rebuild
        var cb = (IContainerBinding)itemsNode.Binding!;
        cb.Resize(3);
        session.MarkStructuralChange();
        session.RebuildDocument();

        var rebiltItemsNode = session.Document.Root.Children.First(c => c.Name == "Items");
        Assert.Equal(3, rebiltItemsNode.Children.Count);
    }

    // T-CE03d: InlineArray
    // Float4Wrapper struct field with [InlineArray(4)] float element type.
    // node.Children.Count == 4. CanResize == false.
    [Fact]
    public void T_CE03d_InlineArray_FourChildrenAndCannotResize()
    {
        var ops = RuntimeTypeOpsFactory.Get(typeof(Float4Wrapper));
        using var buffer = new NativeStructEditBuffer(typeof(Float4Wrapper), new Float4Wrapper(), ops);
        var builder = new ReflectionEditDocumentBuilder();
        var doc = builder.Build(buffer, typeof(Float4Wrapper), EditScope.WholeComponent, null);

        var dataNode = doc.Root.Children.First(c => c.Name == "Data");
        Assert.Equal(EditNodeKind.InlineArray, dataNode.Kind);
        Assert.Equal(4, dataNode.Children.Count);
        var cb = (IContainerBinding)dataNode.Binding!;
        Assert.False(cb.CanResize);
    }

    // T-CE03e: FixedBuffer
    // fixed float[8] field. node.Children.Count == 8. CanResize == false.
    [Fact]
    public unsafe void T_CE03e_FixedBuffer_EightChildrenAndCannotResize()
    {
        var ops = RuntimeTypeOpsFactory.Get(typeof(FixedFloat8));
        using var buffer = new NativeStructEditBuffer(typeof(FixedFloat8), new FixedFloat8(), ops);
        var builder = new ReflectionEditDocumentBuilder();
        var doc = builder.Build(buffer, typeof(FixedFloat8), EditScope.WholeComponent, null);

        var bufNode = doc.Root.Children.First(c => c.Name == "Buf");
        Assert.Equal(EditNodeKind.FixedBuffer, bufNode.Kind);
        Assert.Equal(8, bufNode.Children.Count);
        var cb = (IContainerBinding)bufNode.Binding!;
        Assert.False(cb.CanResize);
    }

    // T-CE03f: empty array
    // float[] field is null or empty. Node children count is 0. No exception thrown.
    [Fact]
    public void T_CE03f_NullOrEmptyArray_ZeroChildrenNoException()
    {
        // null array
        var compNull = new EmptyArrayComp { Nums = null };
        using var sessionNull = Service().Open(compNull, typeof(EmptyArrayComp));
        var nullNode = sessionNull.Document.Root.Children.First(c => c.Name == "Nums");
        Assert.Equal(0, nullNode.Children.Count);

        // empty array
        var compEmpty = new EmptyArrayComp { Nums = Array.Empty<float>() };
        using var sessionEmpty = Service().Open(compEmpty, typeof(EmptyArrayComp));
        var emptyNode = sessionEmpty.Document.Root.Children.First(c => c.Name == "Nums");
        Assert.Equal(0, emptyNode.Children.Count);
    }
}
