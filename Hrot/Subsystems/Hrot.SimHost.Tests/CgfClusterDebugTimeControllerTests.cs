using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using Hrot.CGF.Debug;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐⭐ <b>cgf==editor slice 4 (<c>DQ30</c>) — a breakpoint hit on CGF actually pauses, steps and
/// resumes.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_Editor_Sharing_Slice4_Debug_PauseStep.md</c> §6 ·
/// <c>docs/UX/UX_Feature_Cgf_Brain_Diagnostics.md</c> §3a/§3b/§3c ·
/// <c>docs/UX/Design_Question_30_Debug_Pause_Resume.md</c> §A–§E.</para>
///
/// <para>⛔ <b>What these rails deliberately do NOT claim.</b> They exercise the controller against a
/// real <c>FdpEventBus</c> and real togglable groups, so they prove the halt actuator, the step latch
/// and the intent traffic. ⚠ They cannot prove the cluster-wide barrier — that needs slaves, and the
/// report says which suite covers it and which does not.</para>
/// </summary>
public sealed class CgfClusterDebugTimeControllerTests
{
    // ── the harness ─────────────────────────────────────────────────────────────

    /// <summary>A system that counts its executions, so "exactly one tick" is measurable.</summary>
    private sealed class CountingSystem : IEcsModuleSystem
    {
        public int Executions;
        public void Execute(ISimulationView view, float deltaTime) => Executions++;
    }

    private sealed class Harness
    {
        public readonly FdpEventBus Bus = new();
        public readonly TogglableInputGroup Input;
        public readonly TogglableSimulationGroup Sim;
        public readonly CountingSystem Brain = new();
        public readonly List<string> Logs = new();
        public bool HasCluster = true;

        public readonly CgfClusterDebugTimeController Controller;

        public Harness(int unansweredFreezeFrames = 3)
        {
            OrchestrationEventRegistry.RegisterAll(Bus);

            Input = new TogglableInputGroup("CgfInput", new IEcsModuleSystem[] { new CountingSystem() });
            Sim   = new TogglableSimulationGroup("CgfSimulation", new IEcsModuleSystem[] { Brain });

            Controller = new CgfClusterDebugTimeController(
                controlBus:             Bus,
                inputGroup:             Input,
                simGroup:               Sim,
                hasCluster:             () => HasCluster,
                log:                    Logs.Add,
                unansweredFreezeFrames: unansweredFreezeFrames);
        }

        /// <summary>
        /// One frame in the shape <c>CgfSubsystem.Update</c> runs it: observe, then the step bracket
        /// around the tick. ⭐ The bracket is the thing under test, so the rails must use the real one.
        /// </summary>
        public void Frame()
        {
            Bus.SwapBuffers();
            Controller.ObserveClusterTime();
            Controller.BeginFrame();
            Sim.Execute(null!, 1f / 60f);
            Controller.EndFrame();
        }

        /// <summary>Publishes a mode event the way the time translators would.</summary>
        public void ClusterSays(TimeMode mode)
            => Bus.Publish(new SwitchTimeModeEvent
            {
                TargetMode      = mode,
                TimeScale       = 1f,
                SimTimeSnapshot = mode == TimeMode.Continuous ? 12.5 : 0.0,
            });

        public int PauseIntents  => CountAfterSwap<PauseTimeIntent>();
        public int ResumeIntents => CountAfterSwap<ResumeTimeIntent>();

        public List<float> StepDeltas()
        {
            Bus.SwapBuffers();
            var deltas = new List<float>();
            foreach (var i in Bus.ReadManaged<StepTimeIntent>()) deltas.Add(i.DeltaSeconds);
            return deltas;
        }

        private int CountAfterSwap<T>()
        {
            Bus.SwapBuffers();
            return Bus.ReadManaged<T>().Count;
        }
    }

