using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;
using static Hrot.AI.Behaviors.Brains.TreeNodeIds;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ agg-behaviors <c>ST-2</c> (docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §23.5; B-1, RV-7; routing row <c>(Unit, Stochastic, "Sop")</c> —
    /// <see cref="Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Sop"/>) — <c>StochasticUnitSop</c>, the ONE standing-orders tree of a stochastic unit at every echelon (the
    /// physical squad SOP's plays — ReactToContact, the carrier drills — never run on a memberless unit). Two rows, first true wins, nothing else (R-8310):
    /// <list type="number">
    /// <item><b>UNDER FIRE</b> (rounds arrived in the last <see cref="UnderFireSeconds"/> s) and the unit's order is NOT a Defend or an Attack ⇒ React
    /// <c>StochasticUnitBreakContact</c> (UnderFire urgency): a defender holds its ground — as <c>SquadDefendBehavior.NotDefending</c> spares a physical one — and ⭐ ST-3 (R-8317)
    /// an ATTACKER presses on (its play ends in TaskFailed when it breaks; a Move / March under fire still breaks contact).</item>
    /// <item><b>SUPPRESSED</b> (<see cref="AggregateUnit.SuppressedShare"/> ≥ <see cref="SuppressedAt"/>) ⇒ React <c>StochasticUnitHold</c> (Contact urgency, so a
    /// retreat already running is not stopped by it): the unit stops while suppressed and the paused order resumes when the share falls.</item>
    /// </list>
    /// ⭐ Parallel (R-4212): both conditions read only the unit's OWN <see cref="AggregateUnit"/> and its order; 0 B per tick.
    /// </summary>
    public static class StochasticUnitSop
    {
        /// <summary>⭐ R-8310 — the incoming rounds of the last this-many 1 s steps make the unit UNDER FIRE.</summary>
        public const int UnderFireSeconds = 3;

        /// <summary>⭐ R-8310 — the suppressed share (rounds per exposed man over the last 5 s ÷ 6, C-4) at which the unit holds.</summary>
        public const float SuppressedAt = 0.5f;

        /// <summary>
        /// ⭐ R-8310 — the unit received incoming rounds in the last <see cref="UnderFireSeconds"/> steps: the ring's three newest buckets (the 1 s steps phase B
        /// wrote), plus the bucket at the write head — the one the area sink (<c>StochasticAreaSink</c>) adds a burst to before phase B closes the step.
        /// </summary>
        public static bool UnderFire(in AggregateUnit u)
        {
            int n = AggregateUnit.SuppressionWindowSteps;
            int head = u.IncomingHead % n;
            if (u.IncomingRounds[head] > 0f) return true;
            for (int k = 1; k <= UnderFireSeconds; k++)
                if (u.IncomingRounds[((head - k) % n + n) % n] > 0f) return true;
            return false;
        }

        /// <summary>The unit is stochastic (an <see cref="AggregateUnit"/> simulated at that resolution) — a SOP of the table can only be started on one.</summary>
        private static bool Stochastic(Entity self, EntityRepository world) => AggregateUnits.IsStochastic(world, self);

        /// <summary>⭐ R-8317 — an order whose unit holds its ground under fire: a Defend (it is where it should be) and an Attack (an assault that breaks contact at the first round is no
        /// attack; the Attack play fails the order itself when the unit breaks). </summary>
        public static bool HoldsItsGround(OrderKind kind) => kind == OrderKind.Defend || kind == OrderKind.Attack;

        /// <summary>⭐ Row 1's guard: under fire AND its order is not one that holds its ground (<see cref="HoldsItsGround"/>).</summary>
        [SharedAiCondition]
        public static bool UnderFireNotDefending(Entity self, EntityRepository world)
            => Stochastic(self, world) && UnderFire(in world.GetComponentRO<AggregateUnit>(self))
               && !HoldsItsGround(CurrentOrders.Of(world, self).Kind);   // the NotDefending precedent (CE-2359, DA-55), + the attack (R-8317)

        /// <summary>The unit's suppressed share is at least <see cref="SuppressedAt"/>.</summary>
        public static bool Suppressed(Entity self, EntityRepository world)
            => Stochastic(self, world) && world.GetComponentRO<AggregateUnit>(self).SuppressedShare >= SuppressedAt;

        /// <summary>⭐ Row 2's guard: suppressed.</summary>
        [SharedAiCondition]
        public static bool IsSuppressed(Entity self, EntityRepository world) => Suppressed(self, world);

        [Behavior(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Sop, Params = typeof(NoParams))]
        [BTreeDefinition(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Sop, AssetId = "a8310000-0000-0000-0000-000000000001", Curated = true, Group = "Library/Sop")]
        public static BTreeBuilder<byte, BTreeContext> Build()
            => new BTreeBuilder<byte, BTreeContext>()
            .Selector(rows => rows
                .Sequence(row => row
                    .Condition(UnderFireNotDefending, Id("c8310000-0000-0000-0000-000000000011"))
                        .Label("Under fire (rounds in the last 3 s) AND not defending or attacking")
                        .Comment("R-8310 / R-8317: a defender holds its ground (the NotDefending precedent, CE-2359, DA-55) and an attacker presses on.")
                    .React(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.BreakContact, ReactionUrgency.UnderFire, Id("c8310000-0000-0000-0000-000000000012"))
                        .Label("React: StochasticUnitBreakContact")
                        .Comment("300 m away from the nearest contact (else behind its heading), as a column; Set on arrival, and the order resumes."),
                    Id("c8310000-0000-0000-0000-000000000010"))
                    .Label("Under fire → break contact")
                .Sequence(row => row
                    .Condition(IsSuppressed, Id("c8310000-0000-0000-0000-000000000021"))
                        .Label("Suppressed (share at least half)")
                    .React(StochasticUnitHoldBehavior.Name, ReactionUrgency.Contact, Id("c8310000-0000-0000-0000-000000000022"))
                        .Label("React: StochasticUnitHold")
                        .Comment("Stops where it stands while suppressed; succeeds when the share falls, and the paused order resumes."),
                    Id("c8310000-0000-0000-0000-000000000020"))
                    .Label("Suppressed → hold"),
                Id("c8310000-0000-0000-0000-000000000002"))
                .Label("Rows — first true wins")
                .Comment("ST-2 (B-1, RV-7, R-8310): the stochastic unit's standing orders at every echelon — under fire it breaks contact (unless it defends), suppressed it holds. Nothing else.");
    }
}
