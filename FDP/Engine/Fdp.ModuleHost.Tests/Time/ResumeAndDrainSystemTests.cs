using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Time;
using Xunit;

namespace Fdp.ModuleHost.Tests.Time
{
    /// <summary>
    /// `W1`/`W2` — the staged-write drain.
    ///
    /// <para>Railed against the <see cref="IStagedWrites"/> INTERFACE with a fake, not against the
    /// production implementer: that is <c>DataBreakpointManager</c> and it is being built in the
    /// other lane in parallel. The seam exists precisely so neither lane waits for the other.</para>
    /// </summary>
    public class ResumeAndDrainSystemTests
    {
        /// <summary>
        /// Records what the drain was asked to do. The interesting assertions are all about whether
        /// <see cref="DrainInto"/> was called AT ALL on a given frame — "nothing staged" and "the
        /// drain never ran" are indistinguishable from outside unless something counts.
        /// </summary>
        private sealed class FakeStagedWrites : IStagedWrites
        {
            public int  Pending;
            public bool Rewound;
            public int  DrainCalls;
            public readonly List<ISimulationView> DrainedInto = new();

            public bool HasPending => Pending > 0;
            public bool IsRewound  => Rewound;

            public void DrainInto(ISimulationView view)
            {
                DrainCalls++;
                DrainedInto.Add(view);
                Pending = 0;
            }

            public bool TryGetPending(Entity entity, int typeId, int byteOffset, out byte[] bytes)
            {
                bytes = Array.Empty<byte>();
                return false;
            }
        }

        private static ISimulationView AView()
        {
            var repo = new EntityRepository();
            return repo;
        }

        // ── The three gates ──────────────────────────────────────────────────

        [Fact]
        public void ItDrains_OnAnAdvancingFrame_ThatIsNotRewound()
        {
            var staged = new FakeStagedWrites { Pending = 2, Rewound = false };
            var system = new ResumeAndDrainSystem(staged);
            var view   = AView();

            system.Execute(view, deltaTime: 0.016f);

            Assert.Equal(1, staged.DrainCalls);
            Assert.False(staged.HasPending);
            Assert.Same(view, staged.DrainedInto[0]);
            Assert.Equal(1L, system.DrainCount);
        }

        /// <summary>
        /// A halted frame must leave the edit waiting. This is the difference between "the value
        /// lands when time moves again" and "the value is written into a world that is about to be
        /// recomputed over it".
        /// </summary>
        [Fact]
        public void ItDoesNotDrain_WhileHalted()
        {
            var staged = new FakeStagedWrites { Pending = 1 };
            var system = new ResumeAndDrainSystem(staged);

            system.Execute(AView(), deltaTime: 0f);

            Assert.Equal(0, staged.DrainCalls);
            Assert.True(staged.HasPending, "the staged edit must still be waiting");
            Assert.Equal(0L, system.DrainCount);
        }

        /// <summary>
        /// While a breakpoint holds the pre-tick snapshot, the restore has not happened yet and is
        /// not this system's job. Draining here would write bytes the restore then overwrites.
        /// </summary>
        [Fact]
        public void ItDoesNotDrain_WhileRewound_EvenOnAnAdvancingFrame()
        {
            var staged = new FakeStagedWrites { Pending = 1, Rewound = true };
            var system = new ResumeAndDrainSystem(staged);

            system.Execute(AView(), deltaTime: 0.016f);

            Assert.Equal(0, staged.DrainCalls);
            Assert.True(staged.HasPending);
        }

        [Fact]
        public void ItDoesNothing_WhenNothingIsStaged()
        {
            var staged = new FakeStagedWrites { Pending = 0 };
            var system = new ResumeAndDrainSystem(staged);

            system.Execute(AView(), deltaTime: 0.016f);

            Assert.Equal(0, staged.DrainCalls);
            Assert.Equal(0L, system.DrainCount);
        }

        /// <summary>
        /// The edit survives being halted and lands on the first advancing frame — the end-to-end
        /// claim of the drain, expressed without needing the real implementer.
        /// </summary>
        [Fact]
        public void AnEditStagedWhileHalted_LandsOnTheFirstAdvancingFrame()
        {
            var staged = new FakeStagedWrites { Pending = 1 };
            var system = new ResumeAndDrainSystem(staged);
            var view   = AView();

            system.Execute(view, 0f);      // paused
            system.Execute(view, 0f);      // still paused
            Assert.True(staged.HasPending);

            system.Execute(view, 0.016f);  // resumed

            Assert.Equal(1, staged.DrainCalls);
            Assert.False(staged.HasPending);
        }

