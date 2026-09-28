using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fbt;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Toolkit.Behavior.Tests
{
    public class ChannelArbitrationTests
    {
        [Fact]
        public void Arbitration_ClearsStaleChannel()
        {
            var world = TestWorldFactory.Create();
            
            var sys = new ChannelArbitrationSystem();
            
            var e = world.CreateEntity();
            // Behavior at version 2 (preempted version 1)
            world.AddComponent(e, new BehaviorState { InstanceId = 2 });
            // Channel still has action from version 1
            world.AddComponent(e, new LocomotionChannel { 
                ActiveAction = 1, 
                BehaviorInstanceId = 1,
                Status = NodeStatus.Running
            });
            
            sys.Execute(world, 0.016f);
            
            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(0, channel.ActiveAction);
            // Selective-clear: only ActiveAction is zeroed and ActionInstanceId is bumped.
            // Status and BehaviorInstanceId are NOT reset (differs from `channel = default`).
            Assert.Equal(NodeStatus.Running, channel.Status);  // unchanged
            Assert.Equal(1u, channel.BehaviorInstanceId);      // unchanged (selective-clear, not full reset)
            
            world.Dispose();
        }

        [Fact]
        public void Arbitration_IgnoresValidChannel()
        {
            var world = TestWorldFactory.Create();
            
            var sys = new ChannelArbitrationSystem();
            
            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { InstanceId = 2 });
            world.AddComponent(e, new LocomotionChannel { 
                ActiveAction = 1, 
                BehaviorInstanceId = 2, // Matches
                Status = NodeStatus.Running
            });
            
            sys.Execute(world, 0.016f);
            
            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(1, channel.ActiveAction);
            Assert.Equal(NodeStatus.Running, channel.Status);
            
            world.Dispose();
        }

        [Fact]
        public void Arbitration_IgnoresEmptyChannel()
        {
            var world = TestWorldFactory.Create();
            
            var sys = new ChannelArbitrationSystem();
            
            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { InstanceId = 2 });
            world.AddComponent(e, new LocomotionChannel { 
                ActiveAction = 0, // None
                BehaviorInstanceId = 1, // Stale ID
                Status = NodeStatus.Success // Old status
            });
            
            sys.Execute(world, 0.016f);
            
            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(0, channel.ActiveAction);
            Assert.Equal(NodeStatus.Success, channel.Status);
            
            world.Dispose();
        }

        /// <summary>
        /// Ordering integration test: ChannelArbitrationSystem must run before
        /// LocomotionDispatcherSystem. When it does, a stale channel is cleared
        /// before the dispatcher sees it, so no ghost OnEnter fires.
        /// This verifies the [UpdateBefore]/[UpdateAfter] ordering contract.
        /// </summary>
        [Fact]
        public void Arbitration_Ordering_NoGhostOnEnter_WhenChannelIsStale()
        {
            var world = TestWorldFactory.Create();

            var arbitration = new ChannelArbitrationSystem();
            var dispatcher  = new LocomotionDispatcherSystem();
            var spy         = new WritingSpyExecutor<LocomotionChannel>();
            dispatcher.RegisterExecutor(1, spy);

            var e = world.CreateEntity();
            // Behavior at version 2; channel still thinks version 1 is current.
            world.AddComponent(e, new BehaviorState { InstanceId = 2 });
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction       = 1,
                ActionInstanceId   = 1,
                BehaviorInstanceId = 1,   // stale — mismatches BehaviorState.InstanceId
                DispatchedInstanceId = 0,
                Status             = NodeStatus.Running
            });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });

            // Correct order: arbitration clears stale channel BEFORE dispatcher runs.
            arbitration.Execute(world, 0.016f);
            dispatcher.Execute(world, 0.016f);

            // Arbitration cleared ActiveAction → dispatcher found nothing to dispatch.
            Assert.Equal(0, spy.OnEnterCallCount); // no ghost OnEnter
            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(0, channel.ActiveAction); // confirmed cleared

            world.Dispose();
        }

        // ── Task-3 Tests: OnExit Guarantee (───────────────────────────────────────

        [Fact]
        public void ChannelClear_ShouldNotZeroActionInstanceId()
        {
            // ActionInstanceId must be INCREMENTED (not zeroed) so that
            // LocomotionDispatcherSystem evaluates (ActionInstanceId != DispatchedInstanceId)
            // and fires OnExit for the preempted action.
            var world = TestWorldFactory.Create();
            var sys   = new ChannelArbitrationSystem();

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { InstanceId = 1 });
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction         = 5,
                BehaviorInstanceId   = 0,  // stale: 0 != 1
                ActionInstanceId     = 7,
                DispatchedInstanceId = 7,
            });

            sys.Execute(world, 0.016f);

            var ch = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(0,  ch.ActiveAction);          // cleared
            Assert.Equal(8u, ch.ActionInstanceId);      // incremented: 7 → 8
            Assert.Equal(7u, ch.DispatchedInstanceId);  // unchanged — dispatcher will fire OnExit

            world.Dispose();
        }

        [Fact]
        public void NoPreemption_WhenBehaviorMatches()
        {
            var world = TestWorldFactory.Create();
            var sys   = new ChannelArbitrationSystem();

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { InstanceId = 3 });
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction       = 2,
                BehaviorInstanceId = 3,  // matches — must not be preempted
                ActionInstanceId   = 1,
            });

            sys.Execute(world, 0.016f);

            var ch = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(2, ch.ActiveAction);   // unchanged
            Assert.Equal(1u, ch.ActionInstanceId); // unchanged

            world.Dispose();
        }

        [Fact]
        public void WeaponChannel_ReceivesOnExitSignal()
        {
            var world = TestWorldFactory.Create();
            var sys   = new ChannelArbitrationSystem();

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { InstanceId = 1 });
            world.AddComponent(e, new WeaponChannel
            {
                ActiveAction         = 5,
                BehaviorInstanceId   = 0,   // stale
                ActionInstanceId     = 7,
                DispatchedInstanceId = 7,
            });

            sys.Execute(world, 0.016f);

            var ch = world.GetComponent<WeaponChannel>(e);
            Assert.Equal(0,  ch.ActiveAction);
            Assert.Equal(8u, ch.ActionInstanceId);      // incremented
            Assert.Equal(7u, ch.DispatchedInstanceId);  // unchanged

            world.Dispose();
        }

        [Fact]
        public void InteractionChannel_ReceivesOnExitSignal()
        {
            var world = TestWorldFactory.Create();
            var sys   = new ChannelArbitrationSystem();

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { InstanceId = 1 });
            world.AddComponent(e, new InteractionChannel
            {
                ActiveAction         = 5,
                BehaviorInstanceId   = 0,   // stale
                ActionInstanceId     = 7,
                DispatchedInstanceId = 7,
            });

            sys.Execute(world, 0.016f);

            var ch = world.GetComponent<InteractionChannel>(e);
            Assert.Equal(0,  ch.ActiveAction);
            Assert.Equal(8u, ch.ActionInstanceId);      // incremented
            Assert.Equal(7u, ch.DispatchedInstanceId);  // unchanged

            world.Dispose();
        }

        // ── CE-402 — a BLUEPRINT-issued channel command must CLAIM the channel ──────────────

        /// <summary>
        /// 🔴🔴 <b><c>CE402_R1</c> — a channel command issued by a REAL GENERATED BLUEPRINT survives
        /// arbitration.</b> 📄 <c>Architect_Question_74_Blueprint_Channel_Lifecycle.md</c> §0 ③, §6 ①.
        ///
        /// <para>⛔⛔ <b>The defect.</b> <c>ChannelCommandLowering</c> wrote <c>ActiveAction</c>, the
        /// params and <c>ActionInstanceId++</c> — and never stamped <c>BehaviorInstanceId</c>. Every
        /// other production channel writer stamps it explicitly (<c>CgfNodes</c>,
        /// <c>HillAttackTankNodes</c>, <c>EqsCombatNodes</c>, <c>HsmChannelRegionNodes</c> — 11 sites);
        /// the blueprint lowering was the only one that did not. ⇒ <c>Arbitration_ClearsStaleChannel</c>
        /// above describes exactly what then happened to every blueprint channel command **on the very
        /// next tick**: zeroed. Nothing ever moved.</para>
        ///
        /// <para>⭐⭐ <b>Why this rail drives a REAL generated blueprint</b> rather than hand-writing
        /// the channel: the defect was in the EMITTER, so a fixture that writes the channel itself
        /// cannot see it. <c>HillAssault2ReverseToBaseline</c> is a shipped asset whose graph issues the
        /// built-in <c>MoveTo</c> channel command, and its <c>TickCore</c> is the emitter's own output.</para>
        ///
        /// <para>✅ <b>Red-proof:</b> delete the <c>BehaviorInstanceId</c> stamp from
        /// <c>ChannelCommandLowering.Emit</c> ⇒ the post-arbitration assertion reddens.</para>
        /// </summary>
        [Fact]
        public void CE402_R1_ABlueprintIssuedChannelCommandSurvivesArbitration()
        {
            var world = TestWorldFactory.Create();
            var e     = world.CreateEntity();

            // ⭐ InstanceId 7 rather than 0/1: a stamp that only "works" because both sides are
            //   zero would pass with the defect still present.
            world.AddComponent(e, new BehaviorState { InstanceId = 7 });
            world.AddComponent(e, new LocomotionChannel());

            var p  = default(global::Hrot.AI.Behaviors.Generated.HillAssault2ReverseToBaseline_FF75553A_Bp.Params);
            var ws = default(global::Hrot.AI.Behaviors.Generated.HillAssault2ReverseToBaseline_FF75553A_Bp.WorkingState);
            global::Hrot.AI.Behaviors.Generated.HillAssault2ReverseToBaseline_FF75553A_Bp
                .TickCore(ref p, ref ws, e, world, 0f);

            var issued = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal((ushort)1, issued.ActiveAction);        // MoveTo — the blueprint issued it
            Assert.Equal(7u,        issued.BehaviorInstanceId);  // ⭐ CE-402 — and CLAIMED it

            new ChannelArbitrationSystem().Execute(world, 0.016f);

            Assert.Equal((ushort)1, world.GetComponent<LocomotionChannel>(e).ActiveAction);

            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <b><c>CE402_R2</c> — the NON-VACUITY half.</b> Same blueprint, same tick, but the
        /// behaviour is preempted afterwards ⇒ arbitration MUST still clear it.
        ///
        /// <para>🔒 Without this, <c>R1</c> would also pass if <c>CE-402</c> had been "fixed" by
        /// neutering arbitration (<c>D-C3</c>, the arm Q74 rejects as the cheapest-looking and worst).
        /// ⭐ This pins that the claim is <b>scoped to one behaviour instance</b>, not a blanket
        /// exemption.</para>
        /// </summary>
        [Fact]
        public void CE402_R2_TheClaimIsScopedToOneBehaviourInstance_NotABlanketExemption()
        {
            var world = TestWorldFactory.Create();
            var e     = world.CreateEntity();

            world.AddComponent(e, new BehaviorState { InstanceId = 7 });
            world.AddComponent(e, new LocomotionChannel());

            var p  = default(global::Hrot.AI.Behaviors.Generated.HillAssault2ReverseToBaseline_FF75553A_Bp.Params);
            var ws = default(global::Hrot.AI.Behaviors.Generated.HillAssault2ReverseToBaseline_FF75553A_Bp.WorkingState);
            global::Hrot.AI.Behaviors.Generated.HillAssault2ReverseToBaseline_FF75553A_Bp
                .TickCore(ref p, ref ws, e, world, 0f);

            // The behaviour is preempted — a new instance takes over.
            ref var behaviour = ref world.GetComponentRW<BehaviorState>(e);
            behaviour.InstanceId = 8;

            new ChannelArbitrationSystem().Execute(world, 0.016f);

            Assert.Equal(0, world.GetComponent<LocomotionChannel>(e).ActiveAction);

            world.Dispose();
        }

        // ── CE-405 — RE-ISSUING THE SAME COMMAND MUST NOT COUNT AS A NEW ONE ───────────────

        /// <summary>
        /// Drives the real generated <c>HillAssault2ReverseToBaseline_FF75553A_Bp</c> and replays
        /// its entry block by resetting the working state's phase, which is what a blueprint on a
        /// per-tick path does naturally. Returns the channel after <paramref name="ticks"/> issues.
        /// </summary>
        private static (uint actionInstanceId, int onEnterCalls) ReIssue(
            int ticks, float baselineXPerTick = 0f, NodeStatus statusBetweenTicks = NodeStatus.Running)
        {
            var world = TestWorldFactory.Create();
            var e     = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { InstanceId = 7 });
            world.AddComponent(e, new LocomotionChannel { Status = NodeStatus.Running });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });

            var dispatcher = new LocomotionDispatcherSystem();
            var spy = new WritingSpyExecutor<LocomotionChannel>();
            dispatcher.RegisterExecutor(1, spy);   // MoveTo

            var p  = default(global::Hrot.AI.Behaviors.Generated.HillAssault2ReverseToBaseline_FF75553A_Bp.Params);
            var ws = default(global::Hrot.AI.Behaviors.Generated.HillAssault2ReverseToBaseline_FF75553A_Bp.WorkingState);

            for (int i = 0; i < ticks; i++)
            {
                ws.__phase = 0;                                  // replay the entry block
                p.BaselineX += baselineXPerTick;                 // 0 => an IDENTICAL command
                if (i > 0 && statusBetweenTicks != NodeStatus.Running)
                {
                    ref var pre = ref world.GetComponentRW<LocomotionChannel>(e);
                    pre.Status = statusBetweenTicks;
                }
                global::Hrot.AI.Behaviors.Generated.HillAssault2ReverseToBaseline_FF75553A_Bp
                    .TickCore(ref p, ref ws, e, world, 0.016f);
                dispatcher.Execute(world, 0.016f);
            }

            var ch = world.GetComponent<LocomotionChannel>(e);
            world.Dispose();
            return (ch.ActionInstanceId, spy.OnEnterCallCount);
        }

        /// <summary>
        /// 🔴🔴 <b><c>CE405_R1</c> — re-issuing an IDENTICAL channel command does not advance
        /// <c>ActionInstanceId</c>, so the executor is not re-entered.</b>
        /// 📄 <c>Architect_Question_74_Blueprint_Channel_Lifecycle.md</c> §9.7.
        ///
        /// <para>⛔⛔ <b>The defect.</b> <c>ChannelCommandLowering</c> bumped <c>ActionInstanceId</c>
        /// unconditionally, every time the op ran. <c>LocomotionDispatcherSystem.cs:62</c> reads ANY
        /// change in that id as <i>"a new action was dispatched"</i> and calls <c>OnExit</c> +
        /// <c>OnEnter</c> — and for <c>MoveTo</c>, <c>OnEnter</c> RE-PLANS THE PATH. ⇒ a blueprint
        /// issuing a channel command on a per-tick path re-planned every frame.</para>
        ///
        /// <para>⚠ <b>Why it was latent.</b> 📐 The one shipped blueprint that issues a channel
        /// command gates it behind its own <c>ws.__phase</c> wait, so the op ran ONCE. This rail
        /// resets the phase to replay the entry block — the shape an HSM activity blueprint has
        /// naturally, and the shape the editor end-to-end fixtures will author.</para>
        ///
        /// <para>✅ <b>Red-proof:</b> make the emitted <c>ActionInstanceId++</c> unconditional again
        /// ⇒ the id reaches 5 and <c>OnEnter</c> is called 5 times.</para>
        /// </summary>
        [Fact]
        public void CE405_R1_ReIssuingTheSameCommand_DoesNotReEnterTheExecutor()
        {
            var (actionInstanceId, onEnterCalls) = ReIssue(ticks: 5);

            Assert.Equal(1u, actionInstanceId);   // ⭐ one command, issued five times
            Assert.Equal(1, onEnterCalls);        // ⭐ and the muscle was entered ONCE
        }

        /// <summary>
        /// ⭐⭐ <b><c>CE405_R2</c> — the NON-VACUITY half: CHANGED PARAMS still re-enter.</b>
        ///
        /// <para>🔒 This is why the test is <i>action id + params bytes</i> and not <i>action id
        /// alone</i>. <c>CgfNodes.cs:461-463</c> (Wander) re-enters DELIBERATELY on a fresh
        /// destination — suppressing that would break re-planning, which is a worse defect than the
        /// one being fixed. ⭐ Without this rail, <c>R1</c> would also pass if the emitter had simply
        /// stopped bumping the id at all.</para>
        /// </summary>
        [Fact]
        public void CE405_R2_ChangedParams_DoReEnterTheExecutor()
        {
            var (actionInstanceId, onEnterCalls) = ReIssue(ticks: 5, baselineXPerTick: 10f);

            Assert.Equal(5u, actionInstanceId);   // a genuinely new destination each tick
            Assert.Equal(5, onEnterCalls);        // ⇒ re-planned each tick, as intended
        }

        /// <summary>
        /// ⭐ <b><c>CE405_R3</c> — a FAILED channel is retried</b>, rather than left stuck behind its
        /// own "nothing changed" test.
        /// </summary>
        [Fact]
        public void CE405_R3_AFailedChannelIsReActivated_NotLeftStuck()
        {
            var (actionInstanceId, onEnterCalls) =
                ReIssue(ticks: 2, statusBetweenTicks: NodeStatus.Failure);

            Assert.Equal(2u, actionInstanceId);
            Assert.Equal(2, onEnterCalls);
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE405_R4</c> — ISSUING THE SAME COMMAND TWICE, DELIBERATELY, MEANS IT TWICE.</b>
        /// A command that has COMPLETED (<c>Status == Success</c>) and is then issued again with
        /// identical params must re-enter the executor — <i>"open that door"</i> twice is two opens,
        /// not one.
        ///
        /// <para>🔴 <b>This is the hole the first cut of <c>CE-405</c> had.</b> The guard was written
        /// as <c>ActiveAction != id || Status == Failure</c>, copying the C# idiom at
        /// <c>CgfNodes.cs:305,313</c>. ⛔ That copy is wrong for a blueprint, because <c>CgfNodes</c>
        /// has a second route out that a blueprint statement does not: it FORWARDS the terminal
        /// status up the tree (<c>CgfNodes.cs:307-310</c>) rather than re-issuing, so it never needs
        /// to re-activate on Success. A channel-command op has no "return Success upward" — issuing
        /// it again is the author's ONLY way to say "again".</para>
        ///
        /// <para>📐 <b>Measured across all 12 channel executors:</b> every one is a standing order
        /// that terminates by setting Success or Failure (<c>AimAndFireExecutor.cs:49,56</c> ·
        /// <c>OpenDoorExecutor.cs:33</c> · <c>EjectPassengersExecutor.cs:70</c> ·
        /// <c>MoveToExecutor.cs:108,122</c>). ⇒ a terminal status is the runtime's own statement that
        /// the previous instance is OVER, so a re-issue after it is a new instance by definition.</para>
        ///
        /// <para>⚠ <b>What this does NOT mean.</b> Rate of fire is not this mechanism:
        /// <c>AimAndFireExecutor.cs:61-76</c> publishes one <c>WeaponFireIntent</c> per cooldown from
        /// <c>Execute</c>, every tick it is Running. ⭐ So one standing "fire at target" already
        /// produces many shots, and issuing it twice was never how a second shot is requested.</para>
        ///
        /// <para>✅ <b>Red-proof:</b> narrow the guard back to <c>Status == Failure</c> ⇒ this reddens
        /// with one <c>OnEnter</c> instead of two, while <c>R1</c>–<c>R3</c> stay green.</para>
        /// </summary>
        [Fact]
        public void CE405_R4_ReIssuingAfterTheCommandCompleted_IsANewCommand()
        {
            var (actionInstanceId, onEnterCalls) =
                ReIssue(ticks: 2, statusBetweenTicks: NodeStatus.Success);

            Assert.Equal(2u, actionInstanceId);   // ⭐ two deliberate issues
            Assert.Equal(2, onEnterCalls);        // ⭐ two activations
        }
    }
}
