using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Diagnostics.Breakpoints.Tests;

/// <summary>A component with one field the DESIGNER edits and one the SIMULATION owns (ID 261).</summary>
[ComponentId(261)]
internal struct SharedTwoFieldComp
{
    public int Edited;     // what the operator types into the edit dialog
    public int SimOwned;   // what a system writes during the tick the breakpoint interrupted
}

/// <summary>
/// ⭐⭐⭐ <b>Ruling 14 — the staged debug write must not revert the fields it did not touch.</b>
///
/// <para>
/// 🔴🔴 <b>The mechanism, measured on <c>HEAD</c> rather than assumed.</b>
/// <list type="number">
///   <item><c>OnHit</c> captures <c>_postTickSnapshot ← _liveRepo</c>, then rewinds
///   <c>_liveRepo ← _preTickSnapshot</c>.</item>
///   <item>While paused, <c>ActiveView</c> IS <c>_preTickSnapshot</c> — so the edit dialog is seeded
///   with <b>pre-tick</b> values for every field.</item>
///   <item><c>RequestStep</c>/<c>RequestContinue</c> restore <c>_liveRepo ← _postTickSnapshot</c> and
///   <b>then</b> drain.</item>
/// </list>
/// ⇒ a WHOLE-COMPONENT write built from (2) lands on (3) and carries every untouched field back a
/// tick. ⛔ On the shared <c>Blackboard1024</c> that reverts BTree and HSM state, with no diagnostic.
/// </para>
///
/// <para>
/// ⭐ <b>This is the red-first test the handoff required</b>, and it is red for the reason above rather
/// than for a reason a mock invented: it drives the real <c>DataBreakpointManager</c>, the real
/// <c>EntityCommandBuffer</c> and the real <c>EntityRepository</c> end to end.
/// </para>
/// </summary>
[Collection("ComponentRegistry")]
public sealed class SurgicalFieldWriteTests
{
    private static (DataBreakpointManager Manager, EntityRepository Live, Entity Target)
        PausedMidTick(out SharedTwoFieldComp preTickValue)
    {
        ComponentTypeRegistry.Clear();
        var live    = new EntityRepository();
        var preTick = new EntityRepository();
        live.RegisterComponent<SharedTwoFieldComp>();
        preTick.RegisterComponent<SharedTwoFieldComp>();

        var entity = live.CreateEntity();
        live.AddComponent(entity, new SharedTwoFieldComp { Edited = 1, SimOwned = 10 });
        preTick.SyncFrom(live);
        preTickValue = live.GetComponent<SharedTwoFieldComp>(entity);

        // …the tick runs, and a SYSTEM writes the field the operator is not touching.
        live.Tick();
        ref var c = ref live.GetComponentRW<SharedTwoFieldComp>(entity);
        c.SimOwned = 20;

        var manager = new DataBreakpointManager(
            live, preTick, new DebugSnapshotProvider(preTick), new MockDebugTimeController());

        var bpId = manager.Add(new Breakpoint
        {
            Id = BreakpointId.Invalid, Enabled = true, OccurrenceThreshold = 1, DisplayName = "surgical",
        });
        manager.OnHit(manager.AllBreakpoints.First(b => b.Id == bpId), entity);

        return (manager, live, entity);
    }

    /// <summary>
    /// 🔴 <b>RED before ruling 14.</b> The operator edits <c>Edited</c> only; <c>SimOwned</c> must keep
    /// the value the simulation gave it during the interrupted tick. ⭐ Both halves are asserted — a
    /// test that only checked <c>SimOwned</c> would pass for an implementation that dropped the edit.
    /// </summary>
    [Fact]
    public void AFieldEdit_DoesNotRevertTheFieldsTheSimulationChanged()
    {
        var (manager, live, entity) = PausedMidTick(out var seenWhilePaused);

        // What the paused editor showed, and what the operator turned it into.
        Assert.Equal(10, seenWhilePaused.SimOwned);   // ⚠ pre-tick: the stale value the dialog carries
        var edited = seenWhilePaused;
        edited.Edited = 99;

        manager.StageMutation(entity, typeof(SharedTwoFieldComp), edited, baseline: seenWhilePaused);

        var ecb = (EntityCommandBuffer)((ISimulationView)live).GetCommandBuffer();
        manager.RequestStep();
        ecb.Playback(live);

        var result = live.GetComponent<SharedTwoFieldComp>(entity);
        Assert.Equal(99, result.Edited);
        Assert.Equal(20, result.SimOwned);   // 🔴 was 10 — reverted a tick by the whole-component write
    }

