using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Tests.Runtime;

namespace Hrot.Blueprints.Tests.Runtime.BlueprintMaintenanceSystem;

/// <summary>
/// SC1/SC2/SC3/SC4: tier upgrade across ONE adjacent pair of the ladder.
/// Per Runtime DD §11.5.
///
/// <para>⭐ B4 — design §17.7. These named BB1024 → BB4096 as literals. ⛔ Two things broke when
/// <c>O3b</c> appended the 256 tier: the attach lands on <b>256</b>, not 1024, and 256+4096 is
/// <b>not an adjacent pair</b>, so <c>BlueprintMaintenanceSystem</c> — which walks
/// <see cref="BlueprintTierTable.AdjacentPairs"/> — correctly did nothing and the migration
/// assertions failed. ⇒ the pair is now DERIVED from where the attach actually landed. The class
/// name keeps its historical spelling; the behaviour under test is the adjacent-pair promotion.</para>
/// </summary>
[Collection("DebugProbe")]
public sealed class TierUpgrade_1024_to_4096_Tests
{
    // SC1/SC2: Entity with both BB1024 and BB4096 present -> state migrated, BB1024 removed.
    [Fact]
    public unsafe void TierUpgrade_WhenBothComponentsPresent_MigratesState()
    {
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        var asset = FakeInstanceBp.MakeAsset();

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // ⭐ B4: the pair is whatever the ladder says sits above where the attach LANDED.
        var small = BlueprintTierTable.Of(fixture.World, entity)!;
        var large = NextUp(small);

        // Manually trigger the upgrade signal by adding the NEXT tier up.
        large.Add(fixture.World, entity);

        fixture.TickFrame(0.016f);

        // ⚠ The TIER is the subject here — the entity must have moved OFF the small one ONTO
        //   the large one. ⛔ HasStore cannot express that: it is true on both sides.
        Assert.True(large.Has(fixture.World, entity));
        Assert.False(small.Has(fixture.World, entity));

        // Slot still accessible in the promoted store.
        byte* memory = large.Memory(fixture.World, entity);
        bool found = BlueprintBlackboardPartitions.TryGetSlotOffset(
            memory, FakeInstanceBp.BlueprintId, out int payloadOffset);
        Assert.True(found);
    }

    // SC3: Entity with only BB1024 (no BB4096) is not touched by MaintenanceSystem.
    [Fact]
    public void TierUpgrade_EntityWithOnlyBB1024_NotTouched()
    {
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        var asset = FakeInstanceBp.MakeAsset();

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);   // ONE tier only — whichever the ladder chose

        var small = BlueprintTierTable.Of(fixture.World, entity)!;
        var large = NextUp(small);

        fixture.TickFrame(0.016f);

        // Nothing signalled an upgrade, so the entity must be untouched on its own tier.
        Assert.True(small.Has(fixture.World, entity));
        Assert.False(large.Has(fixture.World, entity));
    }

    // SC4: State written before upgrade is preserved in BB4096 after upgrade.
    [Fact]
    public unsafe void TierUpgrade_StatePreserved_AfterUpgrade()
    {
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        var asset = FakeInstanceBp.MakeAsset();

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Tick once to increment TickCount to 1
        fixture.TickFrame(0.016f);

        // Verify TickCount == 1 in BB1024
        var state1 = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(state1);
        Assert.True(state1!.Value.TryGetField<int>("TickCount", out var tc1));
        Assert.Equal(1, tc1);

        // Add the NEXT tier up to trigger the upgrade.
        NextUp(BlueprintTierTable.Of(fixture.World, entity)!).Add(fixture.World, entity);

        fixture.TickFrame(0.016f);

        // Now in BB4096 -- TickCount should be 2 (1 from before + 1 from this tick)
        var state2 = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(state2);
        Assert.True(state2!.Value.TryGetField<int>("TickCount", out var tc2));
        // After upgrade: state migrated (TickCount = 1) + 1 tick = 2
        Assert.Equal(2, tc2);
    }

    /// <summary>The tier one step up the ladder — <see cref="BlueprintTierTable.AdjacentPairs"/>
    /// is exactly the promotion relation <c>BlueprintMaintenanceSystem</c> walks.</summary>
    private static BlueprintTierSpec NextUp(BlueprintTierSpec from)
    {
        var pairs = BlueprintTierTable.AdjacentPairs;
        for (int i = 0; i < pairs.Count; i++)
            if (pairs[i].From == from)
                return pairs[i].To;

        throw new InvalidOperationException($"{from.Tier} is the top of the ladder.");
    }
}
