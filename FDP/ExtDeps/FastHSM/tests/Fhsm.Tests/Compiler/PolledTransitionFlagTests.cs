using Xunit;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Compiler
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-381</c> — the POLLED transition flags.</b>
    /// 📄 <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §3.1, §8a ③, §8b.
    ///
    /// <para>⭐⭐ <b>What this file pins, and why each one is here rather than "obvious":</b>
    /// <list type="number">
    ///   <item>the two BIT POSITIONS — ⚠ <c>CE-395</c> proved bit drift is a LIVE failure mode in
    ///     this very enum, so the positions are asserted rather than trusted;</item>
    ///   <item><c>HasPolledTransition</c> is DERIVED on the SOURCE state, and only there;</item>
    ///   <item>a polled transition is normalised onto <c>ReservedEventIds.Polled</c>, so it can
    ///     never be selected by the event path — including the COMPLETION pass;</item>
    ///   <item>a machine with no polled transition is byte-for-byte unaffected, which is what makes
    ///     the shipped goldens safe.</item>
    /// </list></para>
    /// </summary>
    public class PolledTransitionFlagTests
    {
        private static StateMachineGraph Flatten(HsmBuilder builder, out HsmFlattener.FlattenedData flat)
        {
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            flat = HsmFlattener.Flatten(graph);
            return graph;
        }

        /// <summary>A two-state machine whose A→B transition is polled or not, as asked.</summary>
        private static HsmFlattener.FlattenedData TwoStates(bool polled, string? guard = null)
        {
            var b = new HsmBuilder("M");
            b.RegisterGuard("G");
            var a = b.State("A").Initial();
            b.State("B");

            var t = a.On(ReservedEventIds.Completion).GoTo("B");
            if (polled) t.Polled();
            if (guard != null) t.Guard(guard);

            Flatten(b, out var flat);
            return flat;
        }

        // ── 1. The bit positions themselves ───────────────────────────────────────────────

        /// <summary>
        /// ⚠⚠ <b>Not paranoia — <c>CE-395</c> is exactly this failure.</b> The flattener writes
        /// priority at bits 8-11 while the kernel reads bits 12-15, so every priority reads zero.
        /// ⭐ These two constants are read by the kernel's Idle arm (<c>CE-382</c>), so a silent
        /// renumber would make polling inert in the same invisible way.
        /// </summary>
        [Fact]
        public void TheFlagBitsAreWhereTheDesignSaysTheyAre()
        {
            Assert.Equal(1 << 6, (int)TransitionFlags.IsPolled);
            Assert.Equal(1 << 9, (int)StateFlags.HasPolledTransition);
        }

        /// <summary>
        /// ⭐⭐ <b><c>IsPolled</c> must not collide with the priority field — under EITHER spelling.</b>
        /// ⛔ The declared one is <c>Priority_Mask = 0xF000</c>; the one the flattener actually writes
        /// is bits 8-11 (<c>CE-395</c>). Bit 6 must be clear of both, and this says so explicitly so
        /// that fixing <c>CE-395</c> cannot quietly land on top of polling.
        /// </summary>
        [Fact]
        public void TheePolledBitCollidesWithNeitherPrioritySpelling()
        {
            Assert.Equal(0, (ushort)TransitionFlags.IsPolled & (ushort)TransitionFlags.Priority_Mask);
            Assert.Equal(0, (ushort)TransitionFlags.IsPolled & 0x0F00);   // CE-395's as-written bits
        }

        // ── 2. Authoring ──────────────────────────────────────────────────────────────────

        [Fact]
        public void TransitionNode_IsPolled_DefaultsFalse()
            => Assert.False(new TransitionNode().IsPolled);

        [Fact]
        public void Builder_Polled_SetsTheNodeProperty()
        {
            var b = new HsmBuilder("M");
            var a = b.State("A").Initial();
            b.State("B");
            a.On(ReservedEventIds.Completion).GoTo("B").Polled();

            var graph = b.Build();
            var src   = graph.FindState("A");
            Assert.NotNull(src);
            Assert.True(src!.Transitions[0].IsPolled);
        }

        // ── 3. The flattener ──────────────────────────────────────────────────────────────

        [Fact]
        public void Flattener_SetsIsPolled_OnTheTransitionDef()
        {
            var flat = TwoStates(polled: true);
            Assert.True((flat.Transitions[0].Flags & TransitionFlags.IsPolled) != 0);
        }

        [Fact]
        public void Flattener_LeavesIsPolledClear_WhenNotAuthored()
        {
            var flat = TwoStates(polled: false);
            Assert.True((flat.Transitions[0].Flags & TransitionFlags.IsPolled) == 0);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The DERIVED state bit — and it lands on the SOURCE state only.</b> This is the
        /// bit <c>CE-382</c>'s Idle arm tests once per active state, so a state that owns no polled
        /// transition costs a single bit test.
        /// </summary>
        [Fact]
        public void Flattener_DerivesHasPolledTransition_OnTheSourceStateOnly()
        {
            var b = new HsmBuilder("M");
            var a0 = b.State("A").Initial();
            b.State("B");
            a0.On(ReservedEventIds.Completion).GoTo("B").Polled();

            var graph = Flatten(b, out var flat);

            // ⭐ FlatIndex is stamped by the flattener, so the graph nodes are the index oracle.
            int a = graph.FindState("A")!.FlatIndex;
            int t = graph.FindState("B")!.FlatIndex;

            Assert.True((flat.States[a].Flags & StateFlags.HasPolledTransition) != 0,
                "the SOURCE state owns the polled transition, so it carries the derived bit");
            Assert.True((flat.States[t].Flags & StateFlags.HasPolledTransition) == 0,
                "the TARGET state owns no polled transition — the bit must not spread to it");
        }

        [Fact]
        public void Flattener_LeavesHasPolledTransitionClear_WhenNothingIsPolled()
        {
            var b = new HsmBuilder("M");
            var a1 = b.State("A").Initial();
            b.State("B");
            a1.On(ReservedEventIds.Completion).GoTo("B");

            Flatten(b, out var flat);

            foreach (var s in flat.States)
                Assert.True((s.Flags & StateFlags.HasPolledTransition) == 0);
        }

        // ── 4. POLLED and COMPLETION stay disjoint ────────────────────────────────────────

        /// <summary>
        /// 🔴🔴 <b>THE ONE THAT MATTERS MOST.</b> A polled transition authored as <c>.On(0)</c> would
        /// otherwise carry <c>EventId 0</c> — and <c>ProcessRTCPhase</c> sets <c>currentEventId = 0</c>
        /// after every executed transition, so the COMPLETION pass would select it too. ⭐ The
        /// flattener normalises it onto the reserved id, so the two paths cannot both claim it.
        /// 📄 design §2.3 (the correction) and §10 ③ (why the encodings were not merged).
        /// </summary>
        [Fact]
        public void APolledTransitionIsNormalisedOntoTheReservedId_SoCompletionCannotSelectIt()
        {
            var flat = TwoStates(polled: true);

            Assert.Equal(ReservedEventIds.Polled, flat.Transitions[0].EventId);
            Assert.NotEqual(ReservedEventIds.Completion, flat.Transitions[0].EventId);
        }

        /// <summary>⭐ The converse: an ordinary eventless transition KEEPS event 0 and stays a
        /// completion transition. ⛔ Normalisation must not capture transitions it was not asked for.</summary>
        [Fact]
        public void AnEventlessTransitionThatIsNotPolled_KeepsTheCompletionId()
        {
            var flat = TwoStates(polled: false);
            Assert.Equal(ReservedEventIds.Completion, flat.Transitions[0].EventId);
        }

        [Fact]
        public void TheReservedIdsAreDistinct()
        {
            Assert.NotEqual(ReservedEventIds.Polled, ReservedEventIds.Completion);
            Assert.NotEqual(ReservedEventIds.Polled, ReservedEventIds.Timer);
        }

        // ── 5. No churn for machines that do not use it ───────────────────────────────────

        /// <summary>
        /// ⭐⭐ <b>The golden-safety property, asserted rather than hoped.</b> Every state flag of a
        /// machine with no polled transition is exactly what it was before the feature existed — so
        /// <c>ComputeStructureHash</c>, which hashes <c>state.Flags</c>, cannot move for the four
        /// shipped assets. 📄 §8a ③.
        /// </summary>
        [Fact]
        public void AMachineWithNoPolledTransitionCarriesNoNewFlagBits()
        {
            var flat = TwoStates(polled: false);

            const StateFlags known =
                StateFlags.IsComposite | StateFlags.IsHistory | StateFlags.IsDeepHistory |
                StateFlags.IsParallel  | StateFlags.HasOnEntry | StateFlags.HasOnExit |
                StateFlags.HasOnUpdate | StateFlags.IsInitial  | StateFlags.IsFinal;

            foreach (var s in flat.States)
                Assert.Equal(StateFlags.None, s.Flags & ~known);

            foreach (var t in flat.Transitions)
                Assert.True((t.Flags & TransitionFlags.IsPolled) == 0);
        }

    }
}
