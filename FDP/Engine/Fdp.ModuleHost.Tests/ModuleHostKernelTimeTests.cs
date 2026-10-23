using System.Linq;
using System;
using Xunit;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Time.Controllers;

namespace Fdp.ModuleHost.Tests
{
    public class ModuleHostKernelTimeTests
    {
        [Fact]
        public void Update_AdvancesGlobalTime()
        {
            // Arrange
            var repository = new EntityRepository();
            var eventAccumulator = new EventAccumulator();
            var kernel = new ModuleHostKernel(repository, eventAccumulator);
            var bus = new FdpEventBus();
            
            repository.RegisterComponent<GlobalTime>();
            repository.SetSingletonUnmanaged(new GlobalTime());
            
            var timeConfig = new TimeControllerConfig
            {
                Role = TimeRole.Standalone,
                Mode = TimeMode.Continuous
            };
            
            var controller = TimeControllerFactory.Create(bus, timeConfig);
            kernel.SetTimeController(controller);
            kernel.InitializeForTest();
            
            // Get initial time
            var timeBefore = kernel.CurrentTime;
            
            // Act
            kernel.Update();
            
            // Assert
            var timeAfter = kernel.CurrentTime;
            Assert.True(timeAfter.FrameNumber > timeBefore.FrameNumber);
            Assert.True(timeAfter.TotalTime > timeBefore.TotalTime);
            Assert.True(timeAfter.DeltaTime > 0);
        }
        
        [Fact]
        public void SetTimeScale_AffectsNextFrame()
        {
            // Arrange
            var repository = new EntityRepository();
            var eventAccumulator = new EventAccumulator();
            var kernel = new ModuleHostKernel(repository, eventAccumulator);
            
            repository.RegisterComponent<GlobalTime>();
            repository.SetSingletonUnmanaged(new GlobalTime());
            
            var bus = new FdpEventBus();
            var timeConfig = new TimeControllerConfig
            {
                Role = TimeRole.Standalone,
                Mode = TimeMode.Continuous
            };
            
            var controller = TimeControllerFactory.Create(bus, timeConfig);
            kernel.SetTimeController(controller);
            kernel.InitializeForTest();
            
            // Act - Pause simulation
            kernel.SetTimeScale(0.0f);
            kernel.Update();
            
            var timePaused = kernel.CurrentTime;
            
            // Assert - Frame advances but delta is zero
            Assert.Equal(0.0f, timePaused.DeltaTime);
            
            // Act - Resume at 2x speed
            kernel.SetTimeScale(2.0f);
            System.Threading.Thread.Sleep(100); // Let some wall time pass
            kernel.Update();
            
            var timeAfter = kernel.CurrentTime;
            
            // Assert - Time is advancing again
            Assert.True(timeAfter.DeltaTime > 0);
            Assert.Equal(2.0f, timeAfter.TimeScale);
        }
        
        [Fact]
        public void ConfigureTime_BeforeInitialize_Succeeds()
        {
            // Arrange
            var repository = new EntityRepository();
            var eventAccumulator = new EventAccumulator();
            var kernel = new ModuleHostKernel(repository, eventAccumulator);
            var bus = new FdpEventBus();
            
            repository.RegisterComponent<GlobalTime>();
            repository.SetSingletonUnmanaged(new GlobalTime());
            
            var timeConfig = new TimeControllerConfig
            {
                Role = TimeRole.Standalone
            };

            var controller = TimeControllerFactory.Create(bus, timeConfig);
            kernel.SetTimeController(controller);
            kernel.SetTimeScale(0.5f);
            
            // Act & Assert - Should not throw
            kernel.InitializeForTest();
            kernel.Update();
            
            var time = kernel.CurrentTime;
            Assert.Equal(0.5f, time.TimeScale);
        }
        
        [Fact]
        public void SetTimeController_AfterInitialize_Throws()
        {
            // Arrange
            var repository = new EntityRepository();
            var eventAccumulator = new EventAccumulator();
            var kernel = new ModuleHostKernel(repository, eventAccumulator);
            var bus = new FdpEventBus();
            
            repository.RegisterComponent<GlobalTime>();
            repository.SetSingletonUnmanaged(new GlobalTime());
            
            var timeConfig = new TimeControllerConfig { Role = TimeRole.Standalone };
            var controller = TimeControllerFactory.Create(bus, timeConfig);
            kernel.SetTimeController(controller);
            kernel.InitializeForTest();
            
            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                kernel.SetTimeController(TimeControllerFactory.Create(bus, new TimeControllerConfig { Role = TimeRole.Master })));
        }
    
        // ── CE-3292 (R-3035) — stepped mode is deterministic: DESIGN_Time_Architecture.md §17 ──────────────────────────

