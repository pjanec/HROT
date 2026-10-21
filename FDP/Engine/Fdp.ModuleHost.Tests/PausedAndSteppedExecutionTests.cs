using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Time;
using Xunit;

namespace Fdp.ModuleHost.Tests
{
    /// <summary>
    /// ⭐⭐ CE-3293 S5 (R-3036) — the acceptance rails of docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md §7: a step-only
    /// system is not called on a paused frame and reads, at the next step, every event since its own last run — exactly once,
    /// whatever the number of paused frames; an every-frame system reads each event once, on the frame it becomes readable; a
    /// background module held over a long pause still gets every event.
    /// </summary>
    public class PausedAndSteppedExecutionTests
    {
        [EventId(932950)] public struct Ping { public int Value; }

        private sealed class ScriptedClock : ITimeController
        {
            public float NextDelta;
            public double Total;
            public long Frame;
            public long Epoch;
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
            public TimeMode GetMode() => TimeMode.Deterministic;
            public GlobalTime GetCurrentState() => new() { TotalTime = Total, FrameNumber = Frame };
            public void SeedState(GlobalTime state) { Total = state.TotalTime; Frame = state.FrameNumber; }
            public long TimelineEpoch => Epoch;
            public void Dispose() { }
        }

        /// <summary>Records every <see cref="Ping"/> it reads, per call, with the call's delta.</summary>
        private abstract class PingReader : IEcsModuleSystem
        {
            public readonly List<(float Dt, int[] Values)> Calls = new();
            public void Execute(ISimulationView view, float deltaTime)
                => Calls.Add((deltaTime, ((EntityRepository)view).ReadEvents<Ping>().ToArray().Select(p => p.Value).ToArray()));
            public int[] AllRead => Calls.SelectMany(c => c.Values).ToArray();
        }

        [UpdateInPhase(SystemPhase.PostSimulation)]
        [TickMode(TickPolicy.WhenTimeAdvances)]
        private sealed class StepReader : PingReader { }

        [UpdateInPhase(SystemPhase.PostSimulation)]
        [TickMode(TickPolicy.EveryFrame)]
        private sealed class FrameReader : PingReader { }

        private static (ModuleHostKernel, EntityRepository, ScriptedClock) Kernel(params IEcsModuleSystem[] systems)
        {
            var world = new EntityRepository();
            world.RegisterComponent<GlobalTime>();
            world.SetSingletonUnmanaged(new GlobalTime());
            world.RegisterEvent<Ping>();
            var kernel = new ModuleHostKernel(world, new EventAccumulator());
            var clock = new ScriptedClock();
            kernel.SetTimeController(clock);
            foreach (var s in systems) kernel.RegisterGlobalSystem(s);
            kernel.InitializeForTest();
            return (kernel, world, clock);
        }

        private static void Frame(ModuleHostKernel kernel, ScriptedClock clock, float dt) { clock.NextDelta = dt; kernel.Update(); }

        /// <summary>
        /// ⭐ Acceptance ① and ② — an event published AT a step (readable on the paused frame after it) and one published DURING the
        /// pause both reach the step-only system at the next step, once, in order; it is never called while paused. The every-frame
        /// system reads each one once, on the frame it became readable. 🔴 Before: a step-only system was called on the paused
        /// frame (reacting while paused) — or, with a hand gate (BrainTickSystem), missed the event for good.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(120)]
        public void AStepOnlySystem_ReadsEverythingSinceItsLastRun_AtTheNextStep_Once(int pausedFrames)
        {
            var step = new StepReader();
            var every = new FrameReader();
            var (kernel, world, clock) = Kernel(step, every);
            Frame(kernel, clock, 1f / 60f);                       // warm-up step

            Frame(kernel, clock, 1f / 60f);                       // step N …
            world.Bus.Publish(new Ping { Value = 1 });            // … late in it: readable on the paused frame after
            for (int i = 0; i < pausedFrames; i++)
            {
                if (i == pausedFrames / 2) world.Bus.Publish(new Ping { Value = 2 });   // published while paused
                Frame(kernel, clock, 0f);
            }
            Frame(kernel, clock, 1f / 60f);                       // the next step
            Frame(kernel, clock, 1f / 60f);                       // and one more

            Assert.All(step.Calls, c => Assert.True(c.Dt > 0f, "a step-only system was called on a paused frame"));
            Assert.Equal(new[] { 1, 2 }, step.AllRead);
            Assert.Equal(new[] { 1, 2 }, step.Calls[^2].Values);  // both at the first step after the pause
            Assert.Equal(new[] { 1, 2 }, every.AllRead);          // the every-frame system: each once
            Assert.Equal(1, every.Calls.First(c => c.Values.Length > 0).Values[0]);   // on the first paused frame (with 2 when 1 frame)
            kernel.Dispose(); world.Dispose();
        }

