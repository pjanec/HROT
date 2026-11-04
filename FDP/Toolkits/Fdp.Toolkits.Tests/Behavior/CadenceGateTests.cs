using System;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2382</c> (T1; docs/DESIGN_Evaluation_Cost_And_Cadence.md §3.0–§3.2, R-2083) — THE BRAIN CADENCE: a task slot whose
    /// <c>BehaviorState.Cadence</c> a policy set ticks every period (staggered), at once when woken, never when asleep — and reads the bus
    /// through ITS OWN event window, so an event published on a frame it skipped is read once, on its next tick. A brain nothing cadences
    /// ticks every frame, as before. The SOP slot's own cadence (<see cref="SopSlotTests"/>) runs on the same <see cref="CadenceGate"/>.
    /// </summary>
    public sealed class CadenceGateTests
    {
        private const int CadencedId = 0x2382, PlainId = 0x2383;

        [EventId(99438)] private struct CadencePing { public int N; }

        [ThreadStatic] private static int _cadencedTicks, _plainTicks, _pingsSeen;
        [ThreadStatic] private static CadenceMode _setMode;
        [ThreadStatic] private static double _setNext;

        /// <summary>A policy-in-a-node: on its tick it counts the pings it can read and leaves its cadence as the test asks.</summary>
        private static NodeStatus Cadenced(ref byte bb, ref BehaviorTreeState s, ref BTreeContext ctx, int p)
        {
            _cadencedTicks++;
            _pingsSeen += ctx.World.ReadEvents<CadencePing>().Length;
            ref var b = ref ctx.World.GetComponentRW<BehaviorState>(ctx.Self);
            if (_setMode != CadenceMode.EveryFrame) b.Cadence = _setMode;
            if (_setMode == CadenceMode.Asleep) b.CadenceNextTick = _setNext;
            return NodeStatus.Running;
        }

        private static NodeStatus Plain(ref byte bb, ref BehaviorTreeState s, ref BTreeContext ctx, int p) { _plainTicks++; return NodeStatus.Running; }

        private static BehaviorDefinition Tree(string name, NodeLogicDelegate<byte, BTreeContext> leaf)
        {
            var b = new BTreeBuilder<byte, BTreeContext>().Sequence(seq => seq.Action(leaf));
            return new BehaviorDefinition { Name = name, BrainTier = BehaviorConstants.BrainTierBTree, BTreeInterpreter = new Interpreter<byte, BTreeContext>(b.Compile(name), b.GetRegistry()) };
        }

        private sealed class Fixture
        {
            public readonly EntityRepository World = TestWorldFactory.Create();
            public readonly EventAccumulator History = new();
            public readonly BehaviorRegistry Registry = new();
            public readonly BehaviorIngressSystem Ingress;
            public readonly BrainTickSystem Brain;
            public double Now;

            public Fixture(bool history = true)
            {
                BlueprintTierTable.RegisterAll(World);
                if (!World.Bus.IsRegisteredManaged<AssignBehaviorEvent>()) World.Bus.RegisterManaged<AssignBehaviorEvent>();
                World.Bus.Register<CadencePing>();
                Registry.Register(CadencedId, "Cad_Cadenced", Tree("Cad_Cadenced", Cadenced));
                Registry.Register(PlainId, "Cad_Plain", Tree("Cad_Plain", Plain));
                Ingress = new BehaviorIngressSystem(Registry);
                Brain = new BrainTickSystem(Registry);
                if (history) World.AttachEventHistory(History);
                _cadencedTicks = _plainTicks = _pingsSeen = 0;
                _setMode = CadenceMode.Periodic; _setNext = 0;
            }

            public Entity Unit(string behaviour)
            {
                var e = World.CreateEntity();
                World.AddComponent(e, new BehaviorState());
                World.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = behaviour, JsonParams = "", Origin = BehaviorOrigin.Operator });
                World.Bus.SwapBuffers();
                Ingress.Execute(World, 0.016f);
                return e;
            }

            /// <summary>One kernel frame in its order: version, swap, capture, then the brain (ModuleHostKernel.cs:554, 660, 665).</summary>
            public void Frame(int pings = 0)
            {
                for (int i = 0; i < pings; i++) World.Bus.Publish(new CadencePing { N = i });
                Now += 1.0 / 60.0;
                World.SetSingleton(new GlobalTime { TotalTime = Now, DeltaTime = 1f / 60f, TimeScale = 1f });
                World.Tick();
                World.Bus.SwapBuffers();
                History.CaptureFrame(World.Bus, World.GlobalVersion);
                Brain.Execute(World, 1f / 60f);
            }
        }

        [Fact]
        public void CE2382_APeriodicSlot_TicksFiveTimesASecond_AndReadsEveryEventOnce()
        {
            var f = new Fixture();
            f.Unit("Cad_Cadenced");
            f.Frame();                                       // its first tick sets Periodic (a policy's first tick)
            int ticksBefore = _cadencedTicks;
            int published = 0;
            for (int i = 0; i < 60; i++) { f.Frame(pings: 1); published++; }   // one second, a ping EVERY frame
            int ticks = _cadencedTicks - ticksBefore;
            Assert.InRange(ticks, 4, 6);                     // ~5 Hz, not 60
            f.Frame(); f.Frame(); for (int i = 0; i < 12; i++) f.Frame();      // let it read the tail
            Assert.Equal(published, _pingsSeen);             // every ping read once — none lost on a skipped frame, none twice
        }

        [Fact]
        public void CE2382_ABrainNothingCadences_TicksEveryFrame()
        {
            var f = new Fixture();
            f.Unit("Cad_Plain");
            for (int i = 0; i < 30; i++) f.Frame();
            Assert.Equal(30, _plainTicks);
        }

        [Fact]
        public void CE2382_AWake_TicksTheSlotOnTheNextFrame_AndAnAsleepSlotWaitsForIt()
        {
            var f = new Fixture();
            var u = f.Unit("Cad_Cadenced");
            _setMode = CadenceMode.Asleep; _setNext = CadenceGate.Never;
            f.Frame();                                       // ticks once, goes to sleep with no timed wake
            int t0 = _cadencedTicks;
            for (int i = 0; i < 120; i++) f.Frame();
            Assert.Equal(t0, _cadencedTicks);                // asleep: two seconds, no tick
            Assert.Equal(0, f.Brain.CadenceReaderCount);     // and it holds no reader (it must not pin the history)
            f.World.GetComponentRW<BehaviorState>(u).CadenceWake = 1;
            f.Frame();
            Assert.Equal(t0 + 1, _cadencedTicks);            // woken ⇒ ticked at once
        }

        [Fact]
        public void CE2382_AnAsleepSlotWithATime_TicksAtThatTime()
        {
            var f = new Fixture();
            f.Unit("Cad_Cadenced");
            _setMode = CadenceMode.Asleep; _setNext = 1.0;   // a known time — a trigger's ExpectedAt
            f.Frame();
            int t0 = _cadencedTicks;
            while (f.Now + 1.0 / 60.0 < 0.99) f.Frame();     // up to ~0.98 s
            Assert.Equal(t0, _cadencedTicks);
            _setNext = CadenceGate.Never;
            f.Frame(); f.Frame();
            Assert.Equal(t0 + 1, _cadencedTicks);            // once, at 1.0 s
        }

        [Fact]
        public void CE2382_WithoutAnEventHistory_ACadencedSlotTicksEveryFrame()
        {
            var f = new Fixture(history: false);             // a bare world: it could not give the slot its window
            f.Unit("Cad_Cadenced");
            for (int i = 0; i < 30; i++) f.Frame();
            Assert.Equal(30, _cadencedTicks);
        }

        [ThreadStatic] private static int _policyCalls;

        /// <summary>A policy owns the slot: it is called after EVERY tick of it (also one that switched to every frame), and what it sets is obeyed.</summary>
        [Fact]
        public void CE2382_APolicy_IsCalledAfterEachTick_AndItsModeIsObeyed()
        {
            const byte TestPolicy = 250;
            CadencePolicies.Register(TestPolicy, static (w, e, now) =>
            {
                _policyCalls++;
                ref var b = ref w.GetComponentRW<BehaviorState>(e);
                b.Cadence = _policyCalls < 3 ? CadenceMode.EveryFrame : CadenceMode.Periodic;   // two frames every frame, then 5 Hz
            });
            var f = new Fixture();
            _setMode = CadenceMode.EveryFrame;                 // the node leaves it alone; the policy decides
            var u = f.Unit("Cad_Cadenced");
            f.World.GetComponentRW<BehaviorState>(u).CadencePolicy = TestPolicy;
            _policyCalls = 0;
            for (int i = 0; i < 60; i++) f.Frame();
            Assert.Equal(_cadencedTicks, _policyCalls);        // once per tick, every tick
            Assert.InRange(_cadencedTicks, 6, 9);             // 2 every-frame ticks, then ~5 Hz for the rest of the second
        }

        [Fact]
        public void CE2382_TheStagger_SpreadsSlotsOverTheFrames()
        {
            var a = CadenceGate.Next(10.0, 0.2, entityIndex: 3);
            var b = CadenceGate.Next(10.0, 0.2, entityIndex: 7);
            Assert.Equal(10.0 + 0.2 + 0.06, a, 9);
            Assert.Equal(10.0 + 0.2 + 0.14, b, 9);
            Assert.True(CadenceGate.Due(CadenceMode.EveryFrame, double.MaxValue, 0, 0));
            Assert.True(CadenceGate.Due(CadenceMode.Periodic, 5, 1, 0));             // a wake beats the schedule
            Assert.False(CadenceGate.Due(CadenceMode.Asleep, CadenceGate.Never, 0, 1e9));
        }
    }
}