        /// <summary>A clock the test scripts frame by frame: a step (dt &gt; 0) or a paused frame (dt = 0), in a chosen mode.</summary>
        private sealed class ScriptedClock : ITimeController
        {
            public TimeMode Mode = TimeMode.Deterministic;
            public float NextDelta;
            public double Total;
            public long Frame;
            public GlobalTime Update()
            {
                Total += NextDelta;
                if (NextDelta > 0f) Frame++;
                var g = new GlobalTime { DeltaTime = NextDelta, TotalTime = Total, FrameNumber = Frame, TimeScale = 1f };
                NextDelta = 0f;
                return g;
            }
            public void SetTimeScale(float scale) { }
            public float GetTimeScale() => 1f;
            public TimeMode GetMode() => Mode;
            public GlobalTime GetCurrentState() => new() { TotalTime = Total, FrameNumber = Frame };
            public void SeedState(GlobalTime state) { Total = state.TotalTime; Frame = state.FrameNumber; }
            public void Dispose() { }
            /// <summary>⭐ CE-3316 — a snap (SnapAndPause) bumps it, as the real clock does.</summary>
            public long TimelineEpoch { get; set; }
        }

        [ComponentId(107)] private struct StepProbe { public int Value; }

        /// <summary>A 10 Hz background module that records the sim time of each run and, after <see cref="SleepMs"/>, writes
        /// its run count into the probe entity through its command buffer (applied at harvest).</summary>
        [Fdp.ModuleHost.Abstractions.TickMode(Fdp.ModuleHost.Abstractions.TickPolicy.WhenTimeAdvances)]
        private sealed class SlotModule : Fdp.ModuleHost.Abstractions.IEcsModule
        {
            public string Name => "SlotModule";
            public int SleepMs;
            public Entity Probe;
            public readonly System.Collections.Generic.List<double> RunAt = new();
            private int _runs;
            public Fdp.ModuleHost.Abstractions.ExecutionPolicy Policy
                => Fdp.ModuleHost.Abstractions.ExecutionPolicy.SlowBackground(10).WithTimeout(2000);
            public void Tick(Fdp.ModuleHost.Abstractions.ISimulationView view, float deltaTime)
            {
                var repo = (EntityRepository)view;
                lock (RunAt) RunAt.Add(repo.HasSingletonUnmanaged<GlobalTime>() ? repo.GetSingletonUnmanaged<GlobalTime>().TotalTime : -1);
                if (SleepMs > 0) System.Threading.Thread.Sleep(SleepMs);
                int n = System.Threading.Interlocked.Increment(ref _runs);
                if (!Probe.IsNull) view.GetCommandBuffer().SetComponent(Probe, new StepProbe { Value = n });
            }
        }

        private static (ModuleHostKernel kernel, EntityRepository world, ScriptedClock clock, SlotModule module) SteppedKernel(int sleepMs)
        {
            var world = new EntityRepository();
            world.RegisterComponent<GlobalTime>();
            world.RegisterComponent<StepProbe>();
            world.SetSingletonUnmanaged(new GlobalTime());
            var probe = world.CreateEntity();
            world.AddComponent(probe, new StepProbe());
            var kernel = new ModuleHostKernel(world, new EventAccumulator());
            var clock = new ScriptedClock();
            var module = new SlotModule { SleepMs = sleepMs, Probe = probe };
            kernel.SetTimeController(clock);
            kernel.RegisterModule(module);
            kernel.InitializeForTest();
            return (kernel, world, clock, module);
        }

        private static void Step(ModuleHostKernel kernel, ScriptedClock clock, float dt) { clock.NextDelta = dt; kernel.Update(); }

        /// <summary>
        /// ⭐ CE-3292 — in stepped mode a 10 Hz module runs on the same STEPS whether the steps come back to back or with any number
        /// of paused frames between them. 📐 Before: its cadence counted rendered frames (`60/Hz`), so the paused frames moved it.
        /// </summary>
        [Fact]
        public void Stepped_A10HzModule_RunsOnTheSameSimTimeSlots_WhateverThePausedFrames()
        {
            double[] RunTimes(int pausedBetween)
            {
                var (kernel, world, clock, module) = SteppedKernel(sleepMs: 0);
                for (int i = 0; i < 60; i++)
                {
                    Step(kernel, clock, 1f / 60f);
                    for (int p = 0; p < pausedBetween; p++) Step(kernel, clock, 0f);
                }
                Step(kernel, clock, 1f / 60f);   // harvest the last run
                kernel.Dispose(); world.Dispose();
                lock (module.RunAt) return module.RunAt.ToArray();
            }

            var backToBack = RunTimes(0);
            var paused     = RunTimes(7);
            Assert.True(backToBack.Length >= 10, $"only {backToBack.Length} runs in 1 s of sim time at 10 Hz");
            Assert.Equal(backToBack, paused);
            // ⭐ one run per 0.1 s slot of sim time, the first on the first step
            Assert.Equal(11, backToBack.Length);
        }