    // ── ① the halt: exact locally, requested cluster-wide ───────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The acceptance criterion's first half.</b> A pause halts this node's brain AT ONCE —
    /// exact at the breakpoint tick, ruling 61 — and asks the master to freeze the cluster.
    ///
    /// <para>⛔ Both halves matter. Halting only locally would leave the cluster running past the
    /// breakpoint (ruling 62 says the hit freezes the WHOLE cluster); asking only the master would let
    /// the brain run on for the whole barrier window, so the breakpoint would report a tick the node
    /// had already passed.</para>
    /// </summary>
    [Fact]
    public void APauseHaltsTheBrainAtOnceAndAsksTheMaster()
    {
        var h = new Harness();
        Assert.True(h.Sim.Enabled);

        h.Controller.RequestPause();

        Assert.False(h.Sim.Enabled);
        Assert.False(h.Input.Enabled);
        Assert.True(h.Controller.IsWorldStateFrozen);
        Assert.Equal(1, h.PauseIntents);
    }

    /// <summary>
    /// ⭐⭐ <b>The halt actually stops the brain</b> — the toggle is not merely a flag someone else has
    /// to honour. Frames keep running (the kernel must, or the resume could never arrive) and the
    /// counted system does not advance.
    /// </summary>
    [Fact]
    public void WhileHaltedTheBrainDoesNotAdvanceThoughFramesKeepRunning()
    {
        var h = new Harness(unansweredFreezeFrames: 99);
        h.Controller.RequestPause();
        h.ClusterSays(TimeMode.Deterministic);

        for (int i = 0; i < 5; i++) h.Frame();

        Assert.Equal(0, h.Brain.Executions);
    }

    // ── ② the step: EXACTLY one tick ────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>THE anti-silent-resume rail — the design's second named risk.</b> A step advances the
    /// brain exactly once and the groups are down again afterwards, so the next frame does nothing.
    ///
    /// <para>⛔ *"A latched re-enable that survives a frame boundary silently turns a step into a
    /// resume — and the operator would read the resulting state as 'one step'."* ⇒ five frames after
    /// one step must still show ONE execution, not five.</para>
    /// </summary>
    [Fact]
    public void AStepAdvancesTheBrainExactlyOnceAndNotAgain()
    {
        var h = new Harness(unansweredFreezeFrames: 99);
        h.Controller.RequestPause();
        h.ClusterSays(TimeMode.Deterministic);
        h.Frame();
        Assert.Equal(0, h.Brain.Executions);

        h.Controller.RequestStepOneTick();
        h.Frame();
        Assert.Equal(1, h.Brain.Executions);

        for (int i = 0; i < 4; i++) h.Frame();
        Assert.Equal(1, h.Brain.Executions);
        Assert.False(h.Sim.Enabled);
    }

    /// <summary>⭐ A step asks the master for one deterministic tick, at the fixed 60 Hz delta.</summary>
    [Fact]
    public void AStepAsksTheMasterForOneDeterministicTick()
    {
        var h = new Harness();
        h.Controller.RequestPause();
        h.Controller.RequestStepOneTick();

        var deltas = h.StepDeltas();
        Assert.Single(deltas);
        Assert.Equal(1f / 60f, deltas[0], 5);
    }

    /// <summary>
    /// ⚠ <b>A step is meaningless while the brain is running, and must not silently become one.</b>
    /// ⛔ Publishing a step intent here would ask the cluster to enter deterministic mode as a
    /// side-effect of a stray call.
    /// </summary>
    [Fact]
    public void AStepIsRefusedWhenNothingIsHalted()
    {
        var h = new Harness();

        h.Controller.RequestStepOneTick();

        Assert.Empty(h.StepDeltas());
        Assert.True(h.Sim.Enabled);
    }

    // ── ③ the resume: through the cluster's own mode event ──────────────────────

