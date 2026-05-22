using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Tests.Runtime;

namespace Hrot.Blueprints.Tests.Runtime.BlueprintTickSystem;

/// <summary>
/// SC5: World-singleton Blueprint is lazily attached and ticked each frame.
/// Per Runtime DD §11.3.
/// </summary>
[Collection("DebugProbe")]
public sealed class WorldSingletonTickTests
{
    // SC5: Singleton is attached lazily on first tick -- slot count == 1 after tick.
    [Fact]
    public unsafe void WorldSingleton_AttachedLazily_OnFirstTick()
    {
        using var fixture = new BlueprintTestFixture();
        FakeWorldSingletonBp.Register(fixture.Registry);

        // No singleton exists yet
        Assert.False(fixture.World.HasSingleton<BlueprintBlackboard1024>());

        fixture.TickFrame(0.016f);

        // After tick, singleton should exist and have exactly one slot
        Assert.True(fixture.World.HasSingleton<BlueprintBlackboard1024>());

        ref var bb     = ref fixture.World.GetSingleton<BlueprintBlackboard1024>();
        ref byte mem   = ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb);
        byte* memory   = (byte*)Unsafe.AsPointer(ref mem);
        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        Assert.Equal(1, (int)header.SlotCount);
    }

    // SC5: Ticking a second frame does NOT add another slot (singleton stays at SlotCount == 1).
    [Fact]
    public unsafe void WorldSingleton_NotReattached_OnSecondTick()
    {
        using var fixture = new BlueprintTestFixture();
        FakeWorldSingletonBp.Register(fixture.Registry);

        fixture.TickFrame(0.016f);
        fixture.TickFrame(0.016f);

        ref var bb     = ref fixture.World.GetSingleton<BlueprintBlackboard1024>();
        ref byte mem   = ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb);
        byte* memory   = (byte*)Unsafe.AsPointer(ref mem);
        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        Assert.Equal(1, (int)header.SlotCount);
    }

    // SC5: InitDefault is called exactly once (on first tick); TickCount increments each frame.
    [Fact]
    public unsafe void WorldSingleton_TickCount_IncrementsEachFrame()
    {
        using var fixture = new BlueprintTestFixture();
        FakeWorldSingletonBp.Register(fixture.Registry);

        fixture.TickFrame(0.016f);
        fixture.TickFrame(0.016f);

        ref var bb     = ref fixture.World.GetSingleton<BlueprintBlackboard1024>();
        ref byte mem   = ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb);
        byte* memory   = (byte*)Unsafe.AsPointer(ref mem);

        bool found = BlueprintBlackboardPartitions.TryGetSlotOffset(
            memory, FakeWorldSingletonBp.BlueprintId, out int payloadOffset);
        Assert.True(found);

        ref var state = ref Unsafe.AsRef<FakeWorldSingletonBp.State>(memory + payloadOffset);
        Assert.Equal(2, state.TickCount);
    }
}
