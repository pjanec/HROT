using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Tests.Runtime;

namespace Hrot.Blueprints.Tests.Runtime.BlueprintTickSystem;

/// <summary>
/// SC1 / SC2: Basic per-frame tick dispatch to Blueprint slots.
/// Per Runtime DD §11.3.
/// </summary>
[Collection("DebugProbe")]
public sealed class SingleSlotTickTests
{
    // SC1: Attach one Blueprint, tick once, TickCount == 1.
    [Fact]
    public void Tick_SingleBlueprintSlot_IncrementsTick()
    {
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        var asset = FakeInstanceBp.MakeAsset();

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        var state = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(state);
        Assert.True(state!.Value.TryGetField<int>("TickCount", out var tc));
        Assert.Equal(1, tc);
    }

    // SC1 extended: Two frames, TickCount == 2.
    [Fact]
    public void Tick_TwoFrames_TickCountIsTwo()
    {
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        var asset = FakeInstanceBp.MakeAsset();

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);
        fixture.TickFrame(0.016f);

        var state = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(state);
        Assert.True(state!.Value.TryGetField<int>("TickCount", out var tc));
        Assert.Equal(2, tc);
    }

    // SC2: Two blueprints registered on same entity, both get ticked.
    [Fact]
    public void Tick_TwoBlueprintsOnOneEntity_BothTicked()
    {
        using var fixture = new BlueprintTestFixture();

        // Register both blueprints in a single staging commit (CommitStaging fully replaces).
        var asset1 = FakeInstanceBp.MakeAsset();
        var asset2 = new BlueprintAsset
            { AssetId = new Guid("CAFEBABE-0000-0000-0000-000000000000"), Name = "FakeSecond" };
        var staging = fixture.Registry.BeginStaging();
        staging.Add(FakeInstanceBp.BlueprintId, FakeInstanceBp.MakeDefinition());
        staging.Add(BlueprintIdHash.Compute(asset2.AssetId),
            new BlueprintDefinition
            {
                Name = "FakeSecond",
                Kind = Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,
                StructureHash = 0xBBBBBBBBBBBBBBBBUL,
                StateSize = FakeInstanceBp.StateSize,
                InitDefault = FakeInstanceBp.InitDefault,
                Tick = FakeInstanceBp.Tick,
                StateFields = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
                {
                    ["TickCount"] = new BlueprintFieldDescriptor(
                        "TickCount", typeof(int),
                        OffsetBytes: Unsafe.SizeOf<BlueprintLatentCursor>(),
                        SizeBytes: sizeof(int), CategoryOrEmpty: ""),
                },
            });
        fixture.Registry.CommitStaging(staging);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset1, entity);
        fixture.AttachBlueprint(asset2, entity);

        fixture.TickFrame(0.016f);

        var state1 = fixture.GetBlueprintState(asset1, entity);
        var state2 = fixture.GetBlueprintState(asset2, entity);
        Assert.NotNull(state1);
        Assert.NotNull(state2);
        Assert.True(state1!.Value.TryGetField<int>("TickCount", out var tc1));
        Assert.True(state2!.Value.TryGetField<int>("TickCount", out var tc2));
        Assert.Equal(1, tc1);
        Assert.Equal(1, tc2);
    }

    // Negative: Entity without any blackboard component is never ticked.
    [Fact]
    public void Tick_SkipsEntity_WithoutBlackboard()
    {
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);

        // Entity with no BB component
        var entity = fixture.CreateEntity();

        // Should not throw, and TickCount stays 0.
        fixture.TickFrame(0.016f);

        // No blackboard means no slot; HasSlot should return false.
        var asset = FakeInstanceBp.MakeAsset();
        Assert.False(fixture.HasSlot(asset, entity));
    }
}
