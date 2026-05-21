using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// Unit tests for Blackboard component layout constants and struct sizing - TASK-RT-003.
/// Covers success criteria SC1-SC7.
/// </summary>
public sealed class BlackboardLayoutTests
{
    // ---- SC1: Component struct sizes are exactly their tier size -------------

    [Fact]
    public void SC1_BlueprintBlackboard1024_Is1024Bytes()
    {
        Assert.Equal(1024, Unsafe.SizeOf<BlueprintBlackboard1024>());
    }

    [Fact]
    public void SC1_BlueprintBlackboard4096_Is4096Bytes()
    {
        Assert.Equal(4096, Unsafe.SizeOf<BlueprintBlackboard4096>());
    }

    [Fact]
    public void SC1_BlueprintBlackboard16384_Is16384Bytes()
    {
        Assert.Equal(16384, Unsafe.SizeOf<BlueprintBlackboard16384>());
    }

    // ---- SC2: Partitioning struct sizes ------------------------------------

    [Fact]
    public void SC2_BlueprintBlackboardHeader_Is32Bytes()
    {
        Assert.Equal(32, Unsafe.SizeOf<BlueprintBlackboardHeader>());
    }

    [Fact]
    public void SC2_BlueprintSlotEntry_Is16Bytes()
    {
        Assert.Equal(16, Unsafe.SizeOf<BlueprintSlotEntry>());
    }

    [Fact]
    public void SC2_BlueprintFreeBlockHeader_Is4Bytes()
    {
        Assert.Equal(4, Unsafe.SizeOf<BlueprintFreeBlockHeader>());
    }

    [Fact]
    public void SC2_SlotEntrySize_Constant_Matches_Struct()
    {
        Assert.Equal(BlueprintBlackboardPartitions.SlotEntrySize, Unsafe.SizeOf<BlueprintSlotEntry>());
    }

    // ---- SC3: Payload layout constants are consistent ----------------------

    [Fact]
    public void SC3_Tier1024_PayloadConstants()
    {
        Assert.Equal(1024, BlueprintBlackboard1024.TotalSize);
        Assert.Equal(32,   BlueprintBlackboard1024.HeaderSize);
        Assert.Equal(4,    BlueprintBlackboard1024.MaxSlots);
        Assert.Equal(64,   BlueprintBlackboard1024.SlotTableSize);   // 4 * 16
        Assert.Equal(96,   BlueprintBlackboard1024.PayloadStart);    // 32 + 64
        Assert.Equal(928,  BlueprintBlackboard1024.PayloadSize);     // 1024 - 96
    }

    [Fact]
    public void SC3_Tier4096_PayloadConstants()
    {
        Assert.Equal(4096, BlueprintBlackboard4096.TotalSize);
        Assert.Equal(32,   BlueprintBlackboard4096.HeaderSize);
        Assert.Equal(8,    BlueprintBlackboard4096.MaxSlots);
        Assert.Equal(128,  BlueprintBlackboard4096.SlotTableSize);   // 8 * 16
        Assert.Equal(160,  BlueprintBlackboard4096.PayloadStart);    // 32 + 128
        Assert.Equal(3936, BlueprintBlackboard4096.PayloadSize);     // 4096 - 160
    }

    [Fact]
    public void SC3_Tier16384_PayloadConstants()
    {
        Assert.Equal(16384, BlueprintBlackboard16384.TotalSize);
        Assert.Equal(32,    BlueprintBlackboard16384.HeaderSize);
        Assert.Equal(16,    BlueprintBlackboard16384.MaxSlots);
        Assert.Equal(256,   BlueprintBlackboard16384.SlotTableSize);  // 16 * 16
        Assert.Equal(288,   BlueprintBlackboard16384.PayloadStart);   // 32 + 256
        Assert.Equal(16096, BlueprintBlackboard16384.PayloadSize);    // 16384 - 288
    }

    // ---- SC4: SlotTableSize == MaxSlots * SlotEntrySize --------------------

    [Fact]
    public void SC4_SlotTableSize_Equals_MaxSlots_Times_SlotEntrySize()
    {
        int slotEntrySize = BlueprintBlackboardPartitions.SlotEntrySize;

        Assert.Equal(BlueprintBlackboard1024.SlotTableSize,  BlueprintBlackboard1024.MaxSlots  * slotEntrySize);
        Assert.Equal(BlueprintBlackboard4096.SlotTableSize,  BlueprintBlackboard4096.MaxSlots  * slotEntrySize);
        Assert.Equal(BlueprintBlackboard16384.SlotTableSize, BlueprintBlackboard16384.MaxSlots * slotEntrySize);
    }

    // ---- SC5: ComponentId attributes match GlobalComponentIds ---------------

    [Fact]
    public void SC5_BlueprintBlackboard1024_HasCorrectComponentId()
    {
        var attr = typeof(BlueprintBlackboard1024).GetCustomAttribute<ComponentIdAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(GlobalComponentIds.BlueprintBlackboard1024, attr!.Id);
    }

    [Fact]
    public void SC5_BlueprintBlackboard4096_HasCorrectComponentId()
    {
        var attr = typeof(BlueprintBlackboard4096).GetCustomAttribute<ComponentIdAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(GlobalComponentIds.BlueprintBlackboard4096, attr!.Id);
    }

    [Fact]
    public void SC5_BlueprintBlackboard16384_HasCorrectComponentId()
    {
        var attr = typeof(BlueprintBlackboard16384).GetCustomAttribute<ComponentIdAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(GlobalComponentIds.BlueprintBlackboard16384, attr!.Id);
    }

    // ---- SC6: Default-init blackboard has zeroed memory ---------------------

    [Fact]
    public unsafe void SC6_Default_BlueprintBlackboard1024_IsZeroed()
    {
        var bb = default(BlueprintBlackboard1024);
        // First 4 bytes of a default-initialised component must be zero (MagicAndVersion not set)
        byte* p = bb.Memory;
        Assert.Equal(0, p[0]);
        Assert.Equal(0, p[1]);
        Assert.Equal(0, p[2]);
        Assert.Equal(0, p[3]);
    }

    // ---- SC7: build is verified by the overall test run passing -------------
    //          (no runtime assertion needed; compilation of this file is the test)
}
