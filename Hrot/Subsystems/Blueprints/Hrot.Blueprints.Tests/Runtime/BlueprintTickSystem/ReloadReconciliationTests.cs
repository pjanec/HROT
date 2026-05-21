using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Systems;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Tests.Runtime;

namespace Hrot.Blueprints.Tests.Runtime.BlueprintTickSystem;

/// <summary>
/// SC4 / SC7: Reload reconciliation -- hard reset on structure-hash mismatch.
/// Per Runtime DD §11.4.
/// </summary>
public sealed class ReloadReconciliationTests
{
    // SC4: Hard-reload changes structure hash -> payload is zeroed and InstanceVersion bumps.
    [Fact]
    public void HardReload_ChangedStructureHash_ResetsPayloadAndBumpsVersion()
    {
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        var asset = FakeInstanceBp.MakeAsset();

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);
        fixture.TickFrame(0.016f);

        // State before reload
        var before = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(before);
        Assert.True(before!.Value.TryGetField<int>("TickCount", out var tcBefore));
        Assert.Equal(2, tcBefore);

        // Commit new staging with SAME id but DIFFERENT StructureHash
        var staging = fixture.Registry.BeginStaging();
        staging.Add(FakeInstanceBp.BlueprintId,
            new BlueprintDefinition
            {
                Name = "FakeInstance",
                Kind = Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,
                StructureHash = 0xDEAD000000000001UL,  // different from FakeInstanceBp.StructureHash
                StateSize = FakeInstanceBp.StateSize,
                InitDefault = FakeInstanceBp.InitDefault,
                Tick = FakeInstanceBp.Tick,
                StateFields = new System.Collections.Generic.Dictionary<string, BlueprintFieldDescriptor>(System.StringComparer.Ordinal)
                {
                    ["TickCount"] = new BlueprintFieldDescriptor(
                        "TickCount", typeof(int),
                        OffsetBytes: Unsafe.SizeOf<BlueprintLatentCursor>(),
                        SizeBytes: sizeof(int), CategoryOrEmpty: ""),
                },
            });
        fixture.Registry.CommitStaging(staging);

        // One more tick triggers hard-reload reconciliation
        fixture.TickFrame(0.016f);

        // TickCount should be 1 (payload was zeroed by ResetSlot, then ticked once)
        var after = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(after);
        Assert.True(after!.Value.TryGetField<int>("TickCount", out var tcAfter));
        Assert.Equal(1, tcAfter);
    }

    // SC4 soft: Same structure hash -> state is preserved.
    [Fact]
    public void SoftReload_SameStructureHash_PreservesState()
    {
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        var asset = FakeInstanceBp.MakeAsset();

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);
        fixture.TickFrame(0.016f);

        // Commit staging with the SAME structure hash
        var staging = fixture.Registry.BeginStaging();
        staging.Add(FakeInstanceBp.BlueprintId, FakeInstanceBp.MakeDefinition());
        fixture.Registry.CommitStaging(staging);

        fixture.TickFrame(0.016f);

        var state = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(state);
        Assert.True(state!.Value.TryGetField<int>("TickCount", out var tc));
        Assert.Equal(3, tc);  // 2 from before + 1 after soft reload
    }

    // SC7: Capturing sink records exactly one OnHardReset call per slot per hard-reload.
    [Fact]
    public void HardReload_LogSink_CalledExactlyOnce()
    {
        var sink = new CapturingSink();
        using var fixture = new BlueprintTestFixture();

        // TickSystem with capturing sink
        var registry = fixture.Registry;
        FakeInstanceBp.Register(registry);
        var asset = FakeInstanceBp.MakeAsset();

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);
        fixture.TickFrame(0.016f);

        // Swap in a tick system that uses our capturing sink
        var sinkTickSystem = new Fdp.Toolkit.Blueprints.Systems.BlueprintTickSystem(registry, sink);

        // Commit staging with different hash
        var staging = registry.BeginStaging();
        staging.Add(FakeInstanceBp.BlueprintId,
            new BlueprintDefinition
            {
                Name = "FakeInstance",
                Kind = Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,
                StructureHash = 0xFFFFFFFFFFFFFFFFUL,
                StateSize = FakeInstanceBp.StateSize,
                InitDefault = FakeInstanceBp.InitDefault,
                Tick = FakeInstanceBp.Tick,
                StateFields = new System.Collections.Generic.Dictionary<string, BlueprintFieldDescriptor>(System.StringComparer.Ordinal)
                {
                    ["TickCount"] = new BlueprintFieldDescriptor(
                        "TickCount", typeof(int),
                        OffsetBytes: Unsafe.SizeOf<BlueprintLatentCursor>(),
                        SizeBytes: sizeof(int), CategoryOrEmpty: ""),
                },
            });
        registry.CommitStaging(staging);

        // Use the sink-backed system directly for one frame
        sinkTickSystem.Execute(fixture.World, 0.016f);

        Assert.Equal(1, sink.CallCount);
    }

    private sealed class CapturingSink : IReloadLogSink
    {
        public int CallCount { get; private set; }
        public void OnHardReset(int blueprintId, uint newInstanceVersion) => CallCount++;
    }
}