    /// <summary>
    /// ⭐ <b>The rail the handoff named: exactly <c>size</c> bytes at <c>byteOffset</c>, and nothing
    /// else.</b> ⚠ Stated over the STAGED mutations rather than only over the outcome — an
    /// implementation that wrote the whole component and happened to get the right answer (because the
    /// baseline was fresh) would satisfy the test above and not this one.
    ///
    /// <para>
    /// 📐 <b>Measured, and the granularity is finer than "one field".</b> The diff is over BYTES, so
    /// editing <c>Edited</c> from <c>1</c> to <c>99</c> stages a <b>one-byte</b> run — the upper three
    /// bytes were <c>0</c> before and after. ⭐ That is strictly better than field granularity and it
    /// is what makes the mechanism need no field-layout knowledge at all: no StructEdit change, no
    /// per-field dirty tracking, and nothing to keep in sync with a struct's real offsets. ⚠ So the
    /// assertion is the INVARIANT — every staged run lies inside the edited field and never inside
    /// the simulation's — not a remembered byte count.
    /// </para>
    /// </summary>
    [Fact]
    public void OnlyTheChangedByteRangeIsStaged()
    {
        var (manager, _, entity) = PausedMidTick(out var seenWhilePaused);

        var edited = seenWhilePaused;
        edited.Edited = 99;
        manager.StageMutation(entity, typeof(SharedTwoFieldComp), edited, baseline: seenWhilePaused);

        var staged = manager.PendingMutationsQueue.ToList();
        Assert.NotEmpty(staged);

        int simOwnedStart = sizeof(int);   // `Edited` occupies [0,4); `SimOwned` starts at 4
        foreach (var m in staged)
        {
            Assert.True(m.IsFieldWrite, "a one-field edit staged a whole-component write.");
            Assert.True(m.ByteOffset >= 0 && m.ByteOffset + m.SizeBytes <= simOwnedStart,
                $"a staged run [{m.ByteOffset},{m.ByteOffset + m.SizeBytes}) reaches into SimOwned's "
                + $"bytes at {simOwnedStart} — the write is not surgical.");
        }

        Assert.Equal(1, staged.Sum(m => m.SizeBytes));   // 1 -> 99 differs in the low byte alone
    }

    /// <summary>
    /// ⚠ <b>An edit that changed nothing stages nothing.</b> The OK button commits whether or not the
    /// designer altered a value, so this is a real path — and a whole-component write on it would
    /// still have reverted the simulation's fields.
    /// </summary>
    [Fact]
    public void AnEditThatChangedNothing_StagesNothing()
    {
        var (manager, _, entity) = PausedMidTick(out var seenWhilePaused);

        manager.StageMutation(
            entity, typeof(SharedTwoFieldComp), seenWhilePaused, baseline: seenWhilePaused);

        Assert.Equal(0, manager.PendingMutationsCount);
    }

    /// <summary>
    /// ⭐ <b>No baseline ⇒ the old behaviour, unchanged.</b> The 4-argument overload is default-
    /// implemented on the interface as a forward to the whole-component write, so every existing
    /// caller and test double keeps working. ⛔ This pins that the fallback is a forward and not a
    /// silent drop.
    /// </summary>
    [Fact]
    public void WithoutABaseline_ItStillStagesTheWholeComponent()
    {
        var (manager, _, entity) = PausedMidTick(out var seenWhilePaused);

        var edited = seenWhilePaused;
        edited.Edited = 99;
        manager.StageMutation(entity, typeof(SharedTwoFieldComp), edited, baseline: null);

        var staged = manager.PendingMutationsQueue.ToList();
        Assert.Single(staged);
        Assert.False(staged[0].IsFieldWrite);
    }
}
