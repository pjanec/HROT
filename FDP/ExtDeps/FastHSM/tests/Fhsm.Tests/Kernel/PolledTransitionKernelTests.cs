using System;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Xunit;

namespace Fhsm.Tests.Kernel
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-382</c> — a POLLED transition fires on a quiescent tick, with NO event posted.</b>
    /// 📄 <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §3.1, §5, §9 ①②⑤.
    ///
    /// <para>🔴 <b>The gap this closes.</b> Guards were evaluated ONLY inside the event phase
    /// (<c>SelectTransition</c> is reached from <c>ProcessEventPhase</c> → <c>RTC</c>), and in
    /// production exactly ONE event is ever posted — <c>BrainTickSystem</c>'s MobilityLost interrupt.
    /// ⇒ a guard could REFINE an event-triggered transition but could never DRIVE one, so
    /// "transition when this condition becomes true" was not expressible.</para>
    ///
    /// <para>⭐⭐ <b>The rails are written against the PHASE MACHINE, not a helper.</b> Each one calls
    /// <c>HsmKernel.Update</c> on a quiescent instance — empty queue, no timer — because that is the
    /// exact condition the feature claims to serve, and the condition that used to be a fixed point.</para>
    /// </summary>
    [Collection("HsmActionDispatcher")]
    public unsafe class PolledTransitionKernelTests
    {
        private const uint   MachineId  = 0x0CE38200;
        private const ushort GuardId    = 0x0382;
        private const ushort ActivityA  = 0x03A0;
        private const ushort ActivityB  = 0x03B0;

        private static bool _guardPasses;
        private static int  _guardCalls;
        private static int  _activityA;
        private static int  _activityB;

        private static bool Guard(void* instance, void* context, ushort eventId, HsmCommandWriter* writer)
        {
            _guardCalls++;
            return _guardPasses;
        }

        private static void ActivityAThunk(void* i, void* c, HsmCommandWriter* w) => _activityA++;
        private static void ActivityBThunk(void* i, void* c, HsmCommandWriter* w) => _activityB++;

        /// <summary>
        /// A → B, where the A→B transition is POLLED (or not, as asked) and guarded. Both states carry
        /// an activity action so "which state ticked" is observable.
        /// ⚠ Built by hand, like <see cref="ActivitySteadyStateTests"/>, so the ids are exactly what
        /// the rail registers — ⛔ no dependency on how a name hashes.
        /// </summary>
        private static HsmDefinitionBlob TwoStateBlob(bool polled, ushort eventId, bool withCompletionHop = false, bool suppressStateBit = false)
        {
            ushort stateCount = (ushort)(withCompletionHop ? 3 : 2);
            var header = new HsmDefinitionHeader { StructureHash = MachineId, StateCount = stateCount };

            var transitions = withCompletionHop
                ? new[]
                  {
                      // 0: A → B, polled (or not)
                      new TransitionDef
                      {
                          SourceStateIndex = 0, TargetStateIndex = 1,
                          EventId = eventId, GuardId = GuardId,
                          Flags = polled ? TransitionFlags.IsPolled : TransitionFlags.None,
                      },
                      // 1: B → C on the COMPLETION id, unguarded — fires in the RTC cascade
                      new TransitionDef
                      {
                          SourceStateIndex = 1, TargetStateIndex = 2,
                          EventId = ReservedEventIds.Completion, GuardId = 0,
                          Flags = TransitionFlags.None,
                      },
                  }
                : new[]
                  {
                      new TransitionDef
                      {
                          SourceStateIndex = 0, TargetStateIndex = 1,
                          EventId = eventId, GuardId = GuardId,
                          Flags = polled ? TransitionFlags.IsPolled : TransitionFlags.None,
                      },
                  };

            var states = new StateDef[stateCount];
            states[0] = new StateDef
            {
                ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1,
                OnEntryActionId = 0xFFFF, OnExitActionId = 0xFFFF, TimerActionId = 0xFFFF,
                ActivityActionId = ActivityA,
                // ⭐ CE-381's DERIVED bit — the gate CE-382 tests before it looks at any transition.
                //   ⚠ suppressStateBit builds the INCONSISTENT blob CE382_R4 needs.
                Flags = (polled && !suppressStateBit) ? StateFlags.HasPolledTransition : StateFlags.None,
            };
            states[1] = new StateDef
            {
                ParentIndex = 0xFFFF,
                FirstTransitionIndex = withCompletionHop ? (ushort)1 : (ushort)0xFFFF,
                TransitionCount = withCompletionHop ? (ushort)1 : (ushort)0,
                OnEntryActionId = 0xFFFF, OnExitActionId = 0xFFFF, TimerActionId = 0xFFFF,
                ActivityActionId = ActivityB,
            };
            if (withCompletionHop)
                states[2] = new StateDef
                {
                    ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF,
                    OnEntryActionId = 0xFFFF, OnExitActionId = 0xFFFF, TimerActionId = 0xFFFF,
                    ActivityActionId = 0xFFFF,
                };

            return new HsmDefinitionBlob(
                header, states, transitions,
                Array.Empty<RegionDef>(), Array.Empty<GlobalTransitionDef>(),
                Array.Empty<ushort>(), Array.Empty<ushort>());
        }

        private static HsmInstance64 EnteredInStateZero()
        {
            var instance = new HsmInstance64();
            instance.Header.MachineId = MachineId;
            instance.Header.Phase     = InstancePhase.Idle;
            // ⚠ The CE-334 fixture trap: HsmInstance64 has TWO leaf slots and a default instance
            //   leaves BOTH at 0, so state 0 would read as active in region 1 too and every dispatch
            //   would double. A real entered machine parks unused regions at 0xFFFF.
            instance.ActiveLeafIds[0] = 0;
            instance.ActiveLeafIds[1] = 0xFFFF;
            return instance;
        }

        private static void Arrange(bool guardPasses)
        {
            HsmActionDispatcher.ClearAll();
            HsmActionDispatcher.RegisterGuard(
                GuardId, (IntPtr)(delegate* <void*, void*, ushort, HsmCommandWriter*, bool>)&Guard);
            HsmActionDispatcher.RegisterAction(
                ActivityA, (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&ActivityAThunk);
            HsmActionDispatcher.RegisterAction(
                ActivityB, (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&ActivityBThunk);
            _guardPasses = guardPasses;
            _guardCalls = _activityA = _activityB = 0;
        }

        private static void Cleanup()
        {
            HsmActionDispatcher.ClearAll();
            _guardCalls = _activityA = _activityB = 0;
        }

        // ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 🔴🔴🔴 <b><c>CE382_R1</c> — THE HEADLINE: a polled transition whose guard passes FIRES on a
        /// quiescent tick, with NO event posted and no timer armed.</b>
        /// ⭐ This is the capability the whole programme exists for; everything else refines it.
        /// </summary>
        [Fact]
        public void CE382_R1_APolledTransitionFires_WithNoEventPosted()
        {
            Arrange(guardPasses: true);
            try
            {
                var blob     = TwoStateBlob(polled: true, eventId: ReservedEventIds.Polled);
                var instance = EnteredInStateZero();
                var page     = new CommandPage();

                HsmKernel.Update(blob, ref instance, 0, 0.016f, ref page);

                Assert.Equal(1, instance.ActiveLeafIds[0]);
                Assert.True(_guardCalls > 0, "the guard must actually have been consulted");
            }
            finally { Cleanup(); }
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE382_R2</c> — SELECTIVITY: an UNMARKED transition's guard is NEVER called on a
        /// quiescent tick.</b>
        ///
        /// <para>🔒 This is the property that makes polling affordable, and the one the user asked
        /// for by name. ⛔ Without it the feature would evaluate every guard in the active
        /// configuration every frame — which is what the rejected tick-event design did.</para>
        /// </summary>
        [Fact]
        public void CE382_R2_AnUnmarkedTransitionsGuardIsNeverCalledOnAQuiescentTick()
        {
            Arrange(guardPasses: true);
            try
            {
                // Same machine, same guard — only IsPolled (and the derived state bit) differ.
                var blob     = TwoStateBlob(polled: false, eventId: 7);
                var instance = EnteredInStateZero();
                var page     = new CommandPage();

                for (int i = 0; i < 5; i++)
                    HsmKernel.Update(blob, ref instance, 0, 0.016f, ref page);

                Assert.Equal(0, _guardCalls);
                Assert.Equal(0, instance.ActiveLeafIds[0]);   // never moved
            }
            finally { Cleanup(); }
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE382_R5</c> — the NEWLY ENTERED state's activity runs IN THE SAME TICK.</b>
        ///
        /// <para>⛔ This is why the polled check runs BEFORE <c>ProcessActivityPhase</c> and why RTC
        /// is delegated INLINE rather than parked. Parking in <c>RTC</c> would cost a tick per phase
        /// — the very thing that disqualified the tick-event design. 📄 §3.1, §9 ⑤.</para>
        /// </summary>
        [Fact]
        public void CE382_R5_TheNewStatesActivityRunsInTheSameTick()
        {
            Arrange(guardPasses: true);
            try
            {
                var blob     = TwoStateBlob(polled: true, eventId: ReservedEventIds.Polled);
                var instance = EnteredInStateZero();
                var page     = new CommandPage();

                HsmKernel.Update(blob, ref instance, 0, 0.016f, ref page);

                Assert.Equal(1, instance.ActiveLeafIds[0]);
                Assert.Equal(1, _activityB);
                Assert.Equal(0, _activityA);   // ⛔ the state being LEFT must not tick
            }
            finally { Cleanup(); }
        }

        /// <summary>
        /// ⭐⭐ <b><c>CE382_R3</c> — a FAILING guard does not move the machine, and the CURRENT state's
        /// activity still runs every tick.</b> ⚠ The polled arm must not disturb <c>CE-334</c>.
        /// </summary>
        [Fact]
        public void CE382_R3_AFailingGuardLeavesTheMachinePut_AndActivitiesStillRun()
        {
            Arrange(guardPasses: false);
            try
            {
                var blob     = TwoStateBlob(polled: true, eventId: ReservedEventIds.Polled);
                var instance = EnteredInStateZero();
                var page     = new CommandPage();

                for (int i = 0; i < 3; i++)
                    HsmKernel.Update(blob, ref instance, 0, 0.016f, ref page);

                Assert.Equal(0, instance.ActiveLeafIds[0]);
                Assert.Equal(3, _guardCalls);   // polled ⇒ re-evaluated EVERY tick
                Assert.Equal(3, _activityA);    // CE-334 still holds
                Assert.Equal(0, _activityB);
            }
            finally { Cleanup(); }
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE382_R6</c> — the COMPLETION CASCADE runs after a polled transition, in the same
        /// tick.</b>
        ///
        /// <para>🔒 This is what delegating to <c>ProcessRTCPhase</c> buys, and why the arm does not
        /// hand-roll "execute one transition": after the polled hop A→B, the RTC loop sets
        /// <c>currentEventId = 0</c> and B's COMPLETION transition B→C fires — exactly as it would
        /// after an event-driven transition. ⛔ A hand-rolled executor would have given polled
        /// transitions second-class semantics. 📄 §2.3.</para>
        /// </summary>
        [Fact]
        public void CE382_R6_TheCompletionCascadeRunsAfterAPolledTransition()
        {
            Arrange(guardPasses: true);
            try
            {
                var blob     = TwoStateBlob(polled: true, eventId: ReservedEventIds.Polled,
                                            withCompletionHop: true);
                var instance = EnteredInStateZero();
                var page     = new CommandPage();

                HsmKernel.Update(blob, ref instance, 0, 0.016f, ref page);

                Assert.Equal(2, instance.ActiveLeafIds[0]);
            }
            finally { Cleanup(); }
        }

        /// <summary>
        /// ⭐⭐ <b><c>CE382_R4</c> — the GATE: a machine whose states carry no
        /// <c>HasPolledTransition</c> bit never consults a guard, even when a transition IS marked
        /// polled.</b>
        ///
        /// <para>⚠ <b>This rail deliberately builds an INCONSISTENT blob</b> — transition marked,
        /// state bit clear — which the flattener can never produce (<c>CE-381</c> derives one from
        /// the other). ⭐ It is here to prove the gate is what does the skipping, i.e. that the cheap
        /// bit test is load-bearing rather than decorative.</para>
        /// </summary>
        [Fact]
        public void CE382_R4_TheStateBitIsTheGate_NoBitMeansNoGuardEvaluation()
        {
            Arrange(guardPasses: true);
            try
            {
                // Transition marked polled, derived state bit deliberately NOT set.
                var blob = TwoStateBlob(polled: true, eventId: ReservedEventIds.Polled,
                                        suppressStateBit: true);

                var instance = EnteredInStateZero();
                var page     = new CommandPage();

                for (int i = 0; i < 3; i++)
                    HsmKernel.Update(blob, ref instance, 0, 0.016f, ref page);

                Assert.Equal(0, _guardCalls);
                Assert.Equal(0, instance.ActiveLeafIds[0]);
            }
            finally { Cleanup(); }
        }
    }
}