        /// <summary>
        /// ⭐ CE-3292 — in stepped mode a background job's result lands at the NEXT STEP — never on a paused frame, even when the job
        /// finished during the pause — so the step it lands on does not depend on how long the pause was.
        /// </summary>
        [Fact]
        public void Stepped_ABackgroundResult_LandsAtTheNextStep_NeverOnAPausedFrame()
        {
            var (kernel, world, clock, module) = SteppedKernel(sleepMs: 20);
            Step(kernel, clock, 1f / 60f);                  // step 1: dispatched
            System.Threading.Thread.Sleep(200);             // the job is long finished
            for (int p = 0; p < 5; p++) Step(kernel, clock, 0f);
            Assert.Equal(0, world.GetComponentRO<StepProbe>(module.Probe).Value);   // ⛔ held over the pause
            Step(kernel, clock, 1f / 60f);                  // step 2: harvested
            Assert.Equal(1, world.GetComponentRO<StepProbe>(module.Probe).Value);
            kernel.Dispose(); world.Dispose();
        }

        /// <summary>
        /// ⭐ CE-3292 — and a slow job is WAITED for at the next step: its result is there at step 2 however long it took.
        /// </summary>
        [Fact]
        public void Stepped_ASlowBackgroundJob_IsWaitedForAtTheNextStep()
        {
            var (kernel, world, clock, module) = SteppedKernel(sleepMs: 150);
            Step(kernel, clock, 1f / 60f);                  // step 1: dispatched, still running
            Step(kernel, clock, 1f / 60f);                  // step 2: waits for it
            Assert.Equal(1, world.GetComponentRO<StepProbe>(module.Probe).Value);
            kernel.Dispose(); world.Dispose();
        }

