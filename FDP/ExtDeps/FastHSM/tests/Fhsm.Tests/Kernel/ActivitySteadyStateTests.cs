using System;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Xunit;

namespace Fhsm.Tests.Kernel
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE STEADY-STATE RAIL — does a RUNNING machine keep running?</b> 📋 <c>CE-334</c>.
    ///
    /// <para>🔴🔴 <b>The dimension this suite was missing, and it is not a missing CASE.</b> 📐 Measured
    /// <c>2026-09-26</c>: every phase rail in <see cref="KernelEntryTests"/> hand-sets
    /// <c>Header.Phase</c>, calls <c>Update</c> <b>once</b>, and asserts the next phase —
    /// <c>Phase_RTC_Advances_To_Activity</c>, <c>Phase_Activity_Returns_To_Idle</c>. ⛔ The second one
    /// literally asserts the PARK, and no rail then asks <i>"and on the next tick?"</i></para>
    ///
    /// <para>⚠ <b>Multi-tick loops DO exist elsewhere</b> — <c>RegionScopedTransitionTests</c> ticks 4×,
    /// <c>CommandBufferIntegrationTests</c> ticks 5× with the comment <i>"Need multiple updates to
    /// process Idle -> Entry -> RTC -> Activity"</i>. 🔒 <b>That comment is the tell:</b> the suite
    /// treats several ticks as the cost of ONE logical step, never as <b>frames passing on a quiet
    /// machine</b>.</para>
    ///
    /// <para>⭐⭐ <b>Why it stayed invisible in the field:</b> the one shipped activity action —
    /// <c>ApcHsmActions.Activity_Cruise</c> — writes <c>loco.ActiveAction = ActionIdFollowRoute</c>,
    /// a <b>latching, idempotent</b> write. ⇒ once is sufficient, and a one-shot is indistinguishable
    /// from per-frame. ⛔ The first NON-latching consumer is a hosted BTree cursor, which must advance
    /// every frame. 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.2.1.</para>
    /// </summary>
    [Collection("HsmActionDispatcher")]
    public unsafe class ActivitySteadyStateTests
    {
        private const ushort ActivityId = 0x0CE3;
        private const uint   MachineId  = 0x0CE33400;

        private static int _ticks;

        /// <summary>The thunk under observation — it counts, and nothing else.</summary>
        private static void CountingActivity(void* instance, void* context, HsmCommandWriter* writer)
            => _ticks++;

        /// <summary>
        /// One state, no transitions, no regions, carrying an ACTIVITY action.
        /// ⚠ Built by hand rather than through <c>HsmBuilder</c> so the action id is exactly what the
        /// rail registers — ⛔ no dependency on how a name hashes.
        /// </summary>
        private static HsmDefinitionBlob BlobWithActivity()
        {
            var header = new HsmDefinitionHeader { StructureHash = MachineId, StateCount = 1 };

            var states = new[]
            {
                new StateDef
                {
                    ParentIndex          = 0xFFFF,
                    FirstTransitionIndex = 0xFFFF,
                    OnEntryActionId      = 0xFFFF,
                    OnExitActionId       = 0xFFFF,
                    TimerActionId        = 0xFFFF,
                    ActivityActionId     = ActivityId,
                },
            };

            return new HsmDefinitionBlob(
                header, states,
                Array.Empty<TransitionDef>(),
                Array.Empty<RegionDef>(),
                Array.Empty<GlobalTransitionDef>(),
                Array.Empty<ushort>(),
                Array.Empty<ushort>());
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE334_R1</c> — an ACTIVE state's activity action runs ON EVERY TICK, with an
        /// EMPTY event queue and no timers.</b>
        ///
        /// <para>🔴 <b>Before the fix this read 1.</b> The phase machine advances ONE PHASE PER TICK
        /// (<c>UpdateBatchCore</c> is a <c>for</c> over instances with no inner loop);
        /// <c>Activity</c> ran the activities and set <c>Idle</c>; and <c>Idle</c> left only on a
        /// non-empty queue ⇒ the machine PARKED and the activity never ran again.</para>
        ///
        /// <para>🔒 <b>What "every tick" means here:</b> the caller ticks the kernel once per frame, so
        /// an <c>ActivityAction</c> is the per-frame hook the name has always promised. ⛔ Anything
        /// less makes it a one-shot with a misleading name — which is what <c>CE-334</c> filed.</para>
        /// </summary>
        [Fact]
        public void CE334_R1_AnActiveStatesActivityRunsEveryTick_OnAQuiescentMachine()
        {
            HsmActionDispatcher.ClearAll();
            HsmActionDispatcher.RegisterAction(
                ActivityId,
                (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&CountingActivity);
            _ticks = 0;
            try
            {
                var blob     = BlobWithActivity();
                var instance = new HsmInstance64();
                instance.Header.MachineId = MachineId;
                instance.Header.Phase     = InstancePhase.Activity;   // entered, state 0 active

                // ⚠⚠ FIXTURE TRAP, measured while writing this rail: HsmInstance64 has TWO leaf
                //    slots and a default instance leaves BOTH at 0 — so state 0 reads as active in
                //    region 0 AND region 1, and every activity dispatch DOUBLES. 📐 The first run
                //    read 10 for 5 ticks. ⛔ A real entered machine parks unused regions at 0xFFFF;
                //    the fixture must too, or it measures the fixture rather than the kernel.
                instance.ActiveLeafIds[0] = 0;
                instance.ActiveLeafIds[1] = 0xFFFF;

                var page = new CommandPage();

                for (int i = 0; i < 5; i++)
                    HsmKernel.Update(blob, ref instance, 0, 0.016f, ref page);

                // ⭐⭐ THE RAIL. ⛔ Nothing enqueues an event and no timer is armed, so the machine is
                //    quiescent by construction — which is exactly the condition that used to park it.
                Assert.Equal(5, _ticks);
            }
            finally
            {
                HsmActionDispatcher.ClearAll();
                _ticks = 0;
            }
        }

        /// <summary>
        /// ⭐⭐ <b><c>CE334_R2</c> — a machine with NO activity action still parks in <c>Idle</c>, and
        /// costs nothing.</b>
        ///
        /// <para>⛔ The other half of the claim, and the one that protects every existing machine: the
        /// fix must make <c>Idle</c> RUN ACTIVITIES, not make it churn. ⚠ A state whose
        /// <c>ActivityActionId</c> is <c>0xFFFF</c> must dispatch nothing, tick after tick, and the
        /// phase must stay <c>Idle</c> so the existing phase rails keep their meaning.</para>
        /// </summary>
        [Fact]
        public void CE334_R2_AMachineWithNoActivityStaysIdleAndDispatchesNothing()
        {
            HsmActionDispatcher.ClearAll();
            HsmActionDispatcher.RegisterAction(
                ActivityId,
                (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&CountingActivity);
            _ticks = 0;
            try
            {
                var header = new HsmDefinitionHeader { StructureHash = MachineId, StateCount = 1 };
                var states = new[]
                {
                    new StateDef
                    {
                        ParentIndex          = 0xFFFF,
                        FirstTransitionIndex = 0xFFFF,
                        OnEntryActionId      = 0xFFFF,
                        OnExitActionId       = 0xFFFF,
                        TimerActionId        = 0xFFFF,
                        ActivityActionId     = 0xFFFF,   // ⛔ none
                    },
                };
                var blob = new HsmDefinitionBlob(
                    header, states,
                    Array.Empty<TransitionDef>(), Array.Empty<RegionDef>(),
                    Array.Empty<GlobalTransitionDef>(), Array.Empty<ushort>(), Array.Empty<ushort>());

                var instance = new HsmInstance64();
                instance.Header.MachineId = MachineId;
                instance.Header.Phase     = InstancePhase.Activity;
                instance.ActiveLeafIds[0] = 0;
                instance.ActiveLeafIds[1] = 0xFFFF;   // see the fixture note in R1
                var page = new CommandPage();

                for (int i = 0; i < 5; i++)
                    HsmKernel.Update(blob, ref instance, 0, 0.016f, ref page);

                Assert.Equal(0, _ticks);
                Assert.Equal(InstancePhase.Idle, instance.Header.Phase);
            }
            finally
            {
                HsmActionDispatcher.ClearAll();
                _ticks = 0;
            }
        }
    }
}
