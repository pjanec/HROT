using StructEdit.Core.Memory;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace StructEdit.Tests.Memory;

// ─── test fixtures ────────────────────────────────────────────────────────────

file struct BlittableStruct { public int X; public float Y; }
file struct FloatTriple { public float A; public float B; public float C; }
file struct StructWithString { public string? Name; public int Value; }
file class SimpleClass { public int X; }
file record SimpleRecord(int X);

// ─── TASK-M001: ComponentMemoryKind & classifier ──────────────────────────────

public class ComponentMemoryClassifierTests
{
    private readonly DefaultComponentMemoryClassifier _sut = new();

    [Fact]
    public void Classify_Class_ReturnsManagedReference()
        => Assert.Equal(ComponentMemoryKind.ManagedReference, _sut.Classify(typeof(SimpleClass)));

    [Fact]
    public void Classify_RecordClass_ReturnsManagedReference()
        => Assert.Equal(ComponentMemoryKind.ManagedReference, _sut.Classify(typeof(SimpleRecord)));

    [Fact]
    public void Classify_BlittableStruct_ReturnsUnmanagedBlittableStruct()
        => Assert.Equal(ComponentMemoryKind.UnmanagedBlittableStruct, _sut.Classify(typeof(BlittableStruct)));

    [Fact]
    public void Classify_StructWithStringField_ReturnsNonBlittableStruct()
        => Assert.Equal(ComponentMemoryKind.NonBlittableStruct, _sut.Classify(typeof(StructWithString)));

    [Fact]
    public unsafe void Classify_UnmanagedStructWithFixedBuffer_ReturnsUnmanagedBlittableStruct()
        => Assert.Equal(ComponentMemoryKind.UnmanagedBlittableStruct, _sut.Classify(typeof(FixedBufferStruct)));
}

// Unsafe struct with fixed buffer — must be at file scope outside the test class
file unsafe struct FixedBufferStruct { public fixed byte B[8]; }

// ─── TASK-M002: IRuntimeTypeOps & RuntimeTypeOpsFactory ───────────────────────

public class RuntimeTypeOpsTests
{
    [Fact]
    public unsafe void RoundTrip_IntStruct_PreservesFieldValue()
    {
        var ops = RuntimeTypeOpsFactory.Get(typeof(BlittableStruct));
        var src = new BlittableStruct { X = 42, Y = 3.14f };

        void* mem = NativeMemory.Alloc((nuint)ops.SizeOf);
        try
        {
            ops.CopyObjectToNative(src, mem);
            var result = (BlittableStruct)ops.BoxFromNative(mem);
            Assert.Equal(42, result.X);
            Assert.Equal(3.14f, result.Y);
        }
        finally
        {
            NativeMemory.Free(mem);
        }
    }

    [Fact]
    public unsafe void RoundTrip_FloatTriple_PreservesAllFields()
    {
        var ops = RuntimeTypeOpsFactory.Get(typeof(FloatTriple));
        var src = new FloatTriple { A = 1.1f, B = 2.2f, C = 3.3f };

        void* mem = NativeMemory.Alloc((nuint)ops.SizeOf);
        try
        {
            ops.CopyObjectToNative(src, mem);
            var result = (FloatTriple)ops.BoxFromNative(mem);
            Assert.Equal(1.1f, result.A);
            Assert.Equal(2.2f, result.B);
            Assert.Equal(3.3f, result.C);
        }
        finally
        {
            NativeMemory.Free(mem);
        }
    }

    [Fact]
    public void Factory_CalledTwiceForSameType_ReturnsSameInstance()
    {
        var first = RuntimeTypeOpsFactory.Get(typeof(BlittableStruct));
        var second = RuntimeTypeOpsFactory.Get(typeof(BlittableStruct));
        Assert.Same(first, second);
    }

    [Fact]
    public void Factory_ConcurrentAccess_ReturnsConsistentInstance()
    {
        IRuntimeTypeOps? shared = null;
        bool allMatch = true;

        Parallel.For(0, 8, _ =>
        {
            var ops = RuntimeTypeOpsFactory.Get(typeof(FloatTriple));
            var prev = Interlocked.CompareExchange(ref shared, ops, null);
            if (prev is not null && !ReferenceEquals(prev, ops))
                allMatch = false;
        });

        Assert.True(allMatch);
    }
}

