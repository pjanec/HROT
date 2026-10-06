using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Lifecycle.Events;
using Fbt;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    public class LocomotionDispatcherTests
    {
        [Fact]
        public void Dispatcher_CallsOnEnter_OnFirstTick()
        {
            var world = TestWorldFactory.Create();
            var sys = new LocomotionDispatcherSystem();
            var spy = new WritingSpyExecutor<LocomotionChannel>();
            sys.RegisterExecutor(1, spy);

            var e = world.CreateEntity();
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction = 1,
                ActionInstanceId = 1,
                DispatchedInstanceId = 0,
                Status = NodeStatus.Running
            });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });

            // First tick: lifecycle fires (OnEnter) then Execute.
            sys.Execute(world, 0.016f);
            Assert.Equal(1, spy.OnEnterCallCount);
            Assert.Equal(1, spy.ExecuteCallCount);

            var ch1 = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(NodeStatus.Running, ch1.Status);           // executor wrote it
            Assert.Equal(ch1.ActionInstanceId, ch1.DispatchedInstanceId); // prevents repeat OnEnter

            // Second tick: no second OnEnter, Execute again.
            sys.Execute(world, 0.016f);
            Assert.Equal(1, spy.OnEnterCallCount);
            Assert.Equal(2, spy.ExecuteCallCount);

            world.Dispose();
        }

        [Fact]
        public void Dispatcher_CallsOnExit_WhenActionChanges()
        {
            var world = TestWorldFactory.Create();
            var sys = new LocomotionDispatcherSystem();
            var spy1 = new SpyExecutor<LocomotionChannel>();
            var spy2 = new SpyExecutor<LocomotionChannel>();
            sys.RegisterExecutor(1, spy1);
            sys.RegisterExecutor(2, spy2);

            var e = world.CreateEntity();
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction = 1,
                ActionInstanceId = 1,
                DispatchedInstanceId = 0,
                Status = NodeStatus.Running
            });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });

            // Tick 1: enters action 1.
            sys.Execute(world, 0.016f);
            Assert.Equal(1, spy1.OnEnterCallCount);
            Assert.Equal(0, spy1.OnExitCallCount);

            // Brain changes to action 2.
            ref var ch = ref world.GetComponentRW<LocomotionChannel>(e);
            ch.ActiveAction = 2;
            ch.ActionInstanceId = 2;

            // Tick 2: exits action 1, enters action 2.
            sys.Execute(world, 0.016f);
            Assert.Equal(1, spy1.OnExitCallCount);
            Assert.Equal(1, spy2.OnEnterCallCount);

            world.Dispose();
        }

        [Fact]
        public void Dispatcher_FailsChannel_WhenCannotMove()
        {
            var world = TestWorldFactory.Create();
            var sys = new LocomotionDispatcherSystem();
            var spy = new SpyExecutor<LocomotionChannel>();
            sys.RegisterExecutor(1, spy);

            var e = world.CreateEntity();
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction = 1,
                ActionInstanceId = 1,
                DispatchedInstanceId = 0,
                Status = NodeStatus.Running
            });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.None });

            sys.Execute(world, 0.016f);

            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(NodeStatus.Failure, channel.Status);
            Assert.Equal(0, spy.ExecuteCallCount);
            Assert.Equal(0, spy.OnEnterCallCount);

            world.Dispose();
        }

        /// <summary>⭐ <c>CE-3091</c> — a RUNNING move whose unit loses CanMove (death) is ENDED: OnExit runs once (for MoveTo, the
        /// STOP the mover follows), never again while the capability stays lost; a restored capability enters the behaviour's
        /// re-issued action afresh. ✅ Red-proof: the old early `continue` ⇒ OnExit 0 (live: a killed unit walked ~80 m).</summary>
        [Fact]
        public void CE3091_LosingCanMove_EndsTheRunningMove_OnceAndOnlyOnce()
        {
            var world = TestWorldFactory.Create();
            var sys = new LocomotionDispatcherSystem();
            var spy = new SpyExecutor<LocomotionChannel>();
            sys.RegisterExecutor(1, spy);

            var e = world.CreateEntity();
            world.AddComponent(e, new LocomotionChannel { ActiveAction = 1, ActionInstanceId = 1, DispatchedInstanceId = 0, Status = NodeStatus.Running });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });
            sys.Execute(world, 0.016f);                                        // the move starts
            Assert.Equal(1, spy.OnEnterCallCount);

            world.GetComponentRW<ActorCapabilityState>(e).Capabilities = ActorCapabilities.None;   // killed
            sys.Execute(world, 0.016f);
            Assert.Equal(1, spy.OnExitCallCount);                              // the move is ended — the STOP is sent
            Assert.Equal(NodeStatus.Failure, world.GetComponent<LocomotionChannel>(e).Status);
            world.GetComponentRW<LocomotionChannel>(e).ActionInstanceId++;     // the behaviour re-issues while it cannot move
            sys.Execute(world, 0.016f);
            Assert.Equal(1, spy.OnExitCallCount);                              // …not again
            Assert.Equal(1, spy.OnEnterCallCount);

            world.GetComponentRW<ActorCapabilityState>(e).Capabilities = ActorCapabilities.CanMove;   // e.g. ejected / repaired
            world.GetComponentRW<LocomotionChannel>(e).Status = NodeStatus.Running;
            world.GetComponentRW<LocomotionChannel>(e).ActionInstanceId++;
            sys.Execute(world, 0.016f);
            Assert.Equal(2, spy.OnEnterCallCount);                             // the re-issued move enters afresh
            Assert.Equal(1, spy.OnExitCallCount);

            world.Dispose();
        }

        [Fact]
        public void Dispatcher_SkipsNullExecutor_Gracefully()
        {
            var world = TestWorldFactory.Create();
            var sys = new LocomotionDispatcherSystem();
            // No executor registered for action 1.

            var e = world.CreateEntity();
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction = 1,
                ActionInstanceId = 1,
                DispatchedInstanceId = 0,
                Status = NodeStatus.Running
            });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });

            // Must not throw.
            sys.Execute(world, 0.016f);

            // Lifecycle bookkeeping still ran even without a registered executor.
            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(channel.ActionInstanceId, channel.DispatchedInstanceId); // updated even without executor

            world.Dispose();
        }

        // ── DEBT-024 test ─────────────────────────────────────────────────────
        /// <summary>
        /// When a DestructionOrder is on the bus (entity entering TearDown, e.g. killed
        /// by DamageSystem in the previous frame), the dispatcher must call OnExit cleanly
        /// at the start of the next Execute() and must not throw.
        /// The 1-frame ELM delay guarantees the entity and its channel are intact when
        /// the order is read.
        /// </summary>
        [Fact]
        public void Dispatcher_CallsOnExit_WhenDestructionOrderReceived()
        {
            var world = TestWorldFactory.Create();
            world.RegisterEvent<DestructionOrder>();
            var sys = new LocomotionDispatcherSystem();
            var spy = new SpyExecutor<LocomotionChannel>();
            sys.RegisterExecutor(1, spy);

            var e = world.CreateEntity();
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction         = 1,
                ActionInstanceId     = 1,
                DispatchedInstanceId = 1,
                Status               = NodeStatus.Running,
            });
            world.AddComponent(e, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove,
            });

            // Publish DestructionOrder (simulating what the ELM emits when teardown begins).
            world.Bus.Publish(new DestructionOrder { Entity = e });
            // Swap so the order is in the read buffer when the dispatcher runs next tick.
            world.Bus.SwapBuffers();

            // Act: dispatcher reads DestructionOrder at the top of Execute, calls OnExit.
            var exception = Record.Exception(() => sys.Execute(world, 0.016f));
            Assert.Null(exception);
            Assert.Equal(1, spy.OnExitCallCount);

            world.Dispose();
        }

        // ── Q74 ACCEPTANCE RAIL ⑤ — the measurement that DELETES `D-E` ─────────────────────

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE404_R5a</c> — ACCEPTANCE RAIL ⑤: release-on-exit followed by claim-on-entry
        /// <b>in the same tick</b> leaves NO OBSERVABLE GAP.</b>
        /// 📄 <c>Architect_Question_74_Blueprint_Channel_Lifecycle.md</c> §6 ⑤, §4 <c>D-E</c>.
        ///
        /// <para>⭐⭐ <b>Why this rail exists, and what it decides.</b> <c>D-E</c> proposed a per-state
        /// <c>KeepChannelsOnExit</c> opt-out, on the premise that releasing a channel on state exit and
        /// re-claiming it on the next state's entry would <i>"drop the command for a frame — a visible
        /// stutter on a driving vehicle."</i> ⭐ That premise is MEASURABLE, and this rail measures it.</para>
        ///
        /// <para>📐 <b>The mechanism that makes the answer "no gap".</b> The dispatcher is the ONLY
        /// observer of the channel, and it runs ONCE per frame, AFTER the brain
        /// (<c>BlueprintTickSystem.cs:17</c> is <c>[UpdateBefore(typeof(LocomotionDispatcherSystem))]</c>;
        /// <c>CognitiveRuntimeModule</c> orders <c>ChannelArbitrationSystem</c> → <c>BrainTickSystem</c>
        /// ahead of <c>ActionDispatchModule</c>'s dispatchers). ⇒ the intermediate <c>ActiveAction == 0</c>
        /// written by the <c>ExitCleanup_*</c> thunk exists only BETWEEN two statements of one brain tick
        /// and is overwritten before anything reads it. <c>CE382_R5</c> supplies the other half — the
        /// newly entered state's activity runs in the SAME tick as the transition.</para>
        ///
        /// <para>⭐ The two writes below are the production idioms verbatim: the release is the generated
        /// <c>ExitCleanup_*</c> body (<c>ActiveAction = 0</c>, <c>ActionInstanceId++</c> — never
        /// <c>= default</c>, per <c>BD1-DESIGN.md</c> §1.1), the claim is what
        /// <c>ChannelCommandLowering.Emit</c> emits.</para>
        ///
        /// <para>✅ <b>Non-vacuity:</b> <see cref="CE404_R5b_SplitAcrossTwoTicks_DOES_LeaveAnIdleFrame"/>
        /// runs the dispatcher BETWEEN the release and the claim and shows the idle frame appear.</para>
        /// </summary>
        [Fact]
        public void CE404_R5a_ReleaseThenReclaimInOneTick_LeavesNoObservableGap()
        {
            var world = TestWorldFactory.Create();
            var sys   = new LocomotionDispatcherSystem();
            var outgoing = new WritingSpyExecutor<LocomotionChannel>();
            var incoming = new WritingSpyExecutor<LocomotionChannel>();
            sys.RegisterExecutor(1, outgoing);
            sys.RegisterExecutor(2, incoming);

            var e = world.CreateEntity();
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction         = 1,
                ActionInstanceId     = 1,
                DispatchedInstanceId = 0,
                Status               = NodeStatus.Running,
            });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });

            // Frame 1: the outgoing action is driving.
            sys.Execute(world, 0.016f);
            Assert.Equal(1, outgoing.OnEnterCallCount);
            Assert.Equal(1, outgoing.ExecuteCallCount);

            // Frame 2, BRAIN half — both halves land inside one tick, before the dispatcher runs.
            ref var ch = ref world.GetComponentRW<LocomotionChannel>(e);
            ch.ActiveAction = 0; unchecked { ch.ActionInstanceId++; }   // ExitCleanup_* thunk (release)
            ch.ActiveAction = 2; unchecked { ch.ActionInstanceId++; }   // the new state's activity (claim)
            ch.Status = NodeStatus.Running;

            // Frame 2, DISPATCH half.
            sys.Execute(world, 0.016f);

            // ⭐ THE RAIL: a clean hand-off, and the muscle was driven on the hand-off frame itself.
            Assert.Equal(1, outgoing.OnExitCallCount);
            Assert.Equal(1, incoming.OnEnterCallCount);
            Assert.Equal(1, incoming.ExecuteCallCount);

            // ⛔ The dispatcher never saw the intermediate zero, and never dispatched action 0:
            //    the outgoing executor ran on frame 1 and not again.
            Assert.Equal(1, outgoing.ExecuteCallCount);
            Assert.Equal((ushort)2, world.GetComponent<LocomotionChannel>(e).ActiveAction);

            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <b><c>CE404_R5b</c> — the NON-VACUITY half of rail ⑤.</b> Identical to
        /// <see cref="CE404_R5a_ReleaseThenReclaimInOneTick_LeavesNoObservableGap"/> except that the
        /// dispatcher runs BETWEEN the release and the claim.
        ///
        /// <para>🔒 Then the gap is real and measurable: one frame on which <b>no executor runs at all</b>.
        /// ⭐ This is what <c>D-E</c> would have guarded against — and the frame ordering measured in
        /// <c>R5a</c> is what already prevents it, with no opt-out flag and no authoring.</para>
        /// </summary>
        [Fact]
        public void CE404_R5b_SplitAcrossTwoTicks_DOES_LeaveAnIdleFrame()
        {
            var world = TestWorldFactory.Create();
            var sys   = new LocomotionDispatcherSystem();
            var outgoing = new WritingSpyExecutor<LocomotionChannel>();
            var incoming = new WritingSpyExecutor<LocomotionChannel>();
            sys.RegisterExecutor(1, outgoing);
            sys.RegisterExecutor(2, incoming);

            var e = world.CreateEntity();
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction         = 1,
                ActionInstanceId     = 1,
                DispatchedInstanceId = 0,
                Status               = NodeStatus.Running,
            });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });

            sys.Execute(world, 0.016f);
            int drivenBeforeTheGap = outgoing.ExecuteCallCount + incoming.ExecuteCallCount;

            // Release only — the claim does NOT happen this tick.
            ref var ch = ref world.GetComponentRW<LocomotionChannel>(e);
            ch.ActiveAction = 0; unchecked { ch.ActionInstanceId++; }

            sys.Execute(world, 0.016f);                       // ⛔ the IDLE frame

            // 🔴 Nothing drove the channel on that frame — that is the gap, made visible.
            Assert.Equal(drivenBeforeTheGap, outgoing.ExecuteCallCount + incoming.ExecuteCallCount);

            // The claim arrives a tick late; the hand-off then completes normally.
            ref var ch2 = ref world.GetComponentRW<LocomotionChannel>(e);
            ch2.ActiveAction = 2; unchecked { ch2.ActionInstanceId++; }
            ch2.Status = NodeStatus.Running;
            sys.Execute(world, 0.016f);

            Assert.Equal(1, incoming.OnEnterCallCount);
            Assert.Equal(1, incoming.ExecuteCallCount);

            world.Dispose();
        }
    }
}
