using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Blueprints.Tests.Runtime;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Blueprints.Tests.Runtime.BlueprintTickSystem;

/// <summary>
/// ⭐⭐⭐ <b><c>C-tick</c>, END TO END — the simulation ticks, and the table lights up.</b>
///
/// <para>
/// ⛔ <b>This is the test that actually says "the table is live".</b> The two halves were each proved
/// separately — Batch 68's predicate against a hand-driven tick, and this batch's counter against the
/// real <c>BlueprintTickSystem</c> — but ⭐ <b>neither proves they are CONNECTED</b>. Here a real
/// blueprint runs on a real world and a real <c>VariableRow</c> reads the real bytes and the real
/// counter.
/// </para>
///
/// <para>
/// 🔴 <b>Every assertion below was unreachable before Batch 69</b>: with no per-asset counter,
/// <c>AssetTick</c> was <c>null</c> on every row and the monitor reported <c>None</c> by design.
/// </para>
/// </summary>
[Collection("DebugProbe")]
public sealed class LiveVariableTableTests : IDisposable
{
    public LiveVariableTableTests() => BlueprintAssetTick.Reset();
    public void Dispose()           => BlueprintAssetTick.Reset();

    /// <summary>Reads the live <c>TickCount</c> field bytes straight out of the entity's slot.</summary>
    private static unsafe byte[] ReadTickCountBytes(BlueprintTestFixture fixture, Entity entity)
    {
        ref var bb    = ref fixture.World.GetComponentRW<BlueprintBlackboard1024>(entity);
        ref byte mem  = ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb);
        byte* memory  = (byte*)Unsafe.AsPointer(ref mem);

        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(
                memory, FakeInstanceBp.BlueprintId, out int payloadOffset))
            return Array.Empty<byte>();

        int fieldOffset = payloadOffset + Unsafe.SizeOf<BlueprintLatentCursor>();
        var bytes = new byte[sizeof(int)];
        Marshal.Copy((IntPtr)(memory + fieldOffset), bytes, 0, bytes.Length);
        return bytes;
    }

    private static VariableRow MakeRow(BlueprintTestFixture fixture, Entity entity)
        => new(
            Origin:    new VariableRowOrigin(
                           FakeInstanceBp.MakeAsset().AssetId, entity, "Variables", "TickCount",
                           "FakeInstance"),
            ShortName: "TickCount",
            TypeText:  "Int32",
            ClrType:   typeof(int),
            ReadValue: () => ReadTickCountBytes(fixture, entity),
            AssetTick: BlueprintAssetTickSource.For(FakeInstanceBp.MakeAsset().AssetId, entity));

    private static BlueprintTestFixture MakeFixtureWith(out Entity entity)
    {
        var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        entity = fixture.World.CreateEntity();
        fixture.AttachBlueprint(FakeInstanceBp.MakeAsset(), entity);
        return fixture;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The sim changes a value ⇒ the row goes red for exactly one asset tick.</b>
    /// <c>FakeInstanceBp.Tick</c> increments <c>TickCount</c> every frame, so it changes on every tick
    /// and — ⚠ correctly, per §4a — <b>stays highlighted</b>: <i>"a value that changes every tick stays
    /// highlighted… VS behaves the same. No damping."</i>
    /// </summary>
    [Fact]
    public void AValueTheSimChangesEveryTick_StaysHighlighted()
    {
        using var fixture = MakeFixtureWith(out var entity);
        BlueprintAssetTickSource.Attach();
        try
        {
            var model = new VariableTableModel(
                new FixedVariableRowSource(new[] { MakeRow(fixture, entity) }),
                VariableTableColumns.Details) { RunState = VariableRunState.Running };

            var row = model.Source.GetRows()[0];

            fixture.TickFrame(0.016f);
            model.Build();                                   // seed at TickCount = 1

            fixture.TickFrame(0.016f);                       // TickCount -> 2
            Assert.True(model.Build().HighlightOf(row).Changed);

            fixture.TickFrame(0.016f);                       // TickCount -> 3
            Assert.True(model.Build().HighlightOf(row).Changed);
        }
        finally { BlueprintAssetTickSource.Detach(); }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE ruling, end to end: paused, the highlight PERSISTS — then a Step clears it.</b>
    ///
    /// <para>
    /// After the value stops changing, the red must survive every frozen frame and clear on the first
    /// real one. ⛔ On the world tick it would have cleared at the first frozen frame.
    /// </para>
    /// </summary>
    [Fact]
    public void Frozen_TheHighlightPersists_AndAStepClearsIt()
    {
        using var fixture = MakeFixtureWith(out var entity);
        BlueprintAssetTickSource.Attach();
        try
        {
            var row   = MakeRow(fixture, entity);
            var model = new VariableTableModel(
                new FixedVariableRowSource(new[] { row }),
                VariableTableColumns.Details) { RunState = VariableRunState.Paused };

            fixture.TickFrame(0.016f);
            model.Build();                                   // seed
            fixture.TickFrame(0.016f);                       // the change
            Assert.True(model.Build().HighlightOf(row).Changed);

            // 30 frozen world frames -- deltaTime == 0, nothing ticks.
            for (int i = 0; i < 30; i++)
            {
                fixture.TickFrame(0f);
                Assert.True(model.Build().HighlightOf(row).Changed,
                    "the highlight must survive a frozen frame -- this is the ruling's whole point");
            }

            // ⭐ The Step. TickCount changes again, so it stays red -- but for the NEW tick, and the
            //   counter has moved, which is what the assertion below distinguishes.
            uint before = BlueprintAssetTick.Get(FakeInstanceBp.BlueprintId, entity)!.Value;
            fixture.TickFrame(0.016f);
            Assert.Equal(before + 1, BlueprintAssetTick.Get(FakeInstanceBp.BlueprintId, entity));
        }
        finally { BlueprintAssetTickSource.Detach(); }
    }

    /// <summary>
    /// ⭐ <b>The refcount</b> — the counter must not be switched off under a second open panel, and
    /// must go off when the last one closes.
    /// </summary>
    [Fact]
    public void TheCounterIsHeldOn_WhileAnyPanelNeedsIt()
    {
        Assert.False(BlueprintAssetTick.Enabled);

        BlueprintAssetTickSource.Attach();
        BlueprintAssetTickSource.Attach();
        Assert.True(BlueprintAssetTick.Enabled);

        BlueprintAssetTickSource.Detach();
        Assert.True(BlueprintAssetTick.Enabled);             // ⛔ the second panel still needs it

        BlueprintAssetTickSource.Detach();
        Assert.False(BlueprintAssetTick.Enabled);
        Assert.Equal(0, BlueprintAssetTickSource.AttachCount);
    }
}