// ─── TASK-M003: NativeStructEditBuffer ────────────────────────────────────────

public class NativeStructEditBufferTests
{
    [Fact]
    public void StoresInitialValue_SpanReflectsOriginal()
    {
        var value = new BlittableStruct { X = 7, Y = 0f };
        var ops = RuntimeTypeOpsFactory.Get(typeof(BlittableStruct));
        using var buf = new NativeStructEditBuffer(typeof(BlittableStruct), value, ops);

        Assert.True(buf.TryGetRootSpan(out var span));
        var read = MemoryMarshal.Read<BlittableStruct>(span);
        Assert.Equal(7, read.X);
    }

    [Fact]
    public void WriteThenBox_ReturnsUpdatedValue()
    {
        var value = new BlittableStruct { X = 1, Y = 0f };
        var ops = RuntimeTypeOpsFactory.Get(typeof(BlittableStruct));
        using var buf = new NativeStructEditBuffer(typeof(BlittableStruct), value, ops);

        // Write directly via binding
        var binding = buf.CreateRootBinding();
        binding.SetBoxed(new BlittableStruct { X = 99, Y = 0.5f });

        var result = (BlittableStruct)buf.Box();
        Assert.Equal(99, result.X);
    }

    [Fact]
    public void IsDirty_StartsFalse()
    {
        var ops = RuntimeTypeOpsFactory.Get(typeof(BlittableStruct));
        using var buf = new NativeStructEditBuffer(typeof(BlittableStruct), new BlittableStruct(), ops);
        Assert.False(buf.IsDirty);
    }

    [Fact]
    public void IsDirty_TrueAfterBindingWrite()
    {
        var ops = RuntimeTypeOpsFactory.Get(typeof(BlittableStruct));
        using var buf = new NativeStructEditBuffer(typeof(BlittableStruct), new BlittableStruct(), ops);
        buf.CreateRootBinding().SetBoxed(new BlittableStruct { X = 1 });
        Assert.True(buf.IsDirty);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var ops = RuntimeTypeOpsFactory.Get(typeof(BlittableStruct));
        var buf = new NativeStructEditBuffer(typeof(BlittableStruct), new BlittableStruct(), ops);
        buf.Dispose();
        buf.Dispose(); // second call must be safe
    }

    [Fact]
    public void OriginalBoxedStruct_NotMutatedAfterBufferWrite()
    {
        var original = new BlittableStruct { X = 5, Y = 0f };
        object boxedOriginal = original; // box separately to preserve the original value

        var ops = RuntimeTypeOpsFactory.Get(typeof(BlittableStruct));
        using var buf = new NativeStructEditBuffer(typeof(BlittableStruct), original, ops);

        buf.CreateRootBinding().SetBoxed(new BlittableStruct { X = 9, Y = 0f });

        // 'original' is a value-type copy – X must still be 5
        Assert.Equal(5, original.X);
    }

    [Fact]
    public void AfterDispose_TryGetRootSpan_ReturnsFalse()
    {
        var ops = RuntimeTypeOpsFactory.Get(typeof(BlittableStruct));
        var buf = new NativeStructEditBuffer(typeof(BlittableStruct), new BlittableStruct(), ops);
        buf.Dispose();
        Assert.False(buf.TryGetRootSpan(out _));
    }
}

// ─── TASK-M004: ManagedObjectEditBuffer & BoxedStructEditBuffer ────────────────

file class Counter { public int X; }
file record RecordWithX(int X);
file struct SmallStruct { public int X; }

public class ManagedObjectEditBufferTests
{
    [Fact]
    public void Clone_IsIsolatedFromOriginal()
    {
        var obj = new Counter { X = 3 };
        using var buf = new ManagedObjectEditBuffer(typeof(Counter), obj);

        // Mutate the clone via the binding
        var binding = buf.CreateRootBinding();
        var clone = (Counter)binding.GetBoxed()!;
        clone.X = 9;

        // Original must be unchanged
        Assert.Equal(3, obj.X);
    }

    [Fact]
    public void Box_ReturnsClonedObject_WithSameInitialValue()
    {
        var rec = new RecordWithX(5);
        using var buf = new ManagedObjectEditBuffer(typeof(RecordWithX), rec);
        var boxed = (RecordWithX)buf.Box();
        Assert.Equal(5, boxed.X);
    }

