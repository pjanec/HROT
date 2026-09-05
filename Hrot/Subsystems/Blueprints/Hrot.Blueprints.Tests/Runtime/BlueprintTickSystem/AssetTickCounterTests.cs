using System;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Tests.Runtime;

namespace Hrot.Blueprints.Tests.Runtime.BlueprintTickSystem;

/// <summary>
/// ⭐⭐⭐ <b><c>C-tick</c> through the REAL <c>BlueprintTickSystem</c> — where the FROZEN claim lives.</b>
///
/// <para>
/// 📄 <c>DESIGN_Variable_Details_And_Editing.md</c> §4a: the counter advances <i>"only when THAT
/// ASSET's tick/update actually runs, and only when it is not frozen."</i> ⇒ ⭐ <b>"frozen" is a
/// property of WHERE the stamp sits</b>, not of the counter type — <c>Execute</c> opens with
/// <c>if (deltaTime &lt;= 0f) return;</c>, so every stamp is below that guard. ⛔ Asserting it against
/// the counter alone would prove nothing.
/// </para>
///
/// <para>
/// 🔴 <b>This rail could not have passed before Batch 69</b> — there was no per-asset counter at all,
/// so <c>AssetTick</c> was <c>null</c> on every row and the highlight was inert by construction.
/// </para>
/// </summary>
[Collection("DebugProbe")]
public sealed class AssetTickCounterTests : IDisposable
{
    public AssetTickCounterTests() => BlueprintAssetTick.Reset();
    public void Dispose()          => BlueprintAssetTick.Reset();

    private static BlueprintTestFixture MakeFixtureWith(out Entity entity)
    {
        var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        entity = fixture.World.CreateEntity();
        fixture.AttachBlueprint(FakeInstanceBp.MakeAsset(), entity);
        return fixture;
    }

    /// <summary>⭐ A real frame advances the counter for that <c>(asset, entity)</c>.</summary>
    [Fact]
    public void ARealFrame_AdvancesTheCounter()
    {
        using var fixture = MakeFixtureWith(out var entity);
        BlueprintAssetTick.Enabled = true;

        fixture.TickFrame(0.016f);
        Assert.Equal(1u, BlueprintAssetTick.Get(FakeInstanceBp.BlueprintId, entity));

        fixture.TickFrame(0.016f);
        Assert.Equal(2u, BlueprintAssetTick.Get(FakeInstanceBp.BlueprintId, entity));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE rail: N frozen world frames do NOT advance it, and a Step advances it once.</b>
    ///
    /// <para>
    /// This is the ruling's whole point — <i>"paused on a breakpoint ⇒ the highlight PERSISTS… it
    /// clears when you actually Step"</i>. ⛔ On the world tick the red would clear on the very first
    /// frozen frame, which is why Batch 68 refused to wire it.
    /// </para>
    /// </summary>
    [Fact]
    public void FrozenFrames_DoNotAdvanceIt_AndAStepAdvancesItOnce()
    {
        using var fixture = MakeFixtureWith(out var entity);
        BlueprintAssetTick.Enabled = true;

        fixture.TickFrame(0.016f);
        uint atPause = BlueprintAssetTick.Get(FakeInstanceBp.BlueprintId, entity)!.Value;

        // 20 world frames with deltaTime == 0 -- the engine's paused state.
        for (int i = 0; i < 20; i++) fixture.TickFrame(0f);

        Assert.Equal(atPause, BlueprintAssetTick.Get(FakeInstanceBp.BlueprintId, entity));

        // ⭐ The Step.
        fixture.TickFrame(0.016f);
        Assert.Equal(atPause + 1, BlueprintAssetTick.Get(FakeInstanceBp.BlueprintId, entity));
    }

    /// <summary>
    /// ⭐⭐ <b>Two entities running ONE asset advance independently</b>, through the real system —
    /// §1a's "entity is part of identity", proved on the tick path rather than on the counter.
    /// </summary>
    [Fact]
    public void TwoEntitiesRunningOneAsset_AdvanceIndependently()
    {
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);

        var a = fixture.World.CreateEntity();
        fixture.AttachBlueprint(FakeInstanceBp.MakeAsset(), a);

        BlueprintAssetTick.Enabled = true;
        fixture.TickFrame(0.016f);          // only `a` exists

        var b = fixture.World.CreateEntity();
        fixture.AttachBlueprint(FakeInstanceBp.MakeAsset(), b);
        fixture.TickFrame(0.016f);          // both tick

        Assert.Equal(2u, BlueprintAssetTick.Get(FakeInstanceBp.BlueprintId, a));
        Assert.Equal(1u, BlueprintAssetTick.Get(FakeInstanceBp.BlueprintId, b));
    }

    /// <summary>
    /// ⛔ <b>Disabled ⇒ the tick path records nothing</b>, so a shipping build with no panel open pays
    /// only a static bool read per instance per frame.
    /// </summary>
    [Fact]
    public void Disabled_TheTickPathRecordsNothing()
    {
        using var fixture = MakeFixtureWith(out var entity);

        fixture.TickFrame(0.016f);
        fixture.TickFrame(0.016f);

        Assert.Null(BlueprintAssetTick.Get(FakeInstanceBp.BlueprintId, entity));
    }
}
