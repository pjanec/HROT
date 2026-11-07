using Fdp.Core;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.RunMode;
using Xunit;

namespace Hrot.Presentation.Tests.RunMode;

/// <summary>
/// ⭐⭐ <c>CE-4316</c> — scenario lifecycle LC-10, <i>Run to …</i> (option A, user-approved): the ACK-gated stepped run — one 1/60 step per frame,
/// the next only once the last has LANDED (no ACKs pending, the clock moved) — until a time or a condition, then paused; the frame cap lifted only
/// while it runs; refused outside Preview; leaving Preview ends it. ✅ Red-proof: the landed-gate removed ⇒ the "one step in flight" assert red.
/// </summary>
public sealed class RunToControllerTests
{
    /// <summary>A transport whose step lands when the test says so (the master's barrier).</summary>
    private sealed class FakeCluster : ITimeTransportFacade
    {
        public bool IsPaused { get; set; } = true;
        public float TimeScale { get; set; } = 1f;
        public double TotalTime { get; set; }
        public bool IsPlayPauseEnabled => true;
        public bool IsStepEnabled => true;
        public bool IsStopEnabled => true;
        public int Steps, InFlight;
        public float Pending;
        public void TogglePlayPause() => IsPaused = !IsPaused;
        public void Step() => StepBy(1f / 60f);
        public void StepBy(float seconds)
        {
            if (!IsPaused) { IsPaused = true; return; }
            Steps++; InFlight++; Pending += seconds;
        }
        /// <summary>The barrier drains: every step asked lands.</summary>
        public void Land() { TotalTime += Pending; Pending = 0; InFlight = 0; }
        public void Stop() { }
        public void SetTimeScale(float s) => TimeScale = s;
        public TransportMode Mode { get; set; }
        public Fdp.Toolkit.Orchestration.ReplayAxis Axis { get; set; }
        public void SetAxis(Fdp.Toolkit.Orchestration.ReplayAxis axis) => Axis = axis;
        public void EnterReview(double at) { }
        public void ExitReview() { }
        public Fdp.Toolkit.Orchestration.ReplaySession? Session { get; set; }
        public void EnterReviewAtSim(double simSeconds) { }
        public double? LivePoint { get; set; }
        public void Seek(double t) { }
        public void SetLoop(double from, double to) { }
        public void ClearLoop() { }
        public (double From, double To)? Loop => null;
    }

    private sealed class Flag { public bool On; }

    private static (RunToController Run, FakeCluster Cluster, Flag Preview) Make(bool preview = true)
    {
        var cluster = new FakeCluster();
        var inPreview = new Flag { On = preview };
        var run = new RunToController(cluster, () => cluster.InFlight > 0, () => inPreview.On, () => new EntityRepository());
        return (run, cluster, inPreview);
    }

    [Fact]
    public void CE4316_RunsToATime_OneLandedStepAtATime_ThenStaysPaused_AndTheCapIsLiftedOnlyWhileRunning()
    {
        var (run, cluster, _) = Make();
        Assert.True(run.Start(atSeconds: 0.1, until: null, stepSeconds: RunToController.StepSeconds));
        Assert.True(FramePacing.Unthrottled);

        run.Tick();
        Assert.Equal(1, cluster.Steps);
        run.Tick(); run.Tick();                               // the step has not landed: nothing more is asked
        Assert.Equal(1, cluster.Steps);
        for (int i = 0; i < 20 && run.IsRunning; i++) { cluster.Land(); run.Tick(); }

        Assert.False(run.IsRunning);
        Assert.Equal(6, cluster.Steps);                       // 0.1 s = six 1/60 steps, deterministic
        Assert.True(cluster.IsPaused);
        Assert.StartsWith("reached", run.Status);
        Assert.False(FramePacing.Unthrottled);
    }

