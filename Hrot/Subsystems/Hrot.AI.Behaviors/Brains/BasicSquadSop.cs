using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Events;
using Hrot.Map.Definitions.Behavior;
using static Hrot.AI.Behaviors.Brains.TreeNodeIds;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-1410</c> (R-1042) — <c>BasicSquadSop</c>, the squad leader's standing orders (CE-2123 S2), as a C#-built tree: node for node
    /// the retired <c>Assets/BTrees/Library/Sop/BasicSquadSop.btree.json</c>, its ids, titles and comments kept. Each row is a SQUAD edge
    /// (published on the leader by <c>SquadCoordinationSystem</c>) → React; the reaction pauses the leader's task and the task resumes after.
    /// </summary>
    public static class BasicSquadSop
    {
        /// <summary>The tree's tuning — the variables the asset's blackboard carried, with their defaults (its constructor, R-3002).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            /// <summary>A squad member (or the leader) was hit or shot at recently (and since the current run began).</summary>
            public SopSenseParams UnderFire;
            /// <summary>The squad's leader soldier fell and the next man took over (CE-2125).</summary>
            public SopSenseParams LeaderDown;
            /// <summary>The squad's shared contacts went from empty to holding one.</summary>
            public SopSenseParams Contact;
            /// <summary>A squad member went down (incapacitated) — once per man (CE-2152 C-c).</summary>
            public SopSenseParams MemberDown;
            /// <summary>How near along the squad's route a danger area must be for the crossing drill (CE-2159).</summary>
            public SopAreaParams AreaAhead;

            public Blackboard()
            {
                UnderFire  = new SopSenseParams { Kind = SensorChange.SquadUnderFire,  Seconds = 3f };
                LeaderDown = new SopSenseParams { Kind = SensorChange.SquadLeaderDown, Seconds = 3f };
                Contact    = new SopSenseParams { Kind = SensorChange.SquadContact,    Seconds = 3f };
                MemberDown = new SopSenseParams { Kind = SensorChange.SquadMemberDown, Seconds = 3f };
                AreaAhead  = new SopAreaParams { WithinMetres = 30f };
            }
        }

        /// <summary>An SOP takes no params (CE-2132): its variables are baked tuning, and a payload with fields is refused.</summary>
        [BehaviorResolver(BehaviorNames.BasicSquadSop)]
        public static unsafe void Resolve(string json, byte* memory, int capacity, EntityRepository world, Entity self)
            => BehaviorParams.RequireNoStoredFields(json, BehaviorNames.BasicSquadSop, typeof(NoParams).FullName!);

        private const string NotDefendingWhy =
            "CE-2359 (AQ102 H, DA-55 at the squad level): a defending squad's defence answers the contact (its posts, displacement, flank turns, " +
            "the hunter) — ReactToContact would pause SquadDefend and take the men out of the house (measured live, rf-wall-hugger: two defenders " +
            "50-60 m out).";

        [Behavior(BehaviorNames.BasicSquadSop, Params = typeof(NoParams))]
        [BTreeDefinition(BehaviorNames.BasicSquadSop, AssetId = "a2123000-0000-0000-0000-000000000001", Curated = true, PublishesVariables = true, Group = "Library/Sop",
            ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> Build()
            => new BTreeBuilder<Blackboard, BTreeContext>()
            .Selector(rows => rows
                .Sequence(row => row
                    .Condition(SquadSopConditions.CarrierRoping, Id("c2123000-0000-0000-0000-000000000091"))
                        .Label("A member aboard a carrier on a Rope hold")
                    .React(BehaviorNames.SquadFastRope, ReactionUrgency.Contact, Id("c2123000-0000-0000-0000-000000000092"))
                        .Label("React: SquadFastRope")
                        .Comment("Contact, so it replaces a running SquadRide (Alert)."),
                    Id("c2123000-0000-0000-0000-000000000090"))
                    .Label("Carrier on a Rope hold → fast rope")
                    .Comment("CE-1118 H-1 (Airborne Insertion §6.3): the squad's helicopter hovers at a Rope stop — down the ropes one man at a time, then a ring round the rope point; the hold releases once nobody is aboard or on a rope.")
                .Sequence(row => row
                    .Condition(SquadSopConditions.CarrierDropping, Id("c2123000-0000-0000-0000-000000000094"))
                        .Label("A member aboard a carrier flying a Drop leg")
                    .React(BehaviorNames.SquadAirborneExit, ReactionUrgency.Contact, Id("c2123000-0000-0000-0000-000000000095"))
                        .Label("React: SquadAirborneExit")
                        .Comment("Contact, so it replaces a running SquadRide (Alert)."),
                    Id("c2123000-0000-0000-0000-000000000093"))
                    .Label("Carrier on its Drop leg → jump")
                    .Comment("CE-1118 H-1 (§6.4): the squad's aircraft flies a Drop leg — the jumpmaster sends the stick out at each man's release point, then the squad assembles.")
                .Sequence(row => row
                    .Condition(SquadSopConditions.CarrierUnloading, Id("c2123000-0000-0000-0000-000000000071"))
                        .Label("A member aboard a carrier that is unloading")
                    .React(BehaviorNames.SquadDismount, ReactionUrgency.Contact, Id("c2123000-0000-0000-0000-000000000072"))
                        .Label("React: SquadDismount")
                        .Comment("Contact, so it replaces a running SquadRide (Alert)."),
                    Id("c2123000-0000-0000-0000-000000000070"))
                    .Label("Carrier unloading the squad → dismount")
                    .Comment("CE-2173 slice 3 (Air Operations §9b): the squad's helicopter holds at an Unload stop — get out; once nobody is aboard the carrier's brain releases it and the squad's task resumes. Unloading, not 'landed', so a squad that has just boarded at a Pickup stop stays aboard.")
                .Sequence(row => row
                    .Condition(bb => bb.UnderFire, SquadSopConditions.SensedFreshAboard, Id("c2123000-0000-0000-0000-000000000041"))
                        .Label("Squad under fire (fresh, 3 s) AND a member aboard a carrier on the ground")
                    .React(BehaviorNames.SquadDismount, ReactionUrgency.UnderFire, Id("c2123000-0000-0000-0000-000000000042"))
                        .Label("React: SquadDismount")
                        .Comment("Everyone out beside the hull; done when nobody is aboard."),
                    Id("c2123000-0000-0000-0000-000000000040"))
                    .Label("Squad under fire while mounted → dismount")
                    .Comment("CE-1056 I-2d (Squad Tactics §5a): a carrier under fire puts its squad under fire; get everyone out first. Once nobody is aboard the drill ends and the rows below fight.")
                .Sequence(row => row
                    .Condition(SquadSopConditions.ContactWhileHalted, Id("c2123000-0000-0000-0000-0000000000b1"))
                        .Label("Enemy within 300 m, the carrier stopped")
                    .React(BehaviorNames.SquadDismount, ReactionUrgency.Contact, Id("c2123000-0000-0000-0000-0000000000b2"))
                        .Label("React: SquadDismount"),
                    Id("c2123000-0000-0000-0000-0000000000b0"))
                    .Label("Enemy near a halted carrier → dismount")
                    .Comment("CE-4032: the squad sits in a carrier that has stopped on the ground and an enemy is known within 300 m (by the squad, or by the carrier's crew) — get out and fight. Above the riding row: a squad in a carrier that DRIVES rides on. Measured live on uc-urban-battle: Squad A sat in a wedged APC for ten minutes, 50 m from the fight.")
                .Sequence(row => row
                    .Condition(SquadSopConditions.Riding, Id("c2123000-0000-0000-0000-000000000081"))
                        .Label("Every live member aboard a vehicle")
                    .React(BehaviorNames.SquadRide, ReactionUrgency.Alert, Id("c2123000-0000-0000-0000-000000000082"))
                        .Label("React: SquadRide")
                        .Comment("Ends the moment somebody is out."),
                    Id("c2123000-0000-0000-0000-000000000080"))
                    .Label("Squad riding → ride")
                    .Comment("CE-2173 slice 3 (Air Operations §9b): every live member is aboard a vehicle — nothing to do but ride; the task waits. Above the contact rows: a squad shot at in the air rides on (the dismount row above only fires on the ground).")
                .Condition(SopConditions.UnderCommand, Id("c2123000-0000-0000-0000-0000000000c1"))
                    .Label("Under a platoon → the platoon reacts for the squad")
                    .Comment("CE-2358 (AQ102 G, R-2072): true = the row wins and nothing is ordered — a squad under a live platoon gets its fight from its platoon's play (AQ97 A at the squad level); the carrier rows above stay the squad's own (mount, dismount, ride). Every squad type now carries this SOP by default.")
                .Sequence(row => row
                    .Condition(SquadDefendBehavior.NotDefending, Id("c2359000-0000-0000-0000-0000000000d1"))
                        .Label("Not defending a position")
                        .Comment(NotDefendingWhy)
                    .Condition(bb => bb.UnderFire, SopConditions.SensedFresh, Id("c2123000-0000-0000-0000-000000000011"))
                        .Label("Squad under fire (fresh, 3 s)")
                    .React(BehaviorNames.ReactToContact, ReactionUrgency.UnderFire, Id("c2123000-0000-0000-0000-000000000012"))
                        .Label("React: ReactToContact")
                        .Comment("Hold and return fire, decide once (assault / break contact / hold), run the drill."),
                    Id("c2123000-0000-0000-0000-000000000010"))
                    .Label("Squad under fire → react")
                .Sequence(row => row
                    .Condition(SquadDefendBehavior.NotDefending, Id("c2359000-0000-0000-0000-0000000000d3"))
                        .Label("Not defending a position")
                        .Comment(NotDefendingWhy)
                    .Condition(bb => bb.LeaderDown, SopConditions.SensedFresh, Id("c2123000-0000-0000-0000-000000000031"))
                        .Label("Squad leader down (fresh, 3 s)")
                    .React(BehaviorNames.ReactToContact, ReactionUrgency.UnderFire, Id("c2123000-0000-0000-0000-000000000032"))
                        .Label("React: ReactToContact")
                        .Comment("The next man took over (CE-2125): hold, then decide again with the weaker squad."),
                    Id("c2123000-0000-0000-0000-000000000030"))
                    .Label("Squad leader down → react")
                .Sequence(row => row
                    .Condition(SquadDefendBehavior.NotDefending, Id("c2359000-0000-0000-0000-0000000000d4"))
                        .Label("Not defending a position")
                        .Comment(NotDefendingWhy)
                    .Condition(bb => bb.MemberDown, SopConditions.SensedFresh, Id("c2123000-0000-0000-0000-000000000051"))
                        .Label("Squad member down (fresh, 3 s)")
                    .React(BehaviorNames.ReactToContact, ReactionUrgency.UnderFire, Id("c2123000-0000-0000-0000-000000000052"))
                        .Label("React: ReactToContact")
                        .Comment("CE-2152 C-c (R-2013): a man is down — the reaction scores Rescue against the fight (and with nothing to fight, brings him in)."),
                    Id("c2123000-0000-0000-0000-000000000050"))
                    .Label("Squad member down → react")
                .Sequence(row => row
                    .Condition(SquadSopConditions.CasualtyInALull, Id("c2123000-0000-0000-0000-0000000000a1"))
                        .Label("A man down to care for, no fire for 10 s")
                    .React(BehaviorNames.CasualtyCare, ReactionUrgency.Contact, Id("c2123000-0000-0000-0000-0000000000a2"))
                        .Label("React: CasualtyCare"),
                    Id("c2123000-0000-0000-0000-0000000000a0"))
                    .Label("Man down, a lull → bring him in")
                    .Comment("CE-2344 (R-2066 E, Squad Tactics §7.5.4): a man down still to care for and no round at the squad for 10 s — the squad drags him to cover itself (the care play: the others cover, the medic carries). Under fire the rows above react and the care waits.")
                .Sequence(row => row
                    .Condition(SquadDefendBehavior.NotDefending, Id("c2359000-0000-0000-0000-0000000000d2"))
                        .Label("Not defending a position")
                        .Comment(NotDefendingWhy)
                    .Condition(bb => bb.Contact, SopConditions.SensedFresh, Id("c2123000-0000-0000-0000-000000000021"))
                        .Label("Squad contact (fresh, 3 s)")
                    .React(BehaviorNames.ReactToContact, ReactionUrgency.Contact, Id("c2123000-0000-0000-0000-000000000022"))
                        .Label("React: ReactToContact")
                        .Comment("Hold and return fire, decide once (assault / break contact / hold), run the drill."),
                    Id("c2123000-0000-0000-0000-000000000020"))
                    .Label("Squad contact → react")
                .Sequence(row => row
                    .Condition(bb => bb.AreaAhead, SquadSopConditions.DangerAreaAhead, Id("c2123000-0000-0000-0000-000000000061"))
                        .Label("Danger area ahead (within 30 m, not yet crossed)")
                    .React(BehaviorNames.SquadDangerCrossing, ReactionUrgency.Alert, Id("c2123000-0000-0000-0000-000000000062"))
                        .Label("React: SquadDangerCrossing")
                        .Comment("Security watches the near side; the rest cross by pairs on new lanes; the far side watches; security follows; reform. The march resumes after."),
                    Id("c2123000-0000-0000-0000-000000000060"))
                    .Label("Danger area ahead → cross it")
                    .Comment("CE-2159 X-c (Squad Tactics §7.7.6): the march's route sensor shows a danger area within 30 m and the squad is not past it — cross it as a drill (Alert: any contact row replaces it)."),
                Id("c2123000-0000-0000-0000-000000000002"))
                .Label("Rows — first true wins")
                .Comment("The squad leader's standing orders: each row is a SQUAD edge (published on the leader by SquadCoordinationSystem) → React. The reaction pauses the leader's task; when it ends, the task resumes (CE-2123 S2).");
    }
}