        /// <summary>
        /// ⭐ The determinism the gate exists for (R-3035, §17): a step-only system sees the same events at the same steps whether the
        /// steps come back to back or with any number of paused frames between them.
        /// </summary>
        [Fact]
        public void AStepOnlySystem_SeesTheSameStepsWhateverThePausedFrames()
        {
            int[][] Run(int paused)
            {
                var step = new StepReader();
                var (kernel, world, clock) = Kernel(step);
                for (int s = 0; s < 30; s++)
                {
                    world.Bus.Publish(new Ping { Value = s });
                    Frame(kernel, clock, 1f / 60f);
                    for (int p = 0; p < paused; p++) Frame(kernel, clock, 0f);
                }
                kernel.Dispose(); world.Dispose();
                return step.Calls.Select(c => c.Values).ToArray();
            }

            var backToBack = Run(0);
            Assert.Equal(backToBack.Length, Run(5).Length);
            Assert.Equal(backToBack, Run(5));
            Assert.Equal(backToBack, Run(1));
        }

        /// <summary>⭐ Rule 7 — a SnapAndPause (the clock's timeline epoch) forgets every event from before it.</summary>
        [Fact]
        public void ASnap_ForgetsTheEventsBeforeIt()
        {
            var step = new StepReader();
            var (kernel, world, clock) = Kernel(step);
            Frame(kernel, clock, 1f / 60f);
            world.Bus.Publish(new Ping { Value = 1 });
            Frame(kernel, clock, 0f);                              // readable while paused …
            Frame(kernel, clock, 0f);
            clock.Epoch++;                                         // … then a load snaps the clock
            world.Bus.Publish(new Ping { Value = 2 });
            Frame(kernel, clock, 0f);
            Frame(kernel, clock, 1f / 60f);
            Assert.Equal(new[] { 2 }, step.AllRead);
            kernel.Dispose(); world.Dispose();
        }

        /// <summary>A 10 Hz background module that counts every <see cref="Ping"/> its snapshot carries.</summary>
        [TickMode(TickPolicy.WhenTimeAdvances)]
        private sealed class CountingBackground : IEcsModule
        {
            public int Seen;
            public int Runs;
            public string Name => nameof(CountingBackground);
            public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(10).WithTimeout(2000);
            public void Tick(ISimulationView view, float deltaTime)
            {
                Interlocked.Add(ref Seen, view.ReadEvents<Ping>().Length);
                Interlocked.Increment(ref Runs);
            }
        }

        /// <summary>
        /// ⭐ Acceptance ③ — a background module held over 120 paused frames, with an event on every one of them, still gets every
        /// event at its next run. 🔴 Before: the history kept 60 frames, so half were trimmed before the module ran.
        /// </summary>
        [Fact]
        public void ABackgroundModule_HeldOverALongPause_GetsEveryEvent()
        {
            var world = new EntityRepository();
            world.RegisterComponent<GlobalTime>();
            world.SetSingletonUnmanaged(new GlobalTime());
            world.RegisterEvent<Ping>();
            var history = new EventAccumulator();
            var kernel = new ModuleHostKernel(world, history);
            var clock = new ScriptedClock();
            kernel.SetTimeController(clock);
            var module = new CountingBackground();
            kernel.RegisterModule(module);
            kernel.InitializeForTest();

            Frame(kernel, clock, 0.1f);                            // its first run
            Frame(kernel, clock, 0.1f);
            int before = Volatile.Read(ref module.Seen);
            for (int i = 0; i < 120; i++)
            {
                world.Bus.Publish(new Ping { Value = i });
                Frame(kernel, clock, 0f);
            }
            Frame(kernel, clock, 0.1f);                            // the next step: a new slot, the module runs
            Frame(kernel, clock, 0.1f);                            // harvest

            Assert.Equal(120, Volatile.Read(ref module.Seen) - before);
            Assert.Equal(0, history.Overflow);
            kernel.Dispose(); world.Dispose();
        }
    }
}
