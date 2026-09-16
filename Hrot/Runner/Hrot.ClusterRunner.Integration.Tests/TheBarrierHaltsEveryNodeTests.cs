using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-029</c> — the slice-4 barrier proof, on a REAL multi-node cluster, and the one
/// measurement <c>DQ30</c> asked for and never got: <b><c>k</c></b>.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_Editor_Sharing_Slice4_Debug_PauseStep.md</c> §7 · §10.7 *(which recorded
/// this as NOT discharged)* · <c>docs/UX/Design_Question_30_Debug_Pause_Resume.md</c> §3 risk 3 —
/// *"measure `k` once during implementation … do not treat 'small' as verified."*</para>
///
/// <para>🔴🔴 <b>The slice-4 report got the framing wrong and this file is the correction.</b> It said the
/// barrier *"needs a live multi-node cluster, which no suite here boots"*. 📐 **`HrotRunnerHarness` boots
/// `Orchestrator` + `SimHost` + `IG` + `ExCon` + `CGF` as separate subsystems on one real CycloneDDS
/// domain** — the orchestrator holds the only `MasterSyncController`, CGF and SimHost hold
/// `SlaveSyncController`s. ⇒ ⭐⭐ **that IS the multi-node cluster**, and the barrier is provable here.
/// *(User correction, `2026-08-25`: "`--mode all` is multi-node.")*</para>
///
/// <para>⭐⭐ <b>Why this is the real proof and the slice-4 unit rails are not.</b> Those drive a real
/// `FdpEventBus` and real togglable groups in-process — they prove the halt, the latch and that an
/// intent is PUBLISHED. ⛔ They cannot prove the intent is CARRIED: CGF → `ClusterOpEgressTranslator` →
/// DDS → orchestrator → `MasterSyncController.SwitchToDeterministic(roster)` → DDS →
/// `SwitchTimeModeEvent` back onto CGF's bus. ⭐ Assertion 2 below is exactly that round trip, and
/// nothing short of a real cluster can make it.</para>
/// </summary>
public sealed class TheBarrierHaltsEveryNodeTests
{
    private readonly ITestOutputHelper _out;

    public TheBarrierHaltsEveryNodeTests(ITestOutputHelper output) => _out = output;

    private const string HitTag = "ce029-barrier-probe";

    /// <summary>100-ns ticks → milliseconds, for the recorded <c>k</c>.</summary>
    private static double TicksToMs(long ticks) => ticks / 10_000.0;

    private static GlobalTime ClockOf(EntityRepository repo)
        => repo.HasSingletonUnmanaged<GlobalTime>()
            ? repo.GetSingletonUnmanaged<GlobalTime>()
            : default;

    /// <summary>
    /// ⭐ Any entity in CGF's world will do as the hit's subject: a bare
    /// <see cref="ExternalHitTagPredicateDto"/> has no remaining delegate, so it fires on the tag
    /// alone. ⚠ Stated so nobody reads this rail as asserting anything about the ENTITY — it is about
    /// the time control the hit triggers.
    /// </summary>
    private static Entity AnyEntityIn(EntityRepository repo)
    {
        foreach (var e in repo.Query().Build()) return e;
        return repo.CreateEntity();
    }

