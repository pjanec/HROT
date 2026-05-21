using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Attributes;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// Unit tests for BlueprintDefinition, BlueprintLatentCursor, delegate types,
/// and BlueprintRegistrarAttribute - TASK-RT-002.
/// Covers success criteria SC1-SC6.
/// </summary>
public sealed class BlueprintDefinitionTests
{
    // ---- SC1: Create with required fields, defaults are empty ---------------

    [Fact]
    public void SC1_DefaultDefinition_HasEmptyCollections()
    {
        var def = new BlueprintDefinition
        {
            Name = "Test",
            Kind = BlueprintDispatchKind.Instance,
            StructureHash = 0x1234,
            StateSize = 64
        };

        Assert.Null(def.Tick);
        Assert.Null(def.InitDefault);
        Assert.NotNull(def.EventHandlers);
        Assert.Empty(def.EventHandlers);
        Assert.NotNull(def.StateFields);
        Assert.Empty(def.StateFields);
        Assert.Null(def.StateClrType);
    }

    // ---- SC2: BlueprintLatentCursor is 16 bytes ------------------------------

    [Fact]
    public void SC2_BlueprintLatentCursor_Is16Bytes()
    {
        int size = Unsafe.SizeOf<BlueprintLatentCursor>();
        Assert.Equal(16, size);
    }

    // ---- SC3: BlueprintLatentCursor is unmanaged (compile-time) -------------

    // If BlueprintLatentCursor is not unmanaged, this method won't compile.
    private static void RequiresUnmanaged<T>() where T : unmanaged { }

    [Fact]
    public void SC3_BlueprintLatentCursor_Satisfies_UnmanagedConstraint()
    {
        // If this compiles, the constraint is satisfied at compile time.
        RequiresUnmanaged<BlueprintLatentCursor>();
    }

    // ---- SC4: BlueprintRegistrarAttribute can be applied to a class ---------

    [BlueprintRegistrar]
    private sealed class FakeRegistrar { }

    [Fact]
    public void SC4_BlueprintRegistrarAttribute_AppliesWithoutError()
    {
        var attr = typeof(FakeRegistrar).GetCustomAttribute<BlueprintRegistrarAttribute>();
        Assert.NotNull(attr);
    }

    [Fact]
    public void SC4_BlueprintRegistrarAttribute_IsNotInherited()
    {
        // Inherited = false means subclasses don't inherit the attribute.
        var attrUsage = typeof(BlueprintRegistrarAttribute).GetCustomAttribute<AttributeUsageAttribute>();
        Assert.NotNull(attrUsage);
        Assert.False(attrUsage!.Inherited);
    }

    // ---- SC5: Delegate parameter counts (7 each, per Runtime DD §3.3) -------

    [Fact]
    public void SC5_TickDelegate_Has7Parameters()
    {
        var invoke = typeof(TickDelegate).GetMethod("Invoke");
        Assert.NotNull(invoke);
        Assert.Equal(7, invoke!.GetParameters().Length);
    }

    [Fact]
    public void SC5_EventHandlerDelegate_Has7Parameters()
    {
        var invoke = typeof(EventHandlerDelegate).GetMethod("Invoke");
        Assert.NotNull(invoke);
        Assert.Equal(7, invoke!.GetParameters().Length);
    }

    [Fact]
    public void SC5_InitDefaultDelegate_Has1Parameter()
    {
        var invoke = typeof(InitDefaultDelegate).GetMethod("Invoke");
        Assert.NotNull(invoke);
        Assert.Equal(1, invoke!.GetParameters().Length);
    }

    // ---- SC6: BlueprintDefinition structural equality -----------------------

    [Fact]
    public void SC6_RecordCopy_IsEqual()
    {
        // sealed record Equals() uses shallow property comparison.
        // Use 'with' to clone so collection references are shared → all properties equal.
        var a = new BlueprintDefinition
        {
            Name = "Eq", Kind = BlueprintDispatchKind.Library,
            StructureHash = 42, StateSize = 0
        };
        var b = a with { };  // shallow copy -- same collection references

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void SC6_DifferentDefinitions_AreNotEqual()
    {
        var a = new BlueprintDefinition
        {
            Name = "A", Kind = BlueprintDispatchKind.Library,
            StructureHash = 1, StateSize = 0
        };
        var b = new BlueprintDefinition
        {
            Name = "B", Kind = BlueprintDispatchKind.Library,
            StructureHash = 1, StateSize = 0
        };

        Assert.NotEqual(a, b);
    }
}
