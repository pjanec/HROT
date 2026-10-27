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
    /// ⭐⭐ <c>CE-1410</c> (R-1042) — <c>BasicVehicleSop</c>, a vehicle's reaction table (DESIGN_Vehicle_Exposure_Cycle §8), as a C#-built tree:
    /// node for node the retired <c>Assets/BTrees/Library/Sop/BasicVehicleSop.btree.json</c>, its ids, titles and comments kept.
    /// </summary>
    public static class BasicVehicleSop
    {
        /// <summary>The tree's tuning — the variables the asset's blackboard carried, with their defaults (its constructor, R-3002).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public SopSenseParams AirSense;
            public SopSenseParams HitSense;
            public SopSenseParams ThreatSense;
            public EngageAircraftParams AirDefence;

            public Blackboard()
            {
                AirSense    = new SopSenseParams { Kind = SensorChange.AirThreat,   Seconds = 3f };
                HitSense    = new SopSenseParams { Kind = SensorChange.Hit,         Seconds = 3f };
                ThreatSense = new SopSenseParams { Kind = SensorChange.FirstThreat, Seconds = 3f };
                AirDefence  = new EngageAircraftParams { ClearSeconds = 10f, MinElevationDeg = 80f };
            }
        }

        /// <summary>An SOP takes no params (CE-2132): its variables are baked tuning, and a payload with fields is refused.</summary>
        [BehaviorResolver(BehaviorNames.BasicVehicleSop)]
        public static unsafe void Resolve(string json, byte* memory, int capacity, EntityRepository world, Entity self)
            => BehaviorParams.RequireNoStoredFields(json, BehaviorNames.BasicVehicleSop, typeof(NoParams).FullName!);

        [Behavior(BehaviorNames.BasicVehicleSop, Params = typeof(NoParams))]
        [BTreeDefinition(BehaviorNames.BasicVehicleSop, AssetId = "a2165000-0000-0000-0000-000000000001", Curated = true, PublishesVariables = true, Group = "Library/Sop",
            ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> Build()
            => new BTreeBuilder<Blackboard, BTreeContext>()
            .Selector(rows => rows
                .Sequence(row => row
                    .Condition(bb => bb.AirSense, SopConditions.SensedFresh, Id("c2165000-0000-0000-0000-0000000000a2"))
                        .Label("Air threat (fresh, 3 s)")
                    .Condition(bb => bb.AirDefence, AirDefenceNodes.CanEngageAir, Id("c2165000-0000-0000-0000-0000000000a3"))
                        .Label("Can engage aircraft")
                    .React(BehaviorNames.EngageAircraft, ReactionUrgency.UnderFire, Id("c2165000-0000-0000-0000-0000000000a4"))
                        .Label("React: EngageAircraft"),
                    Id("c2165000-0000-0000-0000-0000000000a1"))
                    .Label("Aircraft + an air-defence weapon → engage")
                    .Comment("CE-2191 (Air Ops §7f): a unit that carries or controls an air-defence weapon (a SAM launcher, a gun elevating ≥ 80°) stands and fires at the aircraft; every other unit falls through to its old rows.")
                .Sequence(row => row
                    .Condition(bb => bb.AirSense, SopConditions.SensedFresh, Id("c2165000-0000-0000-0000-000000000011"))
                        .Label("Air threat (fresh, 3 s)")
                    .React(BehaviorNames.ShelterFromAir, ReactionUrgency.UnderFire, Id("c2165000-0000-0000-0000-000000000012"))
                        .Label("React: ShelterFromAir"),
                    Id("c2165000-0000-0000-0000-000000000010"))
                    .Label("Drone → shelter")
                .Sequence(row => row
                    .Condition(bb => bb.HitSense, SopConditions.SensedFresh, Id("c2165000-0000-0000-0000-000000000051"))
                        .Label("Hit (fresh, 3 s)")
                    .Condition(ConvoyConditions.CanPushThrough, Id("c2165000-0000-0000-0000-000000000052"))
                        .Label("Driving, mobile, troops aboard")
                    .React(BehaviorNames.PushThrough, ReactionUrgency.Hit, Id("c2165000-0000-0000-0000-000000000053"))
                        .Label("React: PushThrough"),
                    Id("c2165000-0000-0000-0000-000000000050"))
                    .Label("Hit while driving with troops → push through")
                    .Comment("CE-2307 (Squad Tactics §5b CH-1): a carrier never stops in the kill zone with its squad aboard; a standing, empty or immobile one fights from cover below.")
                .Sequence(row => row
                    .Condition(bb => bb.HitSense, SopConditions.SensedFresh, Id("c2165000-0000-0000-0000-000000000021"))
                        .Label("Hit (fresh, 3 s)")
                    .React(BehaviorNames.VehicleFightFromCover, ReactionUrgency.Hit, Id("c2165000-0000-0000-0000-000000000022"))
                        .Label("React: VehicleFightFromCover"),
                    Id("c2165000-0000-0000-0000-000000000020"))
                    .Label("Hit → fight from cover")
                .Sequence(row => row
                    .Condition(bb => bb.ThreatSense, SopConditions.SensedFresh, Id("c2165000-0000-0000-0000-000000000031"))
                        .Label("First threat (fresh, 3 s)")
                    .React(BehaviorNames.VehicleFightFromCover, ReactionUrgency.Contact, Id("c2165000-0000-0000-0000-000000000032"))
                        .Label("React: VehicleFightFromCover"),
                    Id("c2165000-0000-0000-0000-000000000030"))
                    .Label("Contact → fight from cover")
                .DoWhenIdle(BehaviorNames.Idle, Id("c2165000-0000-0000-0000-000000000040"))
                    .Label("Idle: Idle")
                    .Comment("What the vehicle does with no order."),
                Id("c2165000-0000-0000-0000-000000000002"))
                .Label("Rows — first true wins")
                .Comment("A vehicle's reaction table (DESIGN_Vehicle_Exposure_Cycle §8): a drone first, then being hit, then contact; the idle row is last. Row order picks WHICH reaction; urgency decides whether it may interrupt what already runs (R-199).");
    }
}