        [Fact]
        public void ItRefusesToBeBuiltWithoutASeam()
            => Assert.Throws<ArgumentNullException>(() => new ResumeAndDrainSystem(null!));

        // ── W1: the phase ────────────────────────────────────────────────────

        /// <summary>
        /// `W1`. The drain must run BEFORE Input, because Input is roughly 25 state-mutating systems
        /// — draining after them would let them all run against state the designer had changed.
        /// </summary>
        [Fact]
        public void TheDrain_IsScheduledInPreFrame_WhichRunsBeforeInput()
        {
            var attr = (UpdateInPhaseAttribute?)Attribute.GetCustomAttribute(
                typeof(ResumeAndDrainSystem), typeof(UpdateInPhaseAttribute));

            Assert.NotNull(attr);
            Assert.Equal(SystemPhase.PreFrame, attr!.Phase);
            Assert.True((int)SystemPhase.PreFrame < (int)SystemPhase.Input,
                "PreFrame must order before Input");
            Assert.True((int)SystemPhase.PreFrame < (int)SystemPhase.BeforeSync);
        }

        // ── W1, end to end: the KERNEL runs PreFrame, and runs it first ──────

        /// <summary>Records the order in which the kernel executed the phases.</summary>
        private sealed class OrderSpy : IEcsModuleSystem
        {
            private readonly List<string> _log;
            private readonly string _name;
            public OrderSpy(List<string> log, string name) { _log = log; _name = name; }
            public void Execute(ISimulationView view, float deltaTime) => _log.Add(_name);
        }

        [UpdateInPhase(SystemPhase.PreFrame)]
        private sealed class PreFrameSpy : IEcsModuleSystem
        {
            private readonly List<string> _log;
            public PreFrameSpy(List<string> log) { _log = log; }
            public void Execute(ISimulationView view, float deltaTime) => _log.Add("PreFrame");
        }

        [UpdateInPhase(SystemPhase.Input)]
        private sealed class InputSpy : IEcsModuleSystem
        {
            private readonly List<string> _log;
            public InputSpy(List<string> log) { _log = log; }
            public void Execute(ISimulationView view, float deltaTime) => _log.Add("Input");
        }

        /// <summary>
        /// `W1`'s ACTUAL deliverable, which the attribute test above does not cover: the kernel has
        /// to EXECUTE the new phase, and execute it before Input. An enum value that orders first but
        /// is never scheduled would pass every other test in this file and drain nothing, forever.
        /// </summary>
        [Fact]
        public void TheKernel_ExecutesPreFrame_AndDoesSoBeforeInput()
        {
            var log  = new List<string>();
            using var repo = new EntityRepository();
            repo.RegisterComponent<GlobalTime>();
            repo.SetSingletonUnmanaged(new GlobalTime());

            var kernel = new ModuleHostKernel(repo, new EventAccumulator());
            kernel.SetTimeController(global::Fdp.Toolkit.Time.Controllers.TimeControllerFactory.Create(
                new FdpEventBus(),
                new global::Fdp.Toolkit.Time.Controllers.TimeControllerConfig
                {
                    Role = global::Fdp.Toolkit.Time.Controllers.TimeRole.Standalone,
                    Mode = global::Fdp.ModuleHost.Time.TimeMode.Continuous,
                }));
            kernel.RegisterGlobalSystem(new PreFrameSpy(log));
            kernel.RegisterGlobalSystem(new InputSpy(log));
            kernel.Initialize();

            kernel.Update(0.016f);

            Assert.Contains("PreFrame", log);
            Assert.Contains("Input", log);
            Assert.True(log.IndexOf("PreFrame") < log.IndexOf("Input"),
                $"PreFrame must run before Input; observed order: {string.Join(" -> ", log)}");
        }

        /// <summary>
        /// And a PreFrame system must be a legal GLOBAL registration — the kernel keeps an explicit
        /// allow-list of phases it runs for global systems and throws for anything outside it, so
        /// adding the enum value without adding it to that list would have failed here.
        /// </summary>
        [Fact]
        public void APreFrameSystem_IsAValidGlobalRegistration()
        {
            using var repo = new EntityRepository();
            var kernel = new ModuleHostKernel(repo, new EventAccumulator());

            var ex = Record.Exception(() =>
                kernel.RegisterGlobalSystem(new ResumeAndDrainSystem(new FakeStagedWrites())));

            Assert.Null(ex);
        }
    }
}