    [Fact]
    public void CE4316_RunsUntilACondition_AndIsRefusedOutsidePreview_AndLeavingPreviewEndsIt()
    {
        var (run, cluster, _) = Make();
        Assert.True(run.Start(atSeconds: null, until: (_, _) => cluster.TotalTime >= 0.05, stepSeconds: RunToController.StepSeconds));
        for (int i = 0; i < 20 && run.IsRunning; i++) { run.Tick(); cluster.Land(); }
        Assert.False(run.IsRunning);
        Assert.StartsWith("the condition held", run.Status);
        Assert.InRange(cluster.TotalTime, 0.05, 0.07);

        var (outside, _, _) = Make(preview: false);
        Assert.False(outside.Start(atSeconds: 10, until: null));
        Assert.Contains("Preview", outside.Status);
        Assert.False(run.Start(atSeconds: 0.01, until: null));   // not ahead of the clock

        var (left, _, preview) = Make();
        Assert.True(left.Start(atSeconds: 60, until: null));
        preview.On = false;                                   // the user stopped the Preview
        left.Tick();
        Assert.False(left.IsRunning);
        Assert.Equal("left Preview", left.Status);
        Assert.False(FramePacing.Unthrottled);
    }

    [Fact]
    public void CE4316_TheStepIsASettingOfEachRun_NetworkedDefaultsToTheTenXQuantum_AndOutOfRangeIsRefused()
    {
        var (run, cluster, _) = Make();
        Assert.False(run.IsTurbo);
        Assert.True(run.Start(atSeconds: 1.0, until: null));                 // networked default: 1/6 s
        Assert.Equal(RunToController.NetworkedStepSeconds, run.Step);
        for (int i = 0; i < 40 && run.IsRunning; i++) { run.Tick(); cluster.Land(); }
        Assert.Equal(6, cluster.Steps);                                       // 1 s = six 1/6 steps
        Assert.InRange(cluster.TotalTime, 0.99, 1.01);

        var (r2, _, _) = Make();
        Assert.False(r2.Start(atSeconds: 5, until: null, stepSeconds: 0.5f));   // past 1/6
        Assert.Contains("outside", r2.Status);
        Assert.False(r2.Start(atSeconds: 5, until: null, stepSeconds: 0f));
        Assert.True(r2.Start(atSeconds: 5, until: null, stepSeconds: 1f / 20f));
        Assert.Equal(1f / 20f, r2.Step);
        r2.Cancel();
    }

    /// <summary>
    /// ⭐⭐ The TURBO strategy (the networkless editor node, DESIGN_Time_Architecture.md §20): ONE rendered frame runs many node frames, each step
    /// a fixed quantum taken at once — and a step is never asked while the last is still in flight (the frame then only waits). ✅ Red-proof: the
    /// landed-gate removed ⇒ steps &gt; landed and the "one in flight" assert red.
    /// </summary>
    [Fact]
    public void CE4316_Turbo_RunsManyFixedStepsInOneRenderedFrame_NeverTwoInFlight_DefaultStepOneSixtieth()
    {
        var cluster = new FakeCluster();
        int frames = 0, steps = 0, inFlight = 0, maxInFlight = 0;
        void Turbo(float seconds, bool step)
        {
            frames++;
            if (inFlight > 0) { cluster.TotalTime += seconds; inFlight = 0; }      // the last step lands one node frame after it is taken
            if (step) { steps++; inFlight++; maxInFlight = System.Math.Max(maxInFlight, inFlight); }
        }
        var run = new RunToController(cluster, () => inFlight > 0, () => true, () => new EntityRepository(), Turbo);
        Assert.True(run.IsTurbo);
        Assert.True(run.Start(atSeconds: 2.0, until: null));
        Assert.Equal(RunToController.StepSeconds, run.Step);                    // turbo default: the live 1/60
        for (int i = 0; i < 1000 && run.IsRunning; i++) run.Tick();
        Assert.False(run.IsRunning);
        Assert.Equal(120, steps);                                               // 2 s = 120 fixed steps, deterministic
        Assert.Equal(1, maxInFlight);
        Assert.True(frames >= 2 * steps - 1, $"each step waited for its frame ({frames} frames, {steps} steps)");
        Assert.StartsWith("reached", run.Status);

        // a bigger quantum: 1/20 s ⇒ 40 steps for the same 2 s, and it all fits one rendered frame here
        cluster.TotalTime = 0; steps = 0; frames = 0;
        Assert.True(run.Start(atSeconds: 2.0, until: null, stepSeconds: 1f / 20f));
        run.Tick();
        Assert.False(run.IsRunning);
        Assert.Equal(40, steps);
        Assert.InRange(cluster.TotalTime, 1.99, 2.01);
        Assert.Equal(0, cluster.Steps);                                         // turbo never goes through the transport's StepBy
    }

