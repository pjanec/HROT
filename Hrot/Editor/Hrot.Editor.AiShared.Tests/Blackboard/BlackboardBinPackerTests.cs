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

    [Fact]
    public void ExactlyAtCeiling_NoWarning()
    {
        // Pack twenty 4-byte ints = 80 bytes, then five 4-byte floats = 20 bytes = 100 total.
        var vars = new List<BlackboardVariableDescriptor>();
        for (int i = 0; i < 20; i++) vars.Add(V($"i{i}", typeof(int)));
        for (int i = 0; i < 5; i++) vars.Add(V($"f{i}", typeof(float)));

        var result = BlackboardBinPacker.Pack(vars);

        Assert.Equal(100, result.TotalInlineBytes);
        Assert.Equal(PackWarning.None, result.Warning);
        Assert.False(result.RequiresHeavyComponent);
    }

    [Fact]
    public void OverCeiling_WarningInlineMemoryExceeded()
    {
        // 26 x 4-byte ints = 104 bytes > 100.
        var vars = new List<BlackboardVariableDescriptor>();
        for (int i = 0; i < 26; i++) vars.Add(V($"n{i}", typeof(int)));

        var result = BlackboardBinPacker.Pack(vars);

        Assert.True(result.TotalInlineBytes > BlackboardBinPacker.MaxInlineBytes);
        Assert.Equal(PackWarning.InlineMemoryExceeded, result.Warning);
        Assert.False(result.RequiresHeavyComponent);
    }

    [Fact]
    public void OverCeiling_RequiresHeavyComponent_IsFalse()
    {
        var vars = new List<BlackboardVariableDescriptor>();
        for (int i = 0; i < 26; i++) vars.Add(V($"n{i}", typeof(int)));

        var result = BlackboardBinPacker.Pack(vars);

        // Heavy spill is TASK-BB-1c-04; always false in this slice.
        Assert.False(result.RequiresHeavyComponent);
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

    [Fact]
    public void AllVariables_Tier_IsInline()
    {
        var result = Pack(V("a", typeof(int)), V("b", typeof(bool)), V("c", typeof(float)));

        foreach (var v in result.Variables)
            Assert.Equal(PackTier.Inline, v.Tier);
    }

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
    // TASK-BB-1c-04: Heavy-tier spill
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

        // All three should be inline.
        Assert.All(result.Variables, v => Assert.Equal(PackTier.Inline, v.Tier));
        Assert.Equal(0, result.TotalHeavyBytes);
        Assert.False(result.RequiresHeavyComponent);
    }

    [Fact]
    public void Pack_aggregated_vars_that_overflow_inline_placed_heavy()
    {
        // 25 ints = 100 bytes (exactly at MaxInlineBytes).
        // One more aggregated int: 100 + 4 = 104 > MaxInlineBytes => must spill.
        var master = new List<BlackboardVariableDescriptor>();
        for (int i = 0; i < 25; i++) master.Add(V($"m{i}", typeof(int)));

        var aggregated = new List<BlackboardVariableDescriptor>
        {
            V("overflow", typeof(int)),
        };

        var result = BlackboardBinPacker.Pack(master, aggregated);

        var heavy = result.Variables.Where(v => v.Tier == PackTier.Heavy).ToList();
        Assert.Single(heavy);
        Assert.Equal("overflow", heavy[0].Name);
    }

    [Fact]
    public void Pack_aggregated_vars_require_heavy_component_flag()
    {
        // Same setup: 25 master ints (100 B), one aggregated int forces heavy.
        var master = new List<BlackboardVariableDescriptor>();
        for (int i = 0; i < 25; i++) master.Add(V($"m{i}", typeof(int)));
        var aggregated = new List<BlackboardVariableDescriptor> { V("x", typeof(int)) };

        var result = BlackboardBinPacker.Pack(master, aggregated);

        Assert.True(result.RequiresHeavyComponent);
    }

    [Fact]
    public void Pack_master_overflow_does_not_trigger_heavy_placement()
    {
        // 26 ints = 104 bytes: master itself exceeds 100 B ceiling.
        var master = new List<BlackboardVariableDescriptor>();
        for (int i = 0; i < 26; i++) master.Add(V($"m{i}", typeof(int)));
        var aggregated = new List<BlackboardVariableDescriptor> { V("x", typeof(int)) };

        var result = BlackboardBinPacker.Pack(master, aggregated);

        // Master overflow takes precedence; heavy component must NOT be set.
        Assert.False(result.RequiresHeavyComponent);
        Assert.Equal(PackWarning.InlineMemoryExceeded, result.Warning);
    }

    [Fact]
    public void Pack_heavy_offset_starts_at_zero()
    {
        // 25 ints = 100 B inline; first aggregated int spills to heavy at offset 0.
        var master = new List<BlackboardVariableDescriptor>();
        for (int i = 0; i < 25; i++) master.Add(V($"m{i}", typeof(int)));
        var aggregated = new List<BlackboardVariableDescriptor>
        {
            V("h1", typeof(int)),
        };

        var result = BlackboardBinPacker.Pack(master, aggregated);

        var heavy = result.Variables.Where(v => v.Tier == PackTier.Heavy).ToList();
        // First heavy var should start at offset 0.
        Assert.Single(heavy);
        Assert.Equal(0, heavy[0].ByteOffset);
    }

    [Fact]
    public void Pack_heavy_alignment_respected()
    {
        // 25 ints = 100 B inline; bool + long both spill to heavy.
        // bool is 1 B at heavy offset 0; long (8 B) must align to 8 => offset 8.
        var master = new List<BlackboardVariableDescriptor>();
        for (int i = 0; i < 25; i++) master.Add(V($"m{i}", typeof(int)));
        var aggregated = new List<BlackboardVariableDescriptor>
        {
            V("hb", typeof(bool)),
            V("hl", typeof(long)),
        };

        var result = BlackboardBinPacker.Pack(master, aggregated);

        var heavyBool = result.Variables.First(v => v.Name == "hb");
        var heavyLong = result.Variables.First(v => v.Name == "hl");

        Assert.Equal(PackTier.Heavy, heavyBool.Tier);
        Assert.Equal(PackTier.Heavy, heavyLong.Tier);
        // bool at 0; long must align to 8.
        Assert.Equal(0, heavyBool.ByteOffset);
        Assert.Equal(8, heavyLong.ByteOffset);
    }

    [Fact]
    public void TotalHeavyBytes_zero_when_no_heavy_vars()
    {
        var result = BlackboardBinPacker.Pack(new[] { V("x", typeof(int)) });

        Assert.Equal(0, result.TotalHeavyBytes);
    }

    [Fact]
    public void TotalHeavyBytes_nonzero_when_heavy_vars_present()
    {
        // 25 ints = 100 B inline; one aggregated int (4 B) spills to heavy.
        var master = new List<BlackboardVariableDescriptor>();
        for (int i = 0; i < 25; i++) master.Add(V($"m{i}", typeof(int)));
        var aggregated = new List<BlackboardVariableDescriptor>
        {
            V("h1", typeof(int)),
        };

        var result = BlackboardBinPacker.Pack(master, aggregated);

        Assert.True(result.TotalHeavyBytes > 0);
    }
}
