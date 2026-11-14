using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Xunit;

namespace Fdp.Toolkit.Blueprints.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>C-tick</c> — the per-<c>(asset, entity)</c> counter the value monitor was waiting on.</b>
///
/// <para>
/// 📄 <c>DESIGN_Variable_Details_And_Editing.md</c> §4a: <i>"a non-frozen CGF behavior tick, i.e. the
/// asset tick/update call."</i> ⛔ Not the frame, not the world tick.
/// </para>
///
/// <para>
/// ⚠ <b>These tests drive the counter directly; <see cref="BlueprintAssetTickFrozenTests"/> drives it
/// through the real <c>BlueprintTickSystem</c></b> — which is where the FROZEN claim has to be proved,
/// because "frozen" is a property of where the stamp sits in that system, not of this type.
/// </para>
/// </summary>
public sealed class BlueprintAssetTickTests : IDisposable
{
    public BlueprintAssetTickTests() => BlueprintAssetTick.Reset();
    public void Dispose()            => BlueprintAssetTick.Reset();

    private static Entity Ent(int i) => new Entity(i, 1);

    /// <summary>
    /// ⛔ <b>Default OFF, and off means nothing is recorded</b> — not "recorded but hidden". The tick
    /// loop runs every instance every frame and a shipping build must not pay for a panel nobody has
    /// open.
    /// </summary>
    [Fact]
    public void Disabled_RecordsNothing()
    {
        Assert.False(BlueprintAssetTick.Enabled);

        for (int i = 0; i < 10; i++) BlueprintAssetTick.Bump(7, Ent(1));

        Assert.Null(BlueprintAssetTick.Get(7, Ent(1)));
        Assert.Equal(0, BlueprintAssetTick.TrackedInstanceCount);
    }

    /// <summary>
    /// ⭐ <b><c>null</c> is "never ticked", NOT tick zero.</b> It flows straight into
    /// <c>VariableRow.AssetTick</c>, whose contract is that a row with no source is inert rather than
    /// wrong ⇒ an asset that has never run reports no highlight instead of pretending it just changed.
    /// </summary>
    [Fact]
    public void NeverTicked_IsNullRatherThanZero()
    {
        BlueprintAssetTick.Enabled = true;
        Assert.Null(BlueprintAssetTick.Get(7, Ent(1)));
    }

    /// <summary>⭐ One bump per tick, monotonically.</summary>
    [Fact]
    public void EachBump_AdvancesTheCounterByOne()
    {
        BlueprintAssetTick.Enabled = true;

        BlueprintAssetTick.Bump(7, Ent(1));
        Assert.Equal(1u, BlueprintAssetTick.Get(7, Ent(1)));

        BlueprintAssetTick.Bump(7, Ent(1));
        Assert.Equal(2u, BlueprintAssetTick.Get(7, Ent(1)));
    }

    /// <summary>
    /// ⭐⭐ <b>Two entities running ONE asset advance independently</b> — §1a: entity is part of row
    /// identity, and the ruling says so directly: <i>"the same asset on two entities ticks
    /// independently"</i>. 🔴 A counter keyed by asset alone passes every other test here.
    /// </summary>
    [Fact]
    public void TwoEntitiesRunningOneAsset_AdvanceIndependently()
    {
        BlueprintAssetTick.Enabled = true;

        BlueprintAssetTick.Bump(7, Ent(1));
        BlueprintAssetTick.Bump(7, Ent(1));
        BlueprintAssetTick.Bump(7, Ent(2));

        Assert.Equal(2u, BlueprintAssetTick.Get(7, Ent(1)));
        Assert.Equal(1u, BlueprintAssetTick.Get(7, Ent(2)));
        Assert.Equal(2, BlueprintAssetTick.TrackedInstanceCount);
    }

    /// <summary>⭐ And two assets on ONE entity likewise — the key is the pair, not either half.</summary>
    [Fact]
    public void TwoAssetsOnOneEntity_AdvanceIndependently()
    {
        BlueprintAssetTick.Enabled = true;

        BlueprintAssetTick.Bump(7, Ent(1));
        BlueprintAssetTick.Bump(8, Ent(1));
        BlueprintAssetTick.Bump(8, Ent(1));

        Assert.Equal(1u, BlueprintAssetTick.Get(7, Ent(1)));
        Assert.Equal(2u, BlueprintAssetTick.Get(8, Ent(1)));
    }

    /// <summary>
    /// ⚠ <b>The hot path allocates nothing when enabled.</b> The tick loop calls <c>Bump</c> once per
    /// instance per frame, so a per-call closure would be a real allocation regression — hence the
    /// cached update delegate. ⭐ Measured over the SECOND thousand calls, after the dictionary has
    /// grown and the delegate is warm.
    /// </summary>
    [Fact]
    public void Bump_DoesNotAllocate_OnTheSteadyStatePath()
    {
        BlueprintAssetTick.Enabled = true;
        for (int i = 0; i < 1000; i++) BlueprintAssetTick.Bump(7, Ent(1));   // warm up

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) BlueprintAssetTick.Bump(7, Ent(1));
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
