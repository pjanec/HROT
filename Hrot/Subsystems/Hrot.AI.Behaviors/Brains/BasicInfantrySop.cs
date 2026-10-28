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
    /// ⭐⭐ <c>CE-1410</c> (R-1042) — <c>BasicInfantrySop</c>, a soldier's standing orders (CE-2080), as a C#-built tree: node for node the
    /// retired <c>Assets/BTrees/Library/Sop/BasicInfantrySop.btree.json</c>, its ids, titles and comments kept. A reaction table: each row is
    /// condition(s) → React; the idle row is last.
    /// </summary>
    public static class BasicInfantrySop
    {
        /// <summary>The tree's tuning — the variables the asset's blackboard carried, with their defaults (its constructor, R-3002).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public SopSenseParams AirSense;
            public SopSenseParams HitSense;
            public SopSenseParams ThreatSense;
            public SopRoeParams HoldFire;
            public EngageAircraftParams AirDefence;
            public SopSenseParams NearMissSense;

            public Blackboard()
            {
                AirSense      = new SopSenseParams { Kind = SensorChange.AirThreat,   Seconds = 3f };
                HitSense      = new SopSenseParams { Kind = SensorChange.Hit,         Seconds = 3f };
                ThreatSense   = new SopSenseParams { Kind = SensorChange.FirstThreat, Seconds = 3f };
                HoldFire      = new SopRoeParams { Fire = RoeFire.HoldFire };
                AirDefence    = new EngageAircraftParams { ClearSeconds = 10f, MinElevationDeg = 80f };
                NearMissSense = new SopSenseParams { Kind = SensorChange.NearMiss,    Seconds = 3f };
            }
        }

        /// <summary>An SOP takes no params (CE-2132): its variables are baked tuning, and a payload with fields is refused.</summary>
        [BehaviorResolver(BehaviorNames.BasicInfantrySop)]
        public static unsafe void Resolve(string json, byte* memory, int capacity, EntityRepository world, Entity self)
            => BehaviorParams.RequireNoStoredFields(json, BehaviorNames.BasicInfantrySop, typeof(NoParams).FullName!);

        private const string FightFromCoverWhy =
            "CE-2331 (AQ98 A/B, R-2070): fights from cover (PeekAndFire, per the ROE) and ends after 8 s quiet in cover, so the task resumes.";

        [Behavior(BehaviorNames.BasicInfantrySop, Params = typeof(NoParams))]
        [BTreeDefinition(BehaviorNames.BasicInfantrySop, AssetId = "a2080000-0000-0000-0000-000000000001", Curated = true, Group = "Library/Sop",
            ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> Build()
            => new BTreeBuilder<Blackboard, BTreeContext>()
            .Selector(rows => rows
                .Condition(SopConditions.UnderCommand, Id("c2080000-0000-0000-0000-0000000000c1"))
                    .Label("Under command → his unit reacts for him")
                    .Comment("CE-2322 (AQ97 A, R-2062): true = the row wins and nothing is ordered — a man under a live commander gets his reactions from his unit (its SOP, its plays); with no commander (or his commander out of the fight) the rows below are his.")
                .Condition(SopConditions.Aboard, Id("c2080000-0000-0000-0000-0000000000c2"))
                    .Label("Aboard a vehicle → the vehicle decides")
                    .Comment("CE-2322 (AQ97, as built): a crewman in his seat or a passenger — his seat and the vehicle's behaviour decide, not his own rows.")
                .Sequence(row => row
                    .Condition(bb => bb.AirSense, SopConditions.SensedFresh, Id("c2080000-0000-0000-0000-0000000000a2"))
                        .Label("Air threat (fresh, 3 s)")
                    .Condition(bb => bb.AirDefence, AirDefenceNodes.CanEngageAir, Id("c2080000-0000-0000-0000-0000000000a3"))
                        .Label("Can engage aircraft")
                    .React(BehaviorNames.EngageAircraft, ReactionUrgency.UnderFire, Id("c2080000-0000-0000-0000-0000000000a4"))
                        .Label("React: EngageAircraft"),
                    Id("c2080000-0000-0000-0000-0000000000a1"))
                    .Label("Aircraft + an air-defence weapon → engage")
                    .Comment("CE-2191 (Air Ops §7f): a unit that carries or controls an air-defence weapon (a SAM launcher, a gun elevating ≥ 80°) stands and fires at the aircraft; every other unit falls through to its old rows.")
                .Sequence(row => row
                    .Condition(bb => bb.HitSense, SopConditions.SensedFresh, Id("c2080000-0000-0000-0000-000000000011"))
                        .Label("Hit (fresh, 3 s)")
                    .React(BehaviorNames.FightFromCover, ReactionUrgency.Hit, Id("c2080000-0000-0000-0000-000000000012"))
                        .Label("React: FightFromCover")
                        .Comment(FightFromCoverWhy + " Stand-in reaction — swap for your project's take-cover behaviour in the picker."),
                    Id("c2080000-0000-0000-0000-000000000010"))
                    .Label("Hit → fight from cover")
                .Sequence(row => row
                    .Condition(bb => bb.NearMissSense, SopConditions.SensedFresh, Id("c2080000-0000-0000-0000-000000000016"))
                        .Label("Near miss (fresh, 3 s)")
                    .React(BehaviorNames.FightFromCover, ReactionUrgency.UnderFire, Id("c2080000-0000-0000-0000-000000000017"))
                        .Label("React: FightFromCover")
                        .Comment(FightFromCoverWhy + " CE-2322 (AQ97 B): rounds passing near him are reason enough."),
                    Id("c2080000-0000-0000-0000-000000000015"))
                    .Label("Shot at (near miss) → fight from cover")
                .Sequence(row => row
                    .Condition(bb => bb.ThreatSense, SopConditions.SensedFresh, Id("c2080000-0000-0000-0000-000000000021"))
                        .Label("First threat (fresh, 3 s)")
                    .Condition(bb => bb.HoldFire, SopConditions.RoeFireAtMost, Id("c2080000-0000-0000-0000-000000000022"))
                        .Label("ROE ≤ HoldFire")
                    .React(BehaviorNames.FallBack, ReactionUrgency.Contact, Id("c2080000-0000-0000-0000-000000000023"))
                        .Label("React: FallBack"),
                    Id("c2080000-0000-0000-0000-000000000020"))
                    .Label("Contact + hold fire → withdraw")
                .Sequence(row => row
                    .Condition(bb => bb.ThreatSense, SopConditions.SensedFresh, Id("c2080000-0000-0000-0000-000000000031"))
                        .Label("First threat (fresh, 3 s)")
                    .React(BehaviorNames.FightFromCover, ReactionUrgency.Contact, Id("c2080000-0000-0000-0000-000000000032"))
                        .Label("React: FightFromCover")
                        .Comment(FightFromCoverWhy),
                    Id("c2080000-0000-0000-0000-000000000030"))
                    .Label("Contact → fight from cover")
                .Sequence(row => row
                    .Condition(CoverFightConditions.EnemyInSight, Id("c2331000-0000-0000-0000-000000000002"))
                        .Label("Enemy in sight (seen within 1 s)")
                    .Condition(CoverFightConditions.NoTask, Id("c2331000-0000-0000-0000-000000000003"))
                        .Label("No task")
                    .Condition(CoverFightConditions.MayFire, Id("c2331000-0000-0000-0000-000000000005"))
                        .Label("May fire (ROE)")
                        .Comment("CE-3306: FightFromCover fights, so it runs only where the ROE lets him fire; under Hold Fire the withdraw row decides (D5).")
                    .React(BehaviorNames.FightFromCover, ReactionUrgency.Contact, Id("c2331000-0000-0000-0000-000000000004"))
                        .Label("React: FightFromCover"),
                    Id("c2331000-0000-0000-0000-000000000001"))
                    .Label("Enemy in sight, no task → fight from cover")
                    .Comment("CE-2332 (AQ98 C, R-2070): a man nobody gave anything to do engages an enemy he sees from where he is — never Idle with an enemy in sight.")
                .DoWhenIdle(BehaviorNames.Idle, Id("c2080000-0000-0000-0000-000000000040"))
                    .Label("Idle: Idle")
                    .Comment("What the unit does with no order."),
                Id("c2080000-0000-0000-0000-000000000002"))
                .Label("Rows — first true wins")
                .Comment("A reaction table: each row is condition(s) → React; the idle row is last. Row order picks WHICH reaction; urgency decides whether it may interrupt what already runs (R-199).");
    }
}