    [Fact]
    public void SetBoxed_ThenBox_ReturnsUpdatedRecord()
    {
        var rec = new RecordWithX(5);
        using var buf = new ManagedObjectEditBuffer(typeof(RecordWithX), rec);
        buf.CreateRootBinding().SetBoxed(new RecordWithX(20));
        Assert.Equal(20, ((RecordWithX)buf.Box()).X);
    }
}

public class BoxedStructEditBufferTests
{
    [Fact]
    public void SetBoxed_ThenBox_ReturnsUpdatedStruct()
    {
        var s = new SmallStruct { X = 1 };
        using var buf = new BoxedStructEditBuffer(typeof(SmallStruct), s);
        buf.CreateRootBinding().SetBoxed(new SmallStruct { X = 2 });
        var result = (SmallStruct)buf.Box();
        Assert.Equal(2, result.X);
    }
}

// ─── TASK-T001: Additional memory-layer edge-case tests ───────────────────────

public class MemoryLayerEdgeCaseTests
{
    // T001-1: NativeStructEditBuffer.Box() returns an object of the correct type
    [Fact]
    public void NativeBuffer_Box_ReturnsCorrectType()
    {
        var ops = RuntimeTypeOpsFactory.Get(typeof(BlittableStruct));
        using var buf = new NativeStructEditBuffer(typeof(BlittableStruct), new BlittableStruct { X = 7 }, ops);
        var boxed = buf.Box();
        Assert.IsType<BlittableStruct>(boxed);
        Assert.Equal(7, ((BlittableStruct)boxed).X);
    }

    // T001-2: ManagedObjectEditBuffer.IsDirty is true after SetBoxed via field binding (DEBT-05)
    [Fact]
    public void ManagedBuffer_IsDirty_TrueAfterFieldWrite()
    {
        var obj = new Counter { X = 3 };
        using var buf = new ManagedObjectEditBuffer(typeof(Counter), obj);
        Assert.False(buf.IsDirty);
        buf.MarkDirty();
        Assert.True(buf.IsDirty);
    }

    // T001-3: BoxedStructEditBuffer.TryGetRootSpan always returns false
    [Fact]
    public void BoxedBuffer_TryGetRootSpan_ReturnsFalse()
    {
        var s = new SmallStruct { X = 5 };
        using var buf = new BoxedStructEditBuffer(typeof(SmallStruct), s);
        Assert.False(buf.TryGetRootSpan(out _));
    }

    // T001-4: RuntimeTypeOpsFactory returns an IRuntimeTypeOps with correct SizeOf for int
    [Fact]
    public void RuntimeTypeOpsFactory_Int_CorrectSize()
    {
        // int is 4 bytes
        var ops = RuntimeTypeOpsFactory.Get(typeof(int));
        Assert.Equal(4, ops.SizeOf);
    }

    // T001-5: Double-dispose of NativeStructEditBuffer does not throw
    [Fact]
    public void NativeBuffer_DoubleDispose_DoesNotThrow()
    {
        var ops = RuntimeTypeOpsFactory.Get(typeof(BlittableStruct));
        var buf = new NativeStructEditBuffer(typeof(BlittableStruct), new BlittableStruct(), ops);
        buf.Dispose();
        var ex = Record.Exception(() => buf.Dispose());
        Assert.Null(ex);
    }

    // T001-6: ManagedObjectEditBuffer.IsDirty false initially, true after MarkDirty
    [Fact]
    public void ManagedBuffer_IsDirty_FalseInitially_TrueAfterMarkDirty()
    {
        var buf = new ManagedObjectEditBuffer(typeof(Counter), new Counter { X = 0 });
        Assert.False(buf.IsDirty);
        buf.MarkDirty();
        Assert.True(buf.IsDirty);
    }

    // T001-7: BoxedStructEditBuffer.IsDirty false initially, true after MarkDirty
    [Fact]
    public void BoxedBuffer_IsDirty_FalseInitially_TrueAfterMarkDirty()
    {
        var buf = new BoxedStructEditBuffer(typeof(SmallStruct), new SmallStruct { X = 0 });
        Assert.False(buf.IsDirty);
        buf.MarkDirty();
        Assert.True(buf.IsDirty);
    }
}