    // ── ⭐ W-4 (LC-10 from Edit, the mode menu's Run to …) ────────────────────────────────────────────────────────────────

    [Fact]
    public void W4_FromEdit_StartsThePreviewPaused_ThenRunsToTheTime_AndCancelWhileStartingStopsIt()
    {
        var cluster = new FakeCluster();
        var preview = new Flag { On = false };
        int entered = 0;
        var run = new RunToController(cluster, () => cluster.InFlight > 0, () => preview.On, () => new EntityRepository(),
            enterPreview: () => { entered++; return true; });
        Assert.True(run.CanStartFromEdit);

        Assert.True(run.Request(atSeconds: 0.1, until: null, stepSeconds: RunToController.StepSeconds));
        Assert.Equal(1, entered);                                 // the Preview was asked for, once
        Assert.True(run.IsStarting); Assert.False(run.IsRunning);
        run.Tick(); run.Tick();                                   // the Preview is not up yet: nothing steps
        Assert.Equal(0, cluster.Steps);

        preview.On = true;                                        // the Preview is up, paused at its first frame
        run.Tick();
        Assert.False(run.IsStarting); Assert.True(run.IsRunning);
        for (int i = 0; i < 30 && run.IsRunning; i++) { run.Tick(); cluster.Land(); }
        Assert.StartsWith("reached", run.Status);
        Assert.Equal(6, cluster.Steps);                           // the same deterministic run as Start in a Preview
        Assert.True(cluster.IsPaused);

        // cancelled while the Preview starts: nothing runs when it comes up
        preview.On = false; cluster.Steps = 0;
        Assert.True(run.Request(atSeconds: 5, until: null));
        run.Cancel();
        Assert.False(run.IsStarting);
        preview.On = true; run.Tick();
        Assert.False(run.IsRunning); Assert.Equal(0, cluster.Steps);
    }

    [Fact]
    public void W4_FromEdit_IsRefusedWithoutADoor_WithNothingToRunTo_OrWhenThePreviewCannotStart_AndGivesUpIfItNeverComes()
    {
        var (noDoor, _, _) = Make(preview: false);
        Assert.False(noDoor.CanStartFromEdit);
        Assert.False(noDoor.Request(10, null));
        Assert.Contains("Preview", noDoor.Status);

        var cluster = new FakeCluster();
        bool can = false;
        var run = new RunToController(cluster, () => false, () => false, () => new EntityRepository(), enterPreview: () => can);
        Assert.False(run.Request(null, null));
        Assert.Contains("a time or a condition", run.Status);
        Assert.False(run.Request(10, null));
        Assert.Equal("the Preview cannot start now.", run.Status);

        can = true;
        Assert.True(run.Request(10, null));
        for (int i = 0; i <= RunToController.EnterPreviewFrames; i++) run.Tick();
        Assert.False(run.IsStarting);
        Assert.Equal("the Preview did not start.", run.Status);
    }

    [Theory]
    [InlineData("10:00", 600)]
    [InlineData("1:02:03", 3723)]
    [InlineData("10", 600)]
    [InlineData("0:30", 30)]
    public void W4_TheTimeReadsAsTheClockShowsIt(string text, double seconds)
    {
        Assert.True(RunToChoices.TryParseTime(text, out double s));
        Assert.Equal(seconds, s);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ten")]
    [InlineData("1:75")]
    [InlineData("-3")]
    [InlineData("1:2:3:4")]
    public void W4_ATimeTheClockCannotShowIsRefused(string text) => Assert.False(RunToChoices.TryParseTime(text, out _));
}
