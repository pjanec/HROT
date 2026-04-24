using StructEdit.Core;
using StructEdit.Core.Bindings;
using StructEdit.Core.Memory;
using System.Numerics;
using System.Reflection;

namespace StructEdit.Tests.Reflection;

// ── Test fixtures ─────────────────────────────────────────────────────────────

file struct Vec3 { public float X; public float Y; public float Z; }

file class NamedObject
{
    public string? Name { get; set; }
}

// ── TASK-CE01: NestedMemberBinding tests ─────────────────────────────────────

public class NestedMemberBindingTests
{
    // T-CE01a: struct mutation propagated through parent
    // Given a DynamicArrayBinding of Vec3[] with one element, wrap element 0's binding
    // in a NestedMemberBinding for the X field. Call SetBoxed(9f).
    // Verify that the parent container's first element has X == 9f.
    [Fact]
    public void T_CE01a_StructMutation_IsPropagatedThroughParent()
    {
        // Arrange
        var arr = new Vec3[] { new Vec3 { X = 1f, Y = 2f, Z = 3f } };
        var owner = new Vec3Holder { Data = arr };
        var prop = typeof(Vec3Holder).GetProperty("Data")!;
        var parentBinding = new ManagedPropertyBinding(prop, owner);
        var cb = new DynamicArrayBinding(arr, parentBinding, typeof(Vec3));

        var elemBinding = cb.GetElementBinding(0);
        var fiX = typeof(Vec3).GetField("X")!;
        var nestedBinding = new NestedMemberBinding(fiX, elemBinding);

        // Act
        nestedBinding.SetBoxed(9f);

        // Assert: read back through the parent binding, not the nested binding directly
        var container = (Vec3[])parentBinding.GetBoxed()!;
        Assert.Equal(9f, container[0].X);
    }

    // T-CE01b: class mutation (no parent re-push needed) - GetBoxed returns correct value
    // Wrap a ManagedFieldBinding holding a reference type, verify mutation works.
    [Fact]
    public void T_CE01b_ClassMutation_GetBoxedReturnsNewValue()
    {
        // Arrange
        var obj = new NamedObject { Name = "original" };
        var field = typeof(NamedObjectWrapper).GetField("Inner")!;
        var ownerWrapper = new NamedObjectWrapper { Inner = obj };
        var parentBinding = new ManagedFieldBinding(field, ownerWrapper);
        var piName = typeof(NamedObject).GetProperty("Name")!;
        var nestedBinding = new NestedMemberBinding(piName, parentBinding);

        // Act
        nestedBinding.SetBoxed("hello");

        // Assert
        Assert.Equal("hello", nestedBinding.GetBoxed());
        Assert.Equal("hello", obj.Name);
    }

    // T-CE01c: null parent returns null without throwing
    [Fact]
    public void T_CE01c_NullParent_GetBoxedReturnsNull()
    {
        // Arrange: a binding whose parent returns null
        var field = typeof(Vec3).GetField("X")!;
        var nullParent = new NullValueBinding();
        var nestedBinding = new NestedMemberBinding(field, nullParent);

        // Act & Assert: should return null, not throw
        var result = nestedBinding.GetBoxed();
        Assert.Null(result);
    }
}

// ── Helper fixtures ───────────────────────────────────────────────────────────

file class Vec3Holder { public Vec3[]? Data { get; set; } }
file class NamedObjectWrapper { public NamedObject? Inner; }

/// <summary>Stub binding whose GetBoxed always returns null (simulates unset parent).</summary>
file sealed class NullValueBinding : IValueBinding
{
    public Type ValueType => typeof(object);
    public object? GetBoxed() => null;
    public void SetBoxed(object? value) { }
    public bool TryGetSpan(out Span<byte> bytes) { bytes = default; return false; }
}