    /// <summary>
    /// ⭐⭐ <b>A resume waits for this node's own <c>SwitchTimeModeEvent</c>.</b> That is the event whose
    /// <c>ApplyResume</c> → <c>ApplyTimeSnap</c> is <c>DQ30-B</c>'s zero-dt snap; re-enabling the brain
    /// before it would run ticks against a sim-time baseline the master has not yet re-anchored.
    /// </summary>
    [Fact]
    public void AResumeWaitsForTheClustersOwnModeEvent()
    {
        var h = new Harness(unansweredFreezeFrames: 99);
        h.Controller.RequestPause();
        h.ClusterSays(TimeMode.Deterministic);
        h.Frame();

        h.Controller.RequestResume();
        Assert.Equal(1, h.ResumeIntents);

        h.Frame();
        Assert.False(h.Sim.Enabled);            // ⛔ not yet — nobody has answered
        Assert.True(h.Controller.IsWorldStateFrozen);

        h.ClusterSays(TimeMode.Continuous);
        h.Frame();

        Assert.True(h.Sim.Enabled);
        Assert.False(h.Controller.IsWorldStateFrozen);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>An operator's own resume must not release a breakpoint.</b> The debugger owns the world
    /// while it holds it (<c>HaltReason.HeldByBreakpoint</c> outranks the step state for exactly this
    /// reason), so a Continuous event this controller did not ask for leaves the brain halted.
    /// </summary>
    [Fact]
    public void AClusterResumeWeDidNotAskForDoesNotReleaseTheBreakpoint()
    {
        var h = new Harness(unansweredFreezeFrames: 99);
        h.Controller.RequestPause();

        h.ClusterSays(TimeMode.Continuous);
        h.Frame();

        Assert.False(h.Sim.Enabled);
        Assert.True(h.Controller.IsWorldStateFrozen);
        Assert.Equal(0, h.Brain.Executions);
    }

    // ── ④ DQ30-E: the unanswered freeze ─────────────────────────────────────────

    /// <summary>
    /// 🔒 <b><c>DQ30-E</c> / ruling 64 — halt locally anyway and SAY the cluster is still running.</b>
    /// ⛔ A LOG, never a modal: a headless origin logs (ruling 53, the CE-024 correction).
    /// ⚠ And <b>once</b> — a line per frame would bury the log it is meant to stand out in.
    /// </summary>
    [Fact]
    public void AnUnansweredFreezeLogsOnceAndStaysHalted()
    {
        var h = new Harness(unansweredFreezeFrames: 3);
        h.Controller.RequestPause();

        for (int i = 0; i < 20; i++) h.Frame();

        Assert.Single(h.Logs);
        Assert.Contains("cluster is still running", h.Logs[0]);
        Assert.False(h.Sim.Enabled);
    }

    /// <summary>⭐ An ANSWERED freeze never reports one — the deadline is cancelled by the mode event.</summary>
    [Fact]
    public void AnAnsweredFreezeNeverLogs()
    {
        var h = new Harness(unansweredFreezeFrames: 3);
        h.Controller.RequestPause();
        h.ClusterSays(TimeMode.Deterministic);

        for (int i = 0; i < 20; i++) h.Frame();

        Assert.Empty(h.Logs);
    }

    /// <summary>
    /// 🔒 <b>The documented no-DDS mode is NORMAL operation, not a degraded one</b>
    /// (<c>CgfApplication.cs:107</c>). ⛔ No warning, and no intent published to a cluster that does
    /// not exist — *"a permanent warning in a supported mode is ruling 49's dead affordance in another
    /// costume."*
    /// </summary>
    [Fact]
    public void WithNoClusterAPauseIsSilentAndPublishesNothing()
    {
        var h = new Harness(unansweredFreezeFrames: 2) { HasCluster = false };

        h.Controller.RequestPause();
        for (int i = 0; i < 20; i++) h.Frame();

        Assert.False(h.Sim.Enabled);
        Assert.Empty(h.Logs);
        Assert.Equal(0, h.PauseIntents);
    }

    /// <summary>
    /// ⭐⭐ <b>The mirror of <c>DQ30-E</c>, and it is load-bearing.</b> With no cluster no mode event can
    /// ever arrive, so waiting for one would leave an offline node halted for good — a worse failure
    /// than the one E is about. ⇒ the resume applies locally and at once.
    /// </summary>
    [Fact]
    public void WithNoClusterAResumeAppliesLocallyAtOnce()
    {
        var h = new Harness { HasCluster = false };
        h.Controller.RequestPause();

        h.Controller.RequestResume();

        Assert.True(h.Sim.Enabled);
        Assert.False(h.Controller.IsWorldStateFrozen);
        Assert.Equal(0, h.ResumeIntents);
    }

    /// <summary>⚠ And with no cluster a step still steps — locally, exactly once.</summary>
    [Fact]
    public void WithNoClusterAStepStillAdvancesExactlyOneTick()
    {
        var h = new Harness(unansweredFreezeFrames: 99) { HasCluster = false };
        h.Controller.RequestPause();

        h.Controller.RequestStepOneTick();
        h.Frame();
        h.Frame();

        Assert.Equal(1, h.Brain.Executions);
        Assert.Empty(h.StepDeltas());
    }
}
