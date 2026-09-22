using System;
using System.Linq;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Behavior;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-308</c> — rails for <see cref="BehaviorParamSlotResolver"/>, the type the search
    /// compiler AND both field drawers bind through.
    ///
    /// <para>⚠ <b>Why this is the rail that matters.</b> The drawers decide what the user may pick;
    /// the compiler decides what can actually be bound. ⛔ If they disagree, the user builds a search
    /// that compiles to <c>(_, _) =&gt; false</c> and silently matches nothing — the same
    /// silent-wrong-answer shape as <c>CE-312</c>. Routing all three through one resolver makes that
    /// impossible, and these pin the resolver's contract.</para>
    ///
    /// <para>📐 Filed beside <c>PredicateCompilerTests</c> — the behaviour-param search suite — rather
    /// than inside it: that class owns a recording harness these need nothing from. ⚠ Worth recording
    /// that the suite carried <b>no</b> behaviour-param coverage at all before this.</para>
    /// </summary>
    public class BehaviorParamSlotResolverTests
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RootParams { public float Speed; }

        [StructLayout(LayoutKind.Sequential)]
        private struct StateA { public int Ticks; }

        [StructLayout(LayoutKind.Sequential)]
        private struct StateB { public int Hits; }

        private const int SlotA = 0x1111_1111;
        private const int SlotB = 0x2222_2222;

        private static BehaviorDefinition TwoStatefulSlots() => new BehaviorDefinition
        {
            Name                 = "T_TwoSlots",
            BrainTier            = BehaviorConstants.BrainTierBTree,
            BlackboardLayoutType = typeof(RootParams),
            StatefulWorkingSlots = new[]
            {
                new StatefulSlotInfo(SlotA, 4, 0, typeof(StateA), "ActionA", Role: 0, Scope: 0),
                new StatefulSlotInfo(SlotB, 4, 0, typeof(StateB), "ActionB", Role: 0, Scope: 1),
            },
        };

        // ── The case the retired enum could NOT express ──────────────────────

        /// <summary>
        /// ⭐⭐⭐ <b>Two stateful actions, and the predicate names WHICH one.</b> This is precisely
        /// what <c>BlackboardTarget</c> could not do: a two-member enum names a REGION, so with two
        /// working states it could not say which was meant, and the earlier "resolve it only when
        /// there is exactly one slot" proposal would have refused this case entirely.
        /// </summary>
        [Fact]
        public void ResolveDtoType_DistinguishesTwoWorkingSlotsOnOneBehaviour()
        {
            var def = TwoStatefulSlots();

            Assert.Same(typeof(StateA), BehaviorParamSlotResolver.ResolveDtoType(def, SlotA));
            Assert.Same(typeof(StateB), BehaviorParamSlotResolver.ResolveDtoType(def, SlotB));
        }

        [Fact]
        public void ResolveDtoType_SlotKeyZero_IsTheRootParamsRegion()
        {
            var def = TwoStatefulSlots();

            Assert.Same(typeof(RootParams), BehaviorParamSlotResolver.ResolveDtoType(
                def, BehaviorParamSlotResolver.RootParamsSlotKey));
        }

        [Fact]
        public void ResolveDtoType_UnknownSlot_ReturnsNull_SoTheCompilerRefuses()
        {
            var def = TwoStatefulSlots();

            Assert.Null(BehaviorParamSlotResolver.ResolveDtoType(def, unchecked((int)0xDEAD_BEEF)));
        }

        [Fact]
        public void ResolveDtoType_NullDefinition_ReturnsNull()
        {
            Assert.Null(BehaviorParamSlotResolver.ResolveDtoType(
                null, BehaviorParamSlotResolver.RootParamsSlotKey));
        }

        // ── The drawer/compiler agreement contract ───────────────────────────

        /// <summary>
        /// ⭐⭐ <b>Every choice a drawer offers must be one the compiler can bind.</b> That is the
        /// whole reason the two share this type, so it is asserted directly rather than trusted.
        /// </summary>
        [Fact]
        public void EveryOfferedChoice_ResolvesToTheSameTypeTheCompilerWouldBind()
        {
            var def = TwoStatefulSlots();

            var choices = BehaviorParamSlotResolver.GetChoices(def);

            Assert.NotEmpty(choices);
            foreach (var choice in choices)
                Assert.Same(choice.DtoType, BehaviorParamSlotResolver.ResolveDtoType(def, choice.SlotKey));
        }

        [Fact]
        public void GetChoices_ListsRootParamsFirst_ThenEachTypedSlot()
        {
            var def = TwoStatefulSlots();

            var choices = BehaviorParamSlotResolver.GetChoices(def);

            Assert.Equal(3, choices.Count);
            Assert.Equal(BehaviorParamSlotResolver.RootParamsSlotKey, choices[0].SlotKey);
            Assert.Contains("Root", choices[0].Label);
            Assert.Contains(choices, c => c.SlotKey == SlotA && c.Label.Contains("ActionA"));
            // Scope 1 is Behavior-scoped — the label says so, because a shared slot reads very
            // differently from a per-node one when you are choosing what to search.
            Assert.Contains(choices, c => c.SlotKey == SlotB && c.Label.Contains("Behavior"));
        }

        /// <summary>
        /// ⛔ A slot with no typed working state is NOT offered: a predicate cannot bind a property
        /// path against an untyped region, so listing it would offer a search that cannot compile.
        /// </summary>
        [Fact]
        public void GetChoices_OmitsUntypedSlots()
        {
            var def = new BehaviorDefinition
            {
                Name                 = "T_Untyped",
                BrainTier            = BehaviorConstants.BrainTierBTree,
                BlackboardLayoutType = typeof(RootParams),
                StatefulWorkingSlots = new[]
                {
                    new StatefulSlotInfo(SlotA, 4, 0, WorkingStateType: null, NodeLabel: "Untyped"),
                },
            };

            var choices = BehaviorParamSlotResolver.GetChoices(def);

            Assert.Single(choices);
            Assert.Equal(BehaviorParamSlotResolver.RootParamsSlotKey, choices[0].SlotKey);
        }

        /// <summary>
        /// A behaviour with no params and no typed slots offers nothing — the drawer then says so
        /// rather than showing an empty combo.
        /// </summary>
        [Fact]
        public void GetChoices_BehaviourWithNoTypedRegions_IsEmpty()
        {
            var def = new BehaviorDefinition
            {
                Name      = "T_Bare",
                BrainTier = BehaviorConstants.BrainTierBTree,
            };

            Assert.Empty(BehaviorParamSlotResolver.GetChoices(def));
        }
    }
}
