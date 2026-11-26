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
            /// <summary>⭐ CE-4670 D-10 A — the commander's hatch rule (out while calm, in on a threat).</summary>
            public CrewHatchParams Hatch;
            /// <summary>⭐ CE-4670 D-10 B — the light discipline rule (standing, or an order's while it runs).</summary>
            public CrewLightsParams Lights;
            /// <summary>⭐ CE-4670 VA-S5 — "pop smoke": a salvo on a launch at it or a hit it survived, then a cool-down.</summary>
            public CrewSmokeParams Smoke;
            /// <summary>⭐ CE-4670 VA-S5 — a guided missile launched at it (fresh, 3 s).</summary>
            public SopSenseParams LaunchSense;

            public Blackboard()
            {
                AirSense    = new SopSenseParams { Kind = SensorChange.AirThreat,   Seconds = 3f };
                HitSense    = new SopSenseParams { Kind = SensorChange.Hit,         Seconds = 3f };
                ThreatSense = new SopSenseParams { Kind = SensorChange.FirstThreat, Seconds = 3f };
                AirDefence  = new EngageAircraftParams { ClearSeconds = 10f, MinElevationDeg = 80f };
                Hatch       = new CrewHatchParams();
                Smoke       = new CrewSmokeParams();
                LaunchSense = new SopSenseParams { Kind = SensorChange.MissileWarning, Seconds = 3f };
            }
        }

        /// <summary>An SOP takes no params (CE-2132): its variables are baked tuning, and a payload with fields is refused.</summary>
        [BehaviorResolver(BehaviorNames.BasicVehicleSop)]
        public static unsafe void Resolve(string json, byte* memory, int capacity, EntityRepository world, Entity self)
            => BehaviorParams.RequireNoStoredFields(json, BehaviorNames.BasicVehicleSop, typeof(NoParams).FullName!);

        [Behavior(BehaviorNames.BasicVehicleSop, Params = typeof(NoParams))]
        [BTreeDefinition(BehaviorNames.BasicVehicleSop, AssetId = "a2165000-0000-0000-0000-000000000001", Curated = true, Group = "Library/Sop",
            ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> Build()
            => new BTreeBuilder<Blackboard, BTreeContext>()
            .Selector(rows => rows
                .Inverter(side => side
                    .Action(bb => bb.Hatch, VehicleCrewNodes.HatchByThreat, Id("c4670000-0000-0000-0000-0000000000a2"))
                        .Label("Commander's hatch: out while calm, in on a threat"),
                    Id("c4670000-0000-0000-0000-0000000000a1"))
                    .Label("Side step: the crew's hatch (never a row)")
                    .Comment("CE-4670 D-10 A (VA-4): ticked first every frame and inverted, so it never wins the selector — the rows below still choose the reaction. Asks the boarding arbiter (an event) only when the wanted state changes.")
                .Inverter(side => side
                    .Action(bb => bb.Lights, VehicleCrewNodes.LightsByOrder, Id("c4670000-0000-0000-0000-0000000000b2"))
                        .Label("Lights: the order's discipline while it runs, else the standing one"),
                    Id("c4670000-0000-0000-0000-0000000000b1"))
                    .Label("Side step: the light discipline (never a row)")
                    .Comment("CE-4670 D-10 B (VA-3): an order carrying Lights overrides the vehicle's standing (authored) discipline while it runs; one ask per change, an event.")
                .Inverter(side => side
                    .Action(bb => bb.Smoke, VehicleCrewNodes.SmokeByThreat, Id("c4670000-0000-0000-0000-0000000000c2"))
                        .Label("Smoke: one salvo at a launch at me, or a hit I survived"),
                    Id("c4670000-0000-0000-0000-0000000000c1"))
                    .Label("Side step: pop smoke (never a row)")
                    .Comment("CE-4670 VA-S5 (VA-9): the dischargers fire at the threat's bearing, then a cool-down; never the weapon channel, so the gun's fire goes on. The move out of sight is the fight-from-cover row below.")
                .Sequence(row => row
                    .Condition(bb => bb.AirSense, SopConditions.SensedFresh, Id("c4617000-0000-0000-0000-0000000000b2"))
                        .Label("Air threat (fresh, 3 s)")
                    .Condition(RadarSamNodes.CanEngageByRadar, Id("c4617000-0000-0000-0000-0000000000b3"))
                        .Label("A radar and a radar-guided launcher with a round")
                    .React(BehaviorNames.RadarSamEngage, ReactionUrgency.UnderFire, Id("c4617000-0000-0000-0000-0000000000b4"))
                        .Label("React: RadarSamEngage"),
                    Id("c4617000-0000-0000-0000-0000000000b1"))
                    .Label("Aircraft + a radar SAM → radar on, salvo, relocate")
                    .Comment("CE-4617 (DESIGN_Radar_SAM RS-5): a radar SAM vehicle answers an air cue with its radar and a salvo, then switches off and relocates; an empty launcher falls through to the rows below.")
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
                    .Condition(bb => bb.LaunchSense, SopConditions.SensedFresh, Id("c4670000-0000-0000-0000-0000000000d1"))
                        .Label("Missile launched at me (fresh, 3 s)")
                    .React(BehaviorNames.VehicleFightFromCover, ReactionUrgency.Hit, Id("c4670000-0000-0000-0000-0000000000d2"))
                        .Label("React: VehicleFightFromCover"),
                    Id("c4670000-0000-0000-0000-0000000000d0"))
                    .Label("Launch at me → fight from cover")
                    .Comment("CE-4670 VA-S5: a launch is answered like a hit — smoke (the side step above) and out of the shooter's sight before the missile arrives.")
                .Condition(SopConditions.UnderCommand, Id("c4703000-0000-0000-0000-0000000000c1"))
                    .Label("Under a live commander → the commander reacts for the vehicle")
                    .Comment("CE-4703 (user 2026-11-26: \"agree with the lean\"; CE-2358 for vehicles): true = the row wins and nothing is ordered — a vehicle under a live commander (a platoon's play: the hill attack, the convoy) gets its fight from that play, so a first sight of the enemy does not take it out of the play. Self-defence stays its own: the hit, the launch at me and the air rows are ABOVE this row. 📐 ua-hill-attack: Tank 1 saw the enemy from the baseline at 5.2 s, its contact row paused the platoon's move, it fought alone and the pairs never crested.")
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
