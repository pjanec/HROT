using System;
using System.Collections.Generic;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>O6</c> — THE KERNEL STAMPS THE OCCURRENCE.</b>
    /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §4.2 <c>(Q35-A/B)</c>, <c>D2</c>, §9.4.
    ///
    /// <para>🔒 <b>The division of labour, and it is the sentence to remember:</b>
    /// <i>the kernel supplies IDENTITY, the thunk does the LOOKUP.</i> The kernel knows nothing about
    /// the partition allocator and must not learn; what a thunk lacked was four bytes of
    /// <i>"who am I"</i>. These rails pin the four bytes, not the lookup — the lookup is <c>O7</c>.</para>
    ///
    /// <para>⛔⛔ <b>Why the WRITER and not the bridge</b> — <c>Q35</c> option <c>C</c> was rejected for
    /// this: the writer is <b>per-dispatch</b>, the bridge is <b>per-entity-tick</b>, and the occurrence
    /// changes between two actions inside ONE tick. ⭐ Rail ② is that claim, measured — it is the rail
    /// that would redden if someone moved the stamp onto the bridge because it looked tidier.</para>
    ///
    /// <para>⚠ <b>These live HERE and not in <c>Fhsm.Tests</c> on purpose.</b> <c>Fhsm.Tests</c> is
    /// outside the root solution, so it reports a STALE BIN and cannot gate (trap ②). The claim is ours
    /// — it is the identity our key arithmetic will be computed from — so it belongs on our side of the
    /// seam, where a full-solution build actually rebuilds it.</para>
    /// </summary>
    public unsafe class HsmOccurrenceStampTests : IDisposable
    {
        // ── What the dispatched action/guard saw, recorded per dispatch ──────────────────
        // ⚠ Static because a function POINTER cannot close over state — which is also why the
        //   dispatcher takes raw pointers in the first place. Tests in this class are therefore
        //   not parallel-safe with each other; xUnit serialises a class's tests by default.

        private static readonly List<(int Region, ushort State)> Seen = new();

        private static void RecordingAction(void* instance, void* context, HsmCommandWriter* writer)
            => Seen.Add((writer->OccurrenceRegionSlotIndex, writer->OccurrenceStateId));

        private static bool RecordingGuard(void* instance, void* context, ushort eventId, HsmCommandWriter* writer)
        {
            Seen.Add((writer->OccurrenceRegionSlotIndex, writer->OccurrenceStateId));
            return true;
        }

        private const ushort EntryActionA = 0x0A01;
        private const ushort EntryActionB = 0x0A02;
        private const ushort ExitActionA = 0x0A03;
        private const ushort TransitionGuard = 0x0B01;
        private const ushort EventX = 10;

        /// <summary>The kernel never reads the context here — only the instance and the writer matter.</summary>
        private struct NoContext { public int Unused; }

        public HsmOccurrenceStampTests()
        {
            Seen.Clear();
            HsmActionDispatcher.ClearAll();
        }

        public void Dispose()
        {
            HsmActionDispatcher.ClearAll();
            Seen.Clear();
        }

        /// <summary>
        /// Two root-level states, each with its own OnEntry action, joined by one guarded transition:
        /// <c>State 0 --EventX[guard]--> State 1</c>. Small enough that every dispatch is accountable.
        /// </summary>
        private static HsmDefinitionBlob BuildTwoStateBlob(ushort guardId)
        {
            var states = new StateDef[2];
            states[0] = new StateDef
            {
                ParentIndex = 0xFFFF, FirstTransitionIndex = 0, TransitionCount = 1,
                OnEntryActionId = EntryActionA,
                OnExitActionId  = ExitActionA,
            };
            states[1] = new StateDef
            {
                ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF,
                OnEntryActionId = EntryActionB,
            };

            var transitions = new TransitionDef[1];
            transitions[0] = new TransitionDef
            {
                SourceStateIndex = 0, TargetStateIndex = 1, EventId = EventX, GuardId = guardId,
            };

            var header = new HsmDefinitionHeader
            {
                StructureHash = 0x0600A5E1,
                StateCount = 2,
                TransitionCount = 1,
            };

            return new HsmDefinitionBlob(
                header, states, transitions,
                Array.Empty<RegionDef>(), Array.Empty<GlobalTransitionDef>(),
                Array.Empty<ushort>(), Array.Empty<ushort>());
        }

        /// <summary>
        /// ⚠⚠ <b>Every region slot must be marked 0xFFFF first, and rail ③ is what found that out.</b>
        /// <c>HsmInstance128</c> carries FOUR region slots (<c>GetActiveLeafIds</c>: size 128 ⇒ count 4).
        /// A zeroed slot is not "empty" — <b>0 is a valid state index</b>, so the kernel read all four
        /// regions as sitting in State 0 and evaluated the transition's guard FOUR times. ⭐ The first
        /// version of rail ③ asserted a single dispatch and reported <i>"the collection contained 4
        /// items"</i> — a fixture defect, but the same confusion in production would be a real one.
        /// </summary>
        private static HsmInstance128 FreshInstance(HsmDefinitionBlob blob, InstancePhase phase, ushort leaf)
        {
            var inst = new HsmInstance128();
            inst.Header.MachineId = blob.Header.StructureHash;
            inst.Header.Phase = phase;
            for (int r = 0; r < HsmInstance128RegionSlots; r++)
                inst.ActiveLeafIds[r] = 0xFFFF;
            inst.ActiveLeafIds[0] = leaf;
            return inst;
        }

        /// <summary>GetActiveLeafIds maps instanceSize 128 to FOUR region slots.</summary>
        private const int HsmInstance128RegionSlots = 4;

        private static void RegisterRecordingAction(ushort id)
            => HsmActionDispatcher.RegisterAction(
                id, (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&RecordingAction);

        private static void Tick(HsmDefinitionBlob blob, ref HsmInstance128 inst)
        {
            var ctx = default(NoContext);
            var page = default(CommandPage);
            HsmKernel.Update(blob, ref inst, in ctx, 0.016f, ref page);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Rail ① — an action is told WHICH STATE it is running for.</b>
        ///
        /// <para>The instance enters at State 0, so State 0's OnEntry action runs and must see
        /// <c>(region 0, state 0)</c>. ⛔ <b>Red-proof:</b> delete the <c>StampOccurrence</c> call in
        /// <c>HsmKernelCore.ExecuteAction</c> and this reads the <c>NoStateId</c> sentinel instead.</para>
        /// </summary>
        [Fact]
        public void O6_R1_AnActionIsStampedWithItsOwnRegionAndState()
        {
            var blob = BuildTwoStateBlob(guardId: 0);
            RegisterRecordingAction(EntryActionA);

            // Uninitialised (leaf 0xFFFF) + Entry phase ⇒ the kernel initialises and enters State 0.
            var inst = FreshInstance(blob, InstancePhase.Entry, leaf: 0xFFFF);
            Tick(blob, ref inst);

            var dispatch = Assert.Single(Seen);
            Assert.Equal(0, dispatch.Region);
            Assert.Equal((ushort)0, dispatch.State);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Rail ② — TWO dispatches in ONE tick carry DIFFERENT stamps.</b>
        ///
        /// <para>🔴🔴 <b>This is the rail that decides <c>Q35-A</c>.</b> Option <c>C</c> put the
        /// occurrence on <c>HsmKernelBridge</c>, which is constructed once per entity tick. If the
        /// identity lived there, both dispatches below would report the SAME pair and the second
        /// occurrence would read the first one's storage — a silent cross-occurrence alias, which is
        /// the exact failure the whole occurrence model exists to remove.</para>
        ///
        /// <para>⭐ The transition fires within the tick, so State 0's exit path and State 1's entry
        /// happen in one <c>Update</c>. Only a PER-DISPATCH carrier can distinguish them.</para>
        /// </summary>
        [Fact]
        public void O6_R2_TwoDispatchesInOneTickCarryDifferentStamps()
        {
            var blob = BuildTwoStateBlob(guardId: 0);
            RegisterRecordingAction(ExitActionA);    // dispatched for State 0
            RegisterRecordingAction(EntryActionB);   // dispatched for State 1, same tick

            // Already in State 0, RTC phase, EventX pending ⇒ the transition runs this tick and
            // State 1's entry action follows it.
            var inst = FreshInstance(blob, InstancePhase.RTC, leaf: 0);
            inst.Reserved1 = EventX;   // the CurrentEventId scratch slot the kernel reads
            Tick(blob, ref inst);

            Assert.Equal((ushort)1, inst.ActiveLeafIds[0]);   // non-vacuity: the transition really ran

            // ⭐⭐ THE RAIL. Two dispatches, one tick, DIFFERENT stamps, in order: State 0 is exited
            //    and State 1 is entered. A per-entity-tick carrier would report the same pair twice.
            Assert.Equal(new[] { (0, (ushort)0), (0, (ushort)1) }, Seen);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Rail ③ — <c>D2</c>: a GUARD is stamped too.</b>
        ///
        /// <para>⛔ <c>Q35</c> accepted <i>"guards are unserved"</i> on a census that counted
        /// HAND-AUTHORED guards and missed the EMITTER. <c>AiPrimitiveHosting.HsmGuard</c> is a
        /// first-class hosting mode, so <b>"blueprint as an HSM condition"</b> — this design's own goal
        /// sentence — is precisely the composition an unserved guard cannot deliver.</para>
        ///
        /// <para>⭐ The guard is stamped with the state CARRYING the transition (State 0), which is the
        /// occurrence whose storage a blueprint condition would read.</para>
        /// </summary>
        [Fact]
        public void O6_R3_AGuardIsStampedWithTheStateCarryingItsTransition()
        {
            var blob = BuildTwoStateBlob(TransitionGuard);
            HsmActionDispatcher.RegisterGuard(
                TransitionGuard,
                (IntPtr)(delegate* <void*, void*, ushort, HsmCommandWriter*, bool>)&RecordingGuard);

            var inst = FreshInstance(blob, InstancePhase.RTC, leaf: 0);
            inst.Reserved1 = EventX;
            Tick(blob, ref inst);

            Assert.Equal((ushort)1, inst.ActiveLeafIds[0]);   // non-vacuity: the guard passed and it moved

            var dispatch = Assert.Single(Seen);
            Assert.Equal(0, dispatch.Region);
            Assert.Equal((ushort)0, dispatch.State);
        }

        /// <summary>
        /// ⭐⭐ <b>Rail ④ — "never stamped" is DISTINGUISHABLE from "region 0, state 0".</b>
        ///
        /// <para>⚠ Without sentinels the two read identically, and a thunk invoked outside a dispatch
        /// would silently resolve to the first region's first state — a plausible, wrong occurrence.
        /// ⭐ <c>NoStateId</c> is the kernel's own <c>0xFFFF</c> "no active leaf", so an unstamped
        /// writer reads the same as every other "no state" in the instance rather than inventing a
        /// second convention.</para>
        /// </summary>
        [Fact]
        public void O6_R4_AnUnstampedWriterReadsItsSentinels()
        {
            var page = default(CommandPage);
            var writer = new HsmCommandWriter(&page);

            Assert.Equal(HsmCommandWriter.NoRegionSlot, writer.OccurrenceRegionSlotIndex);
            Assert.Equal(HsmCommandWriter.NoStateId, writer.OccurrenceStateId);

            // ⚠ And the sentinels must not collide with a real stamp.
            Assert.NotEqual(0, HsmCommandWriter.NoRegionSlot);
            Assert.NotEqual((ushort)0, HsmCommandWriter.NoStateId);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Rail ⑤ — §9.4: the POINTER overload ticks a slot-resident instance, and agrees.</b>
        ///
        /// <para>🔴 <b>The deciding argument is MEMORY SAFETY, not convenience.</b> The generic overload
        /// takes its size from <c>sizeof(TInstance)</c> — a property of a type the caller CHOSE. The
        /// pointer overload takes it from the allocation. With occurrence payloads packed adjacently
        /// inside one component, an overstated size reads into the NEXT OCCURRENCE'S bytes with no
        /// compiler check and no runtime check.</para>
        ///
        /// <para>⭐ The rail: the same definition and the same starting bytes, ticked both ways, land in
        /// the same state — so the new route is the existing behaviour reached with a size that cannot
        /// disagree with the allocation.</para>
        /// </summary>
        [Fact]
        public void O6_R5_ThePointerOverloadMatchesTheGenericOne()
        {
            var blob = BuildTwoStateBlob(guardId: 0);
            RegisterRecordingAction(EntryActionB);

            var viaGeneric = FreshInstance(blob, InstancePhase.RTC, leaf: 0);
            viaGeneric.Reserved1 = EventX;
            Tick(blob, ref viaGeneric);

            var viaPointer = FreshInstance(blob, InstancePhase.RTC, leaf: 0);
            viaPointer.Reserved1 = EventX;
            {
                var ctx = default(NoContext);
                var page = default(CommandPage);
                // ⚠ A local of unmanaged type is already fixed — no `fixed` statement, and none needed.
                HsmKernel.Update(
                    blob, (byte*)&viaPointer, sizeof(HsmInstance128),
                    &ctx, 0.016f, &page);
            }

            Assert.Equal((ushort)1, viaGeneric.ActiveLeafIds[0]);   // non-vacuity: both really ticked
            Assert.Equal(viaGeneric.ActiveLeafIds[0], viaPointer.ActiveLeafIds[0]);
            Assert.Equal(viaGeneric.Header.Phase, viaPointer.Header.Phase);

            // ⭐ And both routes dispatched — the pointer overload is not a silent no-op.
            //   One entry-into-State-1 per route; State 0's exit action is not registered here.
            Assert.Equal(new[] { (0, (ushort)1), (0, (ushort)1) }, Seen);
        }

        /// <summary>
        /// ⭐ <b>Rail ⑥ — a nonsensical instance size is REFUSED, not read.</b>
        ///
        /// <para>⚠ The pointer overload's one risk is a caller that passes the wrong size. A
        /// non-positive one means the caller does not know how big its occurrence is, and reading
        /// zero bytes as an <c>InstanceHeader</c> is worse than throwing.</para>
        /// </summary>
        [Fact]
        public void O6_R6_ThePointerOverloadRefusesANonPositiveSize()
        {
            var blob = BuildTwoStateBlob(guardId: 0);
            var inst = FreshInstance(blob, InstancePhase.RTC, leaf: 0);

            // ⚠ Written without Assert.Throws: a lambda may not take the address of a local
            //   (CS1686), and the whole point of this overload is that it takes a POINTER.
            ArgumentOutOfRangeException? thrown = null;
            try
            {
                var ctx = default(NoContext);
                var page = default(CommandPage);
                HsmKernel.Update(blob, (byte*)&inst, 0, &ctx, 0.016f, &page);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                thrown = ex;
            }

            Assert.NotNull(thrown);
            Assert.Equal("instanceSize", thrown!.ParamName);
        }
    }
}
