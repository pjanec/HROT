using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

/// <summary>
/// Tests for <see cref="BlackboardBinPacker"/>.
/// </summary>
public sealed class BlackboardBinPackerTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static BlackboardVariableDescriptor V(string name, Type type) =>
        new(name, type);

    private static PackResult Pack(params BlackboardVariableDescriptor[] vars) =>
        BlackboardBinPacker.Pack(vars);

    // -------------------------------------------------------------------------
    // Single-field cases
    // -------------------------------------------------------------------------

    [Fact]
    public void SingleBool_OffsetZero_SizeOne_TotalOne()
    {
        var result = Pack(V("x", typeof(bool)));

        Assert.Single(result.Variables);
        Assert.Equal(0, result.Variables[0].ByteOffset);
        Assert.Equal(1, result.Variables[0].ByteSize);
        Assert.Equal(1, result.TotalInlineBytes);
    }

    [Fact]
    public void SingleInt_OffsetZero_SizeFour()
    {
        var result = Pack(V("n", typeof(int)));

        Assert.Equal(0, result.Variables[0].ByteOffset);
        Assert.Equal(4, result.Variables[0].ByteSize);
    }

    [Fact]
    public void SingleLong_OffsetZero_SizeEight()
    {
        var result = Pack(V("l", typeof(long)));

        Assert.Equal(0, result.Variables[0].ByteOffset);
        Assert.Equal(8, result.Variables[0].ByteSize);
    }

    [Fact]
    public void SingleFloat_OffsetZero_SizeFour()
    {
        var result = Pack(V("f", typeof(float)));

        Assert.Equal(0, result.Variables[0].ByteOffset);
        Assert.Equal(4, result.Variables[0].ByteSize);
    }

    // -------------------------------------------------------------------------
    // Alignment padding cases
    // -------------------------------------------------------------------------

    [Fact]
    public void BoolThenInt_BoolAtZero_IntAt4_Total8()
    {
        var result = Pack(V("a", typeof(bool)), V("b", typeof(int)));

        Assert.Equal(0, result.Variables[0].ByteOffset); // bool at 0
        Assert.Equal(4, result.Variables[1].ByteOffset); // int aligned to 4
        Assert.Equal(8, result.TotalInlineBytes);
    }

    [Fact]
    public void ByteThenLong_ByteAtZero_LongAt8_Total16()
    {
        var result = Pack(V("a", typeof(byte)), V("b", typeof(long)));

        Assert.Equal(0, result.Variables[0].ByteOffset); // byte at 0
        Assert.Equal(8, result.Variables[1].ByteOffset); // long aligned to 8
        Assert.Equal(16, result.TotalInlineBytes);
    }

    [Fact]
    public void IntThenBool_IntAtZero_BoolAt4_Total5()
    {
        var result = Pack(V("a", typeof(int)), V("b", typeof(bool)));

        Assert.Equal(0, result.Variables[0].ByteOffset);
        Assert.Equal(4, result.Variables[1].ByteOffset);
        Assert.Equal(5, result.TotalInlineBytes);
    }

    [Fact]
    public void TwoInts_SecondAt4_Total8()
    {
        var result = Pack(V("a", typeof(int)), V("b", typeof(int)));

        Assert.Equal(0, result.Variables[0].ByteOffset);
        Assert.Equal(4, result.Variables[1].ByteOffset);
        Assert.Equal(8, result.TotalInlineBytes);
    }

    [Fact]
    public void ShortThenInt_ShortAtZero_IntAt4_Total8()
    {
        var result = Pack(V("a", typeof(short)), V("b", typeof(int)));

        Assert.Equal(0, result.Variables[0].ByteOffset);  // short at 0
        Assert.Equal(4, result.Variables[1].ByteOffset);  // int aligned to 4 (2 -> pad to 4)
        Assert.Equal(8, result.TotalInlineBytes);
    }

    // -------------------------------------------------------------------------
    // Alignment cap: 8 bytes max
    // -------------------------------------------------------------------------

    [Fact]
    public void Vector3_AlignedTo4_SizeIs12()
    {
        // Vector3 is 12 bytes. Marshal.SizeOf gives the unmanaged size.
        // Alignment = min(Marshal.SizeOf(Vector3), 8) = min(12, 8) = 8.
        // But Marshal.SizeOf of System.Numerics.Vector3 is actually 12.
        // With AlignmentCap=8, align = min(12, 8) = 8.
        int size = Marshal.SizeOf<Vector3>();
        var result = Pack(V("v", typeof(Vector3)));

        Assert.Equal(0, result.Variables[0].ByteOffset);
        Assert.Equal(size, result.Variables[0].ByteSize);
    }

    [Fact]
    public void ByteThenVector3_Vector3AlignedTo8()
    {
        int v3Size = Marshal.SizeOf<Vector3>();
        var result = Pack(V("a", typeof(byte)), V("v", typeof(Vector3)));

        // byte at 0; Vector3 alignment = min(v3Size, 8). If v3Size=12, align=8 -> offset=8.
        // If v3Size=12, align becomes 8, so offset rounds 1 up to 8.
        int expectedAlign = Math.Min(v3Size, 8);
        int expectedOffset = (expectedAlign > 0 && 1 % expectedAlign != 0)
            ? expectedAlign - (1 % expectedAlign) + 1 - 1 + 1 // = expectedAlign
            : 1;
        // Simpler: given byte at 0 (size 1), next alignment boundary for Vector3:
        int offsetAfterByte = 1;
        if (expectedAlign > 0 && offsetAfterByte % expectedAlign != 0)
            offsetAfterByte += expectedAlign - (offsetAfterByte % expectedAlign);

        Assert.Equal(0, result.Variables[0].ByteOffset);
        Assert.Equal(offsetAfterByte, result.Variables[1].ByteOffset);
    }

    // -------------------------------------------------------------------------
    // Ceiling tests
    // -------------------------------------------------------------------------

    // ⭐⭐ CE-314: both fixtures are SIZED FROM THE CONSTANT. They used to hard-code 25/26 ints against
    //    a 100-byte ceiling, so when CE-307 moved the ceiling they asserted nothing about it. ⛔ A
    //    boundary test that hard-codes the boundary stops being a boundary test the day it moves.
    private const int IntBytes = 4;

    [Fact]
    public void ExactlyAtCeiling_NoWarning()
    {
        int count = BlackboardBinPacker.MaxInlineBytes / IntBytes;   // exactly fills the region
        var vars = new List<BlackboardVariableDescriptor>();
        for (int i = 0; i < count; i++) vars.Add(V($"i{i}", typeof(int)));

        var result = BlackboardBinPacker.Pack(vars);

        Assert.Equal(BlackboardBinPacker.MaxInlineBytes, result.TotalInlineBytes);
        Assert.Equal(PackWarning.None, result.Warning);
    }

    [Fact]
    public void OverCeiling_WarningInlineMemoryExceeded()
    {
        int count = (BlackboardBinPacker.MaxInlineBytes / IntBytes) + 1;   // one int past it
        var vars = new List<BlackboardVariableDescriptor>();
        for (int i = 0; i < count; i++) vars.Add(V($"n{i}", typeof(int)));

        var result = BlackboardBinPacker.Pack(vars);

        Assert.True(result.TotalInlineBytes > BlackboardBinPacker.MaxInlineBytes);
        Assert.Equal(PackWarning.InlineMemoryExceeded, result.Warning);
    }


    // -------------------------------------------------------------------------
    // Empty / null cases
    // -------------------------------------------------------------------------

    [Fact]
    public void EmptyList_ZeroOffset_EmptyResult_NoWarning()
    {
        var result = Pack();

        Assert.Empty(result.Variables);
        Assert.Equal(0, result.TotalInlineBytes);
        Assert.Equal(PackWarning.None, result.Warning);
    }

    [Fact]
    public void NullAggregatedVars_TreatedSameAsEmpty()
    {
        var result = BlackboardBinPacker.Pack(new[] { V("x", typeof(int)) }, aggregatedVars: null);

        Assert.Single(result.Variables);
        Assert.Equal(0, result.Variables[0].ByteOffset);
    }

    // -------------------------------------------------------------------------
    // Tier assignment
    // -------------------------------------------------------------------------


    // -------------------------------------------------------------------------
    // Order preservation
    // -------------------------------------------------------------------------

    [Fact]
    public void Variables_PreserveDeclarationOrder()
    {
        var result = Pack(V("first", typeof(int)), V("second", typeof(bool)), V("third", typeof(float)));

        Assert.Equal("first",  result.Variables[0].Name);
        Assert.Equal("second", result.Variables[1].Name);
        Assert.Equal("third",  result.Variables[2].Name);
    }

    // -------------------------------------------------------------------------
    // Field type round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void FieldType_PreservedInResult()
    {
        var result = Pack(V("x", typeof(int)), V("y", typeof(bool)));

        Assert.Equal(typeof(int),  result.Variables[0].FieldType);
        Assert.Equal(typeof(bool), result.Variables[1].FieldType);
    }

    // -------------------------------------------------------------------------
    // Aggregated variables (CE-314: they continue the ONE region; the heavy tier is gone)
    // -------------------------------------------------------------------------

    [Fact]
    public void Pack_aggregated_vars_that_fit_inline_placed_inline()
    {
        // 2 ints = 8 bytes inline; one more int aggregated fits inline.
        var master = new List<BlackboardVariableDescriptor>
        {
            V("a", typeof(int)),
            V("b", typeof(int)),
        };
        var aggregated = new List<BlackboardVariableDescriptor>
        {
            V("c", typeof(int)),
        };

        var result = BlackboardBinPacker.Pack(master, aggregated);

        // ⭐ CE-314: aggregated variables continue the SAME region; there is no tier to check.
        Assert.Equal(3, result.Variables.Count);
        Assert.Equal(12, result.TotalInlineBytes);
    }

    // ⭐⭐⭐ CE-314 — REPLACEMENT COVERAGE. Nine tests were deleted with the heavy tier, but three of
    //    them were the ONLY place two surviving behaviours were asserted: that aggregated variables
    //    (a) continue the master region's offsets rather than restarting, and (b) align correctly
    //    ACROSS the master/aggregated boundary. ⛔ Deleting the spill tests without these would have
    //    silently dropped that coverage — the "route, don't just delete" half of the removal.

    [Fact]
    public void Pack_aggregated_vars_continue_the_master_regions_offsets()
    {
        // 3 ints = 12 B of master; the aggregated int must land at 12, NOT restart at 0.
        var master = new List<BlackboardVariableDescriptor>();
        for (int i = 0; i < 3; i++) master.Add(V($"m{i}", typeof(int)));
        var aggregated = new List<BlackboardVariableDescriptor> { V("agg", typeof(int)) };

        var result = BlackboardBinPacker.Pack(master, aggregated);

        var agg = result.Variables.First(v => v.Name == "agg");
        Assert.Equal(12, agg.ByteOffset);
        Assert.Equal(16, result.TotalInlineBytes);
    }

    [Fact]
    public void Pack_aggregated_vars_align_across_the_master_boundary()
    {
        // Master ends at 1 byte (a bool). An aggregated long must align to 8 => offset 8, not 1.
        var master = new List<BlackboardVariableDescriptor> { V("mb", typeof(bool)) };
        var aggregated = new List<BlackboardVariableDescriptor> { V("al", typeof(long)) };

        var result = BlackboardBinPacker.Pack(master, aggregated);

        Assert.Equal(0, result.Variables.First(v => v.Name == "mb").ByteOffset);
        Assert.Equal(8, result.Variables.First(v => v.Name == "al").ByteOffset);
        Assert.Equal(16, result.TotalInlineBytes);
    }

    /// <summary>
    /// ⛔ The deleted <c>Pack_master_overflow_does_not_trigger_heavy_placement</c> also pinned that an
    /// over-budget pack still returns EVERY variable with a resolved offset — the panel draws the rows
    /// either way. ⭐ Kept, without the heavy half.
    /// </summary>
    [Fact]
    public void Pack_overBudget_stillResolvesEveryVariablesOffset()
    {
        int count = (BlackboardBinPacker.MaxInlineBytes / IntBytes) + 2;
        var master = new List<BlackboardVariableDescriptor>();
        for (int i = 0; i < count; i++) master.Add(V($"m{i}", typeof(int)));
        var aggregated = new List<BlackboardVariableDescriptor> { V("agg", typeof(int)) };

        var result = BlackboardBinPacker.Pack(master, aggregated);

        Assert.Equal(PackWarning.InlineMemoryExceeded, result.Warning);
        Assert.Equal(count + 1, result.Variables.Count);
        Assert.Contains(result.Variables, v => v.Name == "agg");
    }





    // Regression: an unmarshalable type (e.g. a variable whose CLR type couldn't be resolved and
    // fell back to System.Object) must NOT crash the editor render loop — it degrades to 0 bytes.
    [Fact]
    public void UnmarshalableType_DegradesToZero_DoesNotThrow()
    {
        var vars = new[] { V("unresolved", typeof(object)) };

        var ex = Record.Exception(() => BlackboardBinPacker.Pack(vars));
        Assert.Null(ex);

        var result = BlackboardBinPacker.Pack(vars);
        Assert.Equal(0, result.Variables.Single(v => v.Name == "unresolved").ByteSize);
    }
}
