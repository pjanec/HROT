using StructEdit.Core.Bindings;
using StructEdit.Core.Memory;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace StructEdit.Tests.Reflection;

// ─── test fixtures ────────────────────────────────────────────────────────────

file struct IntStruct { public int X; }
file unsafe struct FixedBufStruct { public fixed byte Data[8]; }

[InlineArray(3)]
file struct Float3InlineArray { private float _element0; }
file struct InlineArrayWrapper { public Float3InlineArray Data; }

file class ManagedOwner
{
    public int Value { get; set; }
    public List<int>? Items { get; set; }
    public int[]? Data { get; set; }
}

file record ManagedRecord(int X);

// ─── TASK-R002: Binding tests ─────────────────────────────────────────────────

public class BindingTests
{
    // ── NativeFieldBinding ────────────────────────────────────────────────────

    [Fact]
    public void NativeFieldBinding_Read_ReturnsFieldValue()
    {
        var ops = RuntimeTypeOpsFactory.Get(typeof(IntStruct));
        using var buffer = new NativeStructEditBuffer(typeof(IntStruct), new IntStruct { X = 42 }, ops);
        var binding = new NativeFieldBinding(buffer, 0, Unsafe.SizeOf<int>(), typeof(int));

        Assert.Equal(42, binding.GetBoxed());
    }

    [Fact]
    public void NativeFieldBinding_Write_UpdatesSpanAndGetBoxed()
    {
        var ops = RuntimeTypeOpsFactory.Get(typeof(IntStruct));
        using var buffer = new NativeStructEditBuffer(typeof(IntStruct), new IntStruct { X = 0 }, ops);
        var binding = new NativeFieldBinding(buffer, 0, Unsafe.SizeOf<int>(), typeof(int));

        binding.SetBoxed(99);

        Assert.Equal(99, binding.GetBoxed());
        Assert.True(binding.TryGetSpan(out var span));
        Assert.Equal(99, MemoryMarshal.Read<int>(span));
    }

    // ── FixedBufferBinding ────────────────────────────────────────────────────

    [Fact]
    public unsafe void FixedBufferBinding_ElementAccess_ReadsCorrectByte()
    {
        var ops = RuntimeTypeOpsFactory.Get(typeof(FixedBufStruct));
        var src = new FixedBufStruct();
        src.Data[3] = 0xFF;
        using var buffer = new NativeStructEditBuffer(typeof(FixedBufStruct), src, ops);

        // Data field is at offset 0 in FixedBufStruct; element type = byte, size = 1, count = 8
        var binding = new FixedBufferBinding(buffer, 0, typeof(byte), sizeof(byte), 8);

        var elem = binding.GetElementBinding(3);
        Assert.Equal((byte)0xFF, elem.GetBoxed());
    }

    // ── InlineArrayBinding ────────────────────────────────────────────────────

    [Fact]
    public void InlineArrayBinding_Count_EqualsInlineArrayLength()
    {
        var ops = RuntimeTypeOpsFactory.Get(typeof(InlineArrayWrapper));
        using var buffer = new NativeStructEditBuffer(
            typeof(InlineArrayWrapper),
            new InlineArrayWrapper(),
            ops);

        // InlineArrayWrapper.Data is at offset 0; 3 float elements
        var binding = new InlineArrayBinding(buffer, 0, typeof(float), Unsafe.SizeOf<float>(), 3);

        Assert.Equal(3, binding.Count);
    }

    // ── ManagedPropertyBinding ────────────────────────────────────────────────

    [Fact]
    public void ManagedPropertyBinding_Read_ReturnsPropertyValue()
    {
        var owner = new ManagedOwner { Value = 7 };
        var prop = typeof(ManagedOwner).GetProperty("Value")!;
        var binding = new ManagedPropertyBinding(prop, owner);

        Assert.Equal(7, binding.GetBoxed());
    }

    [Fact]
    public void ManagedPropertyBinding_Write_UpdatesProperty()
    {
        var owner = new ManagedOwner { Value = 0 };
        var prop = typeof(ManagedOwner).GetProperty("Value")!;
        var binding = new ManagedPropertyBinding(prop, owner);

        binding.SetBoxed(99);

        Assert.Equal(99, binding.GetBoxed());
        Assert.Equal(99, owner.Value);
    }

    // ── DynamicArrayBinding — List<T> ─────────────────────────────────────────

    [Fact]
    public void DynamicArrayBinding_ResizeUp_CountIs5AndNewElementsDefault()
    {
        var owner = new ManagedOwner { Items = new List<int> { 1, 2, 3 } };
        var prop = typeof(ManagedOwner).GetProperty("Items")!;
        var parentBinding = new ManagedPropertyBinding(prop, owner);
        var binding = new DynamicArrayBinding(owner.Items!, parentBinding, typeof(int));

        binding.Resize(5);

        Assert.Equal(5, binding.Count);
        Assert.Equal(0, binding.GetElementBinding(3).GetBoxed()); // new elements default
    }

    [Fact]
    public void DynamicArrayBinding_ResizeDown_CountIs1AndFirstElementPreserved()
    {
        var owner = new ManagedOwner { Items = new List<int> { 1, 2, 3 } };
        var prop = typeof(ManagedOwner).GetProperty("Items")!;
        var parentBinding = new ManagedPropertyBinding(prop, owner);
        var binding = new DynamicArrayBinding(owner.Items!, parentBinding, typeof(int));

        binding.Resize(1);

        Assert.Equal(1, binding.Count);
        Assert.Equal(1, binding.GetElementBinding(0).GetBoxed());
    }

    [Fact]
    public void DynamicArrayBinding_ResizeArray_ParentPropertyHoldsNewArray()
    {
        var owner = new ManagedOwner { Data = new int[3] };
        var prop = typeof(ManagedOwner).GetProperty("Data")!;
        var parentBinding = new ManagedPropertyBinding(prop, owner);
        var binding = new DynamicArrayBinding(owner.Data!, parentBinding, typeof(int));

        binding.Resize(5);

        Assert.Equal(5, binding.Count);
        Assert.NotNull(owner.Data);
        Assert.Equal(5, owner.Data!.Length);
    }
}
