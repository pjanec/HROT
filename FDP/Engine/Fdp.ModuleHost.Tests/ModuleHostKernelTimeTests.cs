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

        /// <summary>⭐ CE-3292 — live play is NOT touched: in continuous mode the cadence stays frame-counted (it eases the CPU).</summary>
        [Fact]
        public void Continuous_TheCadenceStaysFrameCounted()
        {
            var (kernel, world, clock, module) = SteppedKernel(sleepMs: 0);
            clock.Mode = TimeMode.Continuous;
            for (int i = 0; i < 60; i++) { Step(kernel, clock, 0f); System.Threading.Thread.Sleep(1); }   // paused-by-scale frames
            kernel.Dispose(); world.Dispose();
            lock (module.RunAt) Assert.True(module.RunAt.Count >= 5, $"frame-counted 10 Hz ran {module.RunAt.Count} times in 60 frames");
        }
    }
}