    [Fact]
    public void ABreakpointOnCgfFreezesTheWholeClusterThenStepsThenResumes()
    {
        using var h = new HrotRunnerHarness();

        var cgf = h.Cgf;
        Assert.NotNull(cgf);

        var bp = cgf!.DataBreakpointManager;
        Assert.NotNull(bp);

        var controller = cgf.DebugTimeController;
        Assert.NotNull(controller);           // ⛔ would be null if the no-op were still wired

        var cgfWorld = cgf.World;
        var simWorld = h.SimHost.World;
        Assert.NotNull(cgfWorld);
        Assert.NotNull(simWorld);

        // ── let the cluster settle into continuous operation ────────────────────
        h.PumpFrames(20);
        Assert.True(controller!.SimGroupsEnabled, "the brain should be running before the breakpoint");

        // ══ ① THE HALT — exact, local, immediate ════════════════════════════════
        bp!.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = HitTag });
        bp.OnExternalHit(HitTag, AnyEntityIn(cgfWorld!));

        Assert.True(bp.IsPaused);
        Assert.False(controller.SimGroupsEnabled);        // the brain stopped AT the hit tick
        Assert.True(controller.IsWorldStateFrozen);       // and world-state ingress is gated

        long haltTick = bp.PausedTick;
        Assert.True(haltTick > 0, "PausedTick should carry the wall-tick anchor of the hit");

        // ══ ② THE BARRIER — the round trip only a real cluster can make ═════════
        bool answered = h.PumpUntil(() => controller.ClusterPauseRequested, timeoutFrames: 400);

        long barrier = controller.ClusterBarrierWallTicks;
        double kMs   = TicksToMs(barrier - haltTick);

        _out.WriteLine($"[CE-029] cluster answered      : {answered}");
        _out.WriteLine($"[CE-029] CGF halt wall-ticks   : {haltTick}");
        _out.WriteLine($"[CE-029] cluster barrier ticks : {barrier}");
        _out.WriteLine($"[CE-029] k                     : {barrier - haltTick} ticks = {kMs:F1} ms");

        Assert.True(answered,
            "the freeze request must reach the orchestrator's MasterSyncController and come back as a " +
            "SwitchTimeModeEvent — that round trip IS the barrier");

        // ⭐⭐ k is MEASURED here, not assumed. The bound is deliberately loose: this asserts the
        //    barrier is a real future anchor (DQ30's ~200 ms lookahead), not that a particular
        //    latency holds on particular hardware.
        Assert.True(barrier > 0, "a barrier anchor must have been published");
        Assert.True(kMs > 0, $"the barrier must be AHEAD of the halt tick; k={kMs:F1} ms");
        Assert.True(kMs < 5_000, $"k should be the barrier lookahead, not seconds; k={kMs:F1} ms");

        // ══ ③ EVERY NODE HALTS ON THE SAME TICK ════════════════════════════════
        bool bothHalted = h.PumpUntil(
            () => ClockOf(cgfWorld!).IsHalted && ClockOf(simWorld!).IsHalted,
            timeoutFrames: 400);

        var cgfClock = ClockOf(cgfWorld!);
        var simClock = ClockOf(simWorld!);
        _out.WriteLine($"[CE-029] both halted           : {bothHalted}");
        _out.WriteLine($"[CE-029] CGF simTime           : {cgfClock.TotalTime:F4}  (halted={cgfClock.IsHalted})");
        _out.WriteLine($"[CE-029] SimHost simTime       : {simClock.TotalTime:F4}  (halted={simClock.IsHalted})");

        Assert.True(bothHalted,
            "the barrier means every node stops; a node still advancing is DQ30-D's 'step CGF alone' " +
            "failure, where the brain would tick against a frozen world");

        // ⭐ "the same tick" = the same SIMULATION time, not the same wall moment: ruling 61 accepts
        //   that nodes reach the barrier at different wall-clock instants.
        Assert.True(Math.Abs(cgfClock.TotalTime - simClock.TotalTime) < 0.5,
            $"nodes must halt on the same simulation tick: CGF={cgfClock.TotalTime:F4} " +
            $"SimHost={simClock.TotalTime:F4}");

        // ══ ④ STEP — and the ACCEPTED discontinuity, pinned with a real number ══
        //
        // 🔴🔴 The first version of this rail asserted "a step must never move simulation time
        //    BACKWARDS" and it went RED: 5.5553 → 5.4872. ⛔ The assertion was wrong, and `DQ30` §B
        //    says so outright about the zero-dt snap it DECIDED: *"a cooldown or timer started at T is
        //    instantly k ticks OLDER when the clock snaps … it is a real discontinuity, and it is the
        //    price of not rewinding the world."* ⇒ ⭐⭐ backwards is the DESIGNED behaviour, because CGF's
        //    kernel keeps ticking through the barrier window (DQ30-A) while the master's authoritative
        //    sim time does not, and the snap adopts the master's.
        //
        // ⇒ ⭐⭐⭐ so this asserts the accepted cost is BOUNDED rather than absent — which is the claim
        //    §B actually makes, and the one worth defending against regression.
        double cgfBeforeStep = cgfClock.TotalTime;

        bp.RequestStep();
        h.PumpFrames(40);

        var cgfAfterStep = ClockOf(cgfWorld!);
        double jumpMs = Math.Abs(cgfAfterStep.TotalTime - cgfBeforeStep) * 1000.0;
        _out.WriteLine($"[CE-029] CGF simTime after step: {cgfAfterStep.TotalTime:F4}");
        _out.WriteLine($"[CE-029] snap discontinuity     : {jumpMs:F1} ms (DQ30-B's accepted cost)");

        // ⚠ Bounded by the barrier window plus a generous margin — ⛔ NOT by "tens of ms", which is what
        //   §B assumed and this batch measured to be optimistic (see the design's §10.8).
        Assert.True(jumpMs < 2_000,
            $"the snap discontinuity must stay within the barrier window, not seconds; {jumpMs:F1} ms");

        // 🔴 Measured, and NOT what a second wrong assertion here first claimed: `RequestStep` calls
        //    `ClearPausedState()`, so `IDataBreakpointManager.IsPaused` is FALSE after a step. ⇒ what
        //    still holds the world is the SIM-GROUP LATCH and the cluster's deterministic mode — which
        //    is what design §3b actually describes ("groups ON for 1 tick → OFF again").
        Assert.False(bp.IsPaused,
            "measured contract: RequestStep clears the manager's paused state (ClearPausedState)");
        Assert.False(controller.SimGroupsEnabled,
            "the step latch must be down again — a latch outliving the frame is a silent resume");
        Assert.True(controller.ClusterPauseRequested,
            "and the CLUSTER must still be deterministic: a step is not a resume");

        // ══ ⑤ RESUME — running again ════════════════════════════════════════════
        //
        // ⛔⛔ NOT `bp.RequestContinue()`, and this is a finding rather than a style choice: it opens
        //    with `if (!_isPaused) return;`, and the step above already cleared that flag ⇒ after a
        //    step it is a NO-OP and the brain would stay halted forever.
        // ⭐⭐ Production does not use it either — `BlueprintDebugSession.Continue()` goes straight to
        //    `_timeController.RequestResume()` (📌 the same shape `M-41` measured for the drain: the
        //    manager's request pair is bypassed by the surface that actually drives it).
        controller.RequestResume();

        bool running = h.PumpUntil(() => controller.SimGroupsEnabled, timeoutFrames: 400);
        _out.WriteLine($"[CE-029] running after resume  : {running}");

        Assert.True(running, "resume must re-enable the brain once the cluster's Continuous event lands");
        Assert.False(controller.IsWorldStateFrozen, "and world-state ingress must resume with it");
    }
}