        /// <summary>
        /// ⭐ CE-3292 — a load or a preview Stop snaps the clock BACK: a job dispatched before that belongs to a world that no longer
        /// exists, so its result is discarded, never applied to the new timeline.
        /// </summary>
        [Fact]
        public void TheClockGoingBack_DiscardsAJobFromTheOldTimeline()
        {
            var (kernel, world, clock, module) = SteppedKernel(sleepMs: 100);
            for (int i = 0; i < 6; i++) Step(kernel, clock, 1f / 60f);   // runs dispatched on the old timeline
            clock.Total = 0; clock.Frame = 0;                          // the snap (a load)
            int before = world.GetComponentRO<StepProbe>(module.Probe).Value;
            System.Threading.Thread.Sleep(250);
            Step(kernel, clock, 0f);                                   // the new timeline begins (paused)
            Step(kernel, clock, 1f / 60f);                             // first step: the old job harvested — and discarded
            Assert.Equal(before, world.GetComponentRO<StepProbe>(module.Probe).Value);
            kernel.Dispose(); world.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-3316</c> (restore design §1l) — a slot-paced background run is IN FLIGHT from the step that dispatches it to the step that
        /// harvests it, and only then: what a checkpoint waits out. Not in continuous play (nothing there repeats).
        /// </summary>
        [Fact]
        public void CE3316_ASlotRunIsInFlight_FromItsDispatchToItsHarvest_InSteppedModeOnly()
        {
            var (kernel, world, clock, _) = SteppedKernel(sleepMs: 0);
            var inFlight = new System.Collections.Generic.List<bool>();
            for (int i = 0; i < 8; i++) { Step(kernel, clock, 1f / 60f); inFlight.Add(kernel.SlotRunInFlight); }
            // steps 1 (slot 0) and 6 (slot 1, t = 0.1 s) dispatch; the step after each harvests
            Assert.Equal(new[] { true, false, false, false, false, true, false, false }, inFlight);
            clock.Mode = TimeMode.Continuous;
            for (int i = 0; i < 12; i++) { Step(kernel, clock, 1f / 60f); Assert.False(kernel.SlotRunInFlight); }
            kernel.Dispose(); world.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3316</c> — a checkpoint written on a quiet step (0.15 s) and restored into a FRESH kernel runs its slow module on the
        /// SAME steps as the straight run: the restore counts the slot it stands in as run. 📌 Found by R1 on <c>ua-civ-melee</c>: restored,
        /// every module counted as never run and dispatched on the first step — a hearing solve a step late, a threat boost off by 5.
        /// A load (no <c>ResumeSlotsAfterRestore</c>) still runs on the first step. 🔴 Red-proof: the kernel ignores the resume ⇒ an extra
        /// run at 0.1667 s.
        /// </summary>
        [Fact]
        public void CE3316_ARestoredKernel_RunsItsSlowModulesOnTheStraightRunsSteps()
        {
            double[] After(double[] runs, double t) => runs.Where(r => r > t + 1e-9).ToArray();

            var (straight, sw, sc, sm) = SteppedKernel(sleepMs: 0);
            for (int i = 0; i < 9; i++) Step(straight, sc, 1f / 60f);
            double checkpointAt = sc.Total;                                  // the quiet step the checkpoint was written on
            for (int i = 0; i < 21; i++) Step(straight, sc, 1f / 60f);
            Step(straight, sc, 1f / 60f);
            straight.Dispose(); sw.Dispose();
            double[] straightRuns; lock (sm.RunAt) straightRuns = sm.RunAt.ToArray();

            double[] Restored(bool resume)
            {
                var (kernel, world, clock, module) = SteppedKernel(sleepMs: 0);
                Step(kernel, clock, 0f);                                    // the fresh process, paused
                if (resume) kernel.ResumeSlotsAfterRestore();               // PutBack, for a checkpoint
                clock.Total = checkpointAt; clock.Frame = 9; clock.TimelineEpoch++;   // the snap to the checkpoint's clock
                Step(kernel, clock, 0f);
                for (int i = 0; i < 22; i++) Step(kernel, clock, 1f / 60f);
                kernel.Dispose(); world.Dispose();
                lock (module.RunAt) return module.RunAt.ToArray();
            }

            Assert.Equal(After(straightRuns, checkpointAt), Restored(resume: true));
            Assert.Equal(10.0 / 60.0, Restored(resume: false)[0], 5);      // a load-like snap: the first step runs it
        }

        [EventId(932961)] private struct HeardPing { public int Value; }

        /// <summary>
        /// ⭐ <c>CE-3316</c> (D2's checkpoint half) — the checkpoint's UNCONSUMED events (its recorded pending buffer, handed over in a holding
        /// bus) are delivered at the restore's clock snap: in the live bus's read buffer on the snap frame — after the history restart, so
        /// not erased — and only that frame (read once). 📌 Found by R1 on <c>ua-civ-melee</c>: a heard contact published in the checkpoint's
        /// frame was lost, and the Policeman's threat score came out 5 lower. 🔴 Red-proof: drop the publish at the snap ⇒ nothing arrives.
        /// </summary>
        [Fact]
        public void CE3316_ACheckpointsUnconsumedEvents_ArriveOnceAtTheRestoresSnap()
        {
            var (kernel, world, clock, _) = SteppedKernel(sleepMs: 0);
            world.RegisterEvent<HeardPing>();
            Step(kernel, clock, 0f);
            var holding = new FdpEventBus();
            holding.Register<HeardPing>();
            holding.Publish(new HeardPing { Value = 5 });
            holding.SwapBuffers();                                     // the keyframe's pending events, read into the holding bus's current
            kernel.ResumeAfterRestore(holding);
            clock.Total = 3.0; clock.Frame = 180; clock.TimelineEpoch++;   // the snap to the checkpoint's clock
            Step(kernel, clock, 0f);
            Assert.Equal(new[] { 5 }, world.ReadEvents<HeardPing>().ToArray().Select(p => p.Value).ToArray());
            Step(kernel, clock, 1f / 60f);
            Assert.Empty(world.ReadEvents<HeardPing>().ToArray());   // delivered once
            kernel.Dispose(); world.Dispose();
        }

        /// <summary>⭐ CE-3292 — live play is NOT touched: in continuous mode the cadence stays frame-counted (it eases the CPU).</summary>
        [Fact]
        public void Continuous_TheCadenceStaysFrameCounted()
        {
            var (kernel, world, clock, module) = SteppedKernel(sleepMs: 0);
            clock.Mode = TimeMode.Continuous;
            for (int i = 0; i < 60; i++) { Step(kernel, clock, 1f / 60f); System.Threading.Thread.Sleep(1); }
            kernel.Dispose(); world.Dispose();
            lock (module.RunAt) Assert.True(module.RunAt.Count >= 5, $"frame-counted 10 Hz ran {module.RunAt.Count} times in 60 frames");
        }

        /// <summary>
        /// ⭐ CE-3293 S5 (R-3036 rule 2, §10 F11) — at time scale 0 in CONTINUOUS mode a step-only background module is held too:
        /// the gate does not depend on the mode. 🔴 Before: held only in stepped mode, so it ran on every 6th paused frame.
        /// </summary>
        [Fact]
        public void Continuous_AtTimeScaleZero_AStepOnlyBackgroundModuleIsHeld()
        {
            var (kernel, world, clock, module) = SteppedKernel(sleepMs: 0);
            clock.Mode = TimeMode.Continuous;
            for (int i = 0; i < 60; i++) { Step(kernel, clock, 0f); System.Threading.Thread.Sleep(1); }   // paused-by-scale frames
            kernel.Dispose(); world.Dispose();
            lock (module.RunAt) Assert.Empty(module.RunAt);
        }
    }
}

