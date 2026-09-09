using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

// ---------------------------------------------------------------------------
// Fixture types -- the canonical A1 wrapper shape and its near-misses
// ---------------------------------------------------------------------------

[InlineArray(4)]
public struct IntBuf4 { private int _e0; }

/// <summary>The canonical recognized shape: exactly int Count + one [InlineArray] buffer.</summary>
public struct IntList4 { public int Count; public IntBuf4 Items; }

[InlineArray(3)]
public struct Vec3Buf3 { private Vector3 _e0; }

public struct Vec3List3 { public int Count; public Vec3Buf3 Items; }

/// <summary>Field order flipped -- still the same shape.</summary>
public struct FlippedList { public IntBuf4 Items; public int Count; }

/// <summary>Near-miss: an EXTRA field disqualifies the wrapper.</summary>
public struct ListPlusExtra { public int Count; public IntBuf4 Items; public float Extra; }

/// <summary>Near-miss: Count has the wrong type.</summary>
public struct FloatCountList { public float Count; public IntBuf4 Items; }

/// <summary>Near-miss: the "buffer" carries no [InlineArray].</summary>
public struct PlainPair { public int Count; public Vector3 Items; }

[InlineArray(2)]
public struct NestedBuf { private IntList4 _e0; }

/// <summary>Near-miss: nested list element (forbidden in v1).</summary>
public struct NestedList { public int Count; public NestedBuf Items; }

/// <summary>Host DTO: a wrapper field (recognized) + the LOOSE twin-field pattern (passthrough).</summary>
public struct ListHostDto
{
    public IntList4 Waypoints;     // A1 wrapper -> EditorManaged
    public IntBuf4  LooseItems;    // loose buffer field -> passthrough
    public int      LooseCount;
}

/// <summary>
/// FC-3a (Q#21-A1/B1) -- classifier + display-type recognition of the fixed-list wrapper:
/// <list type="bullet">
///   <item><see cref="BlackboardFieldClassifier.TryGetFixedListShape"/> accepts EXACTLY the
///   canonical shape (int Count + one [InlineArray(N)] buffer, order-irrelevant) and rejects
///   every near-miss incl. nested lists;</item>
///   <item>a wrapper FIELD classifies EditorManaged when its element is known; the loose
///   twin-field pattern stays ReadOnlyPassthrough (A1);</item>
///   <item><see cref="BlackboardTypeHelper.GetDisplayName"/> renders <c>List&lt;T&gt;[N]</c>.</item>
/// </list>
/// </summary>
public sealed class FixedListClassifierTests
{
    private static FieldParseResult SimpleResult(string name) =>
        new(name, null, (0, 1), true, false, false);

    private static FieldInfo GetField<T>(string name) =>
        typeof(T).GetField(name, BindingFlags.Public | BindingFlags.Instance)!;

    private static readonly IReadOnlySet<Type> EmptyKnown = new HashSet<Type>();

    // ---- shape recognition --------------------------------------------------

    [Theory]
    [InlineData(typeof(IntList4), typeof(int), 4)]
    [InlineData(typeof(Vec3List3), typeof(Vector3), 3)]
    [InlineData(typeof(FlippedList), typeof(int), 4)]
    public void TryGetFixedListShape_CanonicalShapes_Recognized(Type wrapper, Type expectedElem, int expectedCap)
    {
        Assert.True(BlackboardFieldClassifier.TryGetFixedListShape(wrapper, out var elem, out int cap));
        Assert.Equal(expectedElem, elem);
        Assert.Equal(expectedCap, cap);
    }

    [Theory]
    [InlineData(typeof(ListPlusExtra))]   // extra field
    [InlineData(typeof(FloatCountList))]  // wrong Count type
    [InlineData(typeof(PlainPair))]       // no [InlineArray] buffer
    [InlineData(typeof(NestedList))]      // nested list element (v1 forbidden)
    [InlineData(typeof(IntBuf4))]         // a bare buffer is not a wrapper
    [InlineData(typeof(int))]             // primitives never match
    [InlineData(typeof(Vector3))]         // plain struct never matches
    public void TryGetFixedListShape_NearMisses_Rejected(Type t)
        => Assert.False(BlackboardFieldClassifier.TryGetFixedListShape(t, out _, out _));

    // ---- classification -----------------------------------------------------

    [Fact]
    public void Classify_WrapperField_KnownElement_EditorManaged()
    {
        var result = BlackboardFieldClassifier.Classify(
            SimpleResult("Waypoints"), GetField<ListHostDto>("Waypoints"), EmptyKnown);

        Assert.Equal(FieldClassification.EditorManaged, result.Classification);
        Assert.Null(result.ReadOnlyReason);
    }

    [Fact]
    public void Classify_LooseTwinFieldPattern_StaysPassthrough()
    {
        // A1: the loose Items+Count DTO pattern is NOT recognized -- the bare buffer field
        // classifies as an unknown type (preserved byte-for-byte, not editor-managed).
        var result = BlackboardFieldClassifier.Classify(
            SimpleResult("LooseItems"), GetField<ListHostDto>("LooseItems"), EmptyKnown);

        Assert.Equal(FieldClassification.ReadOnlyPassthrough, result.Classification);
        Assert.Contains("unknown type", result.ReadOnlyReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_WrapperField_UnknownElement_Passthrough()
    {
        // NestedList's element is itself a wrapper -> shape rejected -> unknown type.
        var fi = typeof(NestedListHost).GetField("Bad", BindingFlags.Public | BindingFlags.Instance)!;
        var result = BlackboardFieldClassifier.Classify(SimpleResult("Bad"), fi, EmptyKnown);
        Assert.Equal(FieldClassification.ReadOnlyPassthrough, result.Classification);
    }

    public struct NestedListHost { public NestedList Bad; }

    // ---- display type (the B1 Variables-panel surface) ----------------------

    [Theory]
    [InlineData(typeof(IntList4), "List<int>[4]")]
    [InlineData(typeof(Vec3List3), "List<Vector3>[3]")]
    public void GetDisplayName_Wrapper_RendersListForm(Type wrapper, string expected)
        => Assert.Equal(expected, BlackboardTypeHelper.GetDisplayName(wrapper));

    [Fact]
    public void GetDisplayName_NonWrapperTypes_Unchanged()
    {
        Assert.Equal("int", BlackboardTypeHelper.GetDisplayName(typeof(int)));
        Assert.Equal("Vector3", BlackboardTypeHelper.GetDisplayName(typeof(Vector3)));
        Assert.Equal(nameof(PlainPair), BlackboardTypeHelper.GetDisplayName(typeof(PlainPair)));
    }
}
