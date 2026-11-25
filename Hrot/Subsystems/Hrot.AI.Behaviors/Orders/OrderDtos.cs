using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Fbt.Kernel;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Navigation;
using Hrot.Map.Definitions.Behavior.Orders;

namespace Hrot.AI.Behaviors.Orders
{
    /// <summary>
    /// ⭐ <c>CE-2140</c> — the MOVE order: go to <see cref="Location"/>. The params DTO of every behaviour that fulfils Move
    /// (<c>InfantrySquadMove</c>, …). Flat JSON, e.g. <c>{"Location":[340,300,0],"Speed":2}</c>. A zero field keeps the fulfilling
    /// behaviour's own default. ⭐ <see cref="Location"/> is a <see cref="Fdp.Toolkit.Replication.PointLink"/> (S0 item 5): local metres
    /// <c>[x, y, z]</c>, <c>"@Key"</c> or both — the type alone makes the menu ask for a point (DESIGN_Map_Picking_Unification). ⛔ Not geodetic
    /// since S0 (it was a <c>PickableGeoPoint</c>; CE-2433).
    /// </summary>
    /// <summary>⭐ <c>CE-4670</c> D-10 B — an order's light discipline: <see cref="Unset"/> keeps the vehicle's own (standing) discipline; the rest name a
    /// <see cref="Fdp.Toolkit.Behavior.Components.LightDiscipline"/> by the same name.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OrderLights : byte { Unset = 0, Normal, Convoy, Blackout, Off }

    [Order(nameof(OrderKind.Move), Gesture = OrderGesture.GroundClick)]
    [StructLayout(LayoutKind.Sequential)]
    public struct MoveOrder
    {
        /// <summary>Where to go (local metres, S0). ⭐ <c>CE-2148</c> — required: a move without a point is refused.</summary>
        [Required]
        [JsonPropertyName("Location")]
        public Fdp.Toolkit.Replication.PointLink Location;

        /// <summary>The asked speed (m/s); 0 = the behaviour's default.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>How close counts as arrived (m); 0 = the behaviour's default.</summary>
        [JsonPropertyName("ArrivalRadius")]
        public float ArrivalRadius;

        /// <summary>How much to prefer roads.</summary>
        [JsonPropertyName("RoadUse")]
        public RoadUse RoadUse;
        /// <summary>⭐ <c>CE-4670</c> D-10 B — the vehicle's light discipline WHILE this order runs (a night move: <c>Convoy</c>; a covert one:
        /// <c>Blackout</c>); none ⇒ the vehicle keeps its standing (authored) discipline. Restored when the order ends (<c>VehicleCrewNodes.LightsByOrder</c>).</summary>
        [JsonPropertyName("Lights")]
        public OrderLights Lights;
    }

    /// <summary>
    /// ⭐ <c>CE-9004</c> (C-9, R-9008; 📄 docs/designs/vehicle-tactics/DESIGN_Road_Convoy.md) — the CONVOY order, given to EACH vehicle of the column: the lead
    /// (<see cref="Rank"/> 0, no <see cref="Lead"/>) drives to <see cref="Destination"/> — in SUMO traffic (<see cref="Mode"/> <c>Traffic</c>, with its beacon when
    /// <see cref="Priority"/>) or on our own route (<c>Route</c>, the outer lane); every other vehicle drives the lead's path at <see cref="Gap"/> behind the one
    /// ahead of it. Flat JSON, e.g. <c>{"Destination":[600,40,0],"Lead":"@escort","Rank":2,"Mode":"Route"}</c>.
    /// </summary>
    // ⭐ CE-1133 / CE-1978 (ui, cross-lane edit — said) — NO gesture: a ground right-click is the Move order's (ONE order per gesture). Bound to GroundClick as
    //   well, Convoy sorted first and took every RTS ground click — it needs a form (Lead, Rank), so no unit moved at all. DESIGN_Road_Convoy names no gesture.
    [Order(nameof(OrderKind.Convoy))]
    [StructLayout(LayoutKind.Sequential)]
    public struct ConvoyOrder
    {
        /// <summary>Where the column goes (the lead's destination; the followers stop with the lead).</summary>
        [Required]
        [JsonPropertyName("Destination")]
        public Fdp.Toolkit.Replication.PointLink Destination;

        /// <summary>The column's lead; none ⇒ this vehicle leads.</summary>
        [JsonPropertyName("Lead")]
        public Fdp.Toolkit.Replication.EntityLink Lead;

        /// <summary>The place in the column: 0 = the lead, 1 = right behind it …</summary>
        [JsonPropertyName("Rank")]
        public int Rank;

        /// <summary>Centre-to-centre spacing along the lead's path (m); 0 = 15 m.</summary>
        [JsonPropertyName("Gap")]
        public float Gap;

        /// <summary>How the lead drives: <c>Auto</c> · <c>Traffic</c> (handed to SUMO) · <c>Route</c> (our own route).</summary>
        [JsonPropertyName("Mode")]
        public CarKinem.Convoy.ConvoyMode Mode;

        /// <summary>The lead's beacon and siren (in SUMO traffic: a priority vehicle).</summary>
        [JsonPropertyName("Priority")]
        public bool Priority;

        /// <summary>The lead's speed (m/s); 0 = 10 m/s.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>⭐ <c>CE-9012</c> — an authored ROUTE the lead follows (as <c>FollowRoute</c> does) instead of routing to <see cref="Destination"/> itself;
        /// none ⇒ the lead routes. With a route the lead drives our own path (the route), whatever <see cref="Mode"/> says. A platoon's convoy sends it.</summary>
        [JsonPropertyName("Route")]
        [Fdp.Toolkit.Behavior.Attributes.MapPickableEntity("road_graphs")]
        public Fdp.Toolkit.Replication.EntityLink Route;
        /// <summary>⭐ <c>CE-4670</c> D-10 B — the vehicle's light discipline WHILE this order runs (a night move: <c>Convoy</c>; a covert one:
        /// <c>Blackout</c>); none ⇒ the vehicle keeps its standing (authored) discipline. Restored when the order ends (<c>VehicleCrewNodes.LightsByOrder</c>).</summary>
        [JsonPropertyName("Lights")]
        public OrderLights Lights;
    }

    /// <summary>
    /// ⭐ <c>CE-4693</c> (bridge laying BL-S3, BL-11; 📄 docs/designs/engineering/DESIGN_Bridge_Laying.md §12) — the LAY BRIDGE order, given to a bridge engineer: lay its span
    /// across the water between <see cref="NearBank"/> (the side it comes from) and <see cref="FarBank"/>. The deck is centred on the water gap along that line. ⛔ Refused, with its
    /// reason, when the gap is wider than the engineer's span, a bank (or a deck end) is in the water, or there is no water between them. Flat JSON, e.g.
    /// <c>{"NearBank":[80,50,0],"FarBank":[80,82,0]}</c>. ⚠ The two-point map pick is the UI's gesture (a NOTE to ui); HTTP and a commander's seat give two points now.
    /// </summary>
    [Order(nameof(OrderKind.LayBridge))]
    [StructLayout(LayoutKind.Sequential)]
    public struct LayBridgeOrder
    {
        /// <summary>The bank the engineer lays from (local metres, S0).</summary>
        [Required]
        [JsonPropertyName("NearBank")]
        public Fdp.Toolkit.Replication.PointLink NearBank;

        /// <summary>The bank across the water.</summary>
        [Required]
        [JsonPropertyName("FarBank")]
        public Fdp.Toolkit.Replication.PointLink FarBank;

        /// <summary>Drive speed to the bank (m/s); 0 = the behaviour's default.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;
    }

    /// <summary>
    /// ⭐ <c>CE-4693</c> (BL-S3) — the RECOVER BRIDGE order: the engineer drives to the nearer end of <see cref="Bridge"/> (a laid bridge it can carry) and takes it back on board.
    /// No bridge named ⇒ the nearest laid bridge of the type it carries (within <c>BridgeLayingBehaviors.NearestBridgeMetres</c>) — a bridge laid during the run has no
    /// authored name to give. Flat JSON, e.g. <c>{"Bridge":"@Bridge"}</c> or <c>{}</c>.
    /// </summary>
    [Order(nameof(OrderKind.RecoverBridge))]
    [StructLayout(LayoutKind.Sequential)]
    public struct RecoverBridgeOrder
    {
        /// <summary>The laid bridge to take back; none = the nearest of the type the engineer carries.</summary>
        [JsonPropertyName("Bridge")]
        public Fdp.Toolkit.Replication.EntityLink Bridge;

        /// <summary>Drive speed to the deck end (m/s); 0 = the behaviour's default.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;
    }

    /// <summary>
    /// ⭐ <c>CE-2140</c> — the ATTACK order: close in on <see cref="Target"/> and fight it until it is out of the fight. ⚠ ROE still
    /// applies. ⭐ <see cref="Target"/> is an <see cref="EntityRef"/>, so the type alone makes the menu ask for an entity.
    /// <para>⭐ agg-behaviors R-8303 / R-8316 (ST-3): the attack may be on a <see cref="Position"/> instead — a commander's platoon attack on a point has no entity
    /// (<c>Target OR Position</c> required, <see cref="RequiredAnyOfAttribute"/>); the map menu still asks for an entity (the EnemyClick gesture, the pick is
    /// <see cref="Target"/>). <see cref="SearchRadius"/>, <see cref="Speed"/> and <see cref="HoldBack"/> are the optional extras a commander sends (R-8300: every
    /// play that fulfils Attack accepts them strictly).</para>
    /// </summary>
    [Order(nameof(OrderKind.Attack), Gesture = OrderGesture.EnemyClick)]
    [StructLayout(LayoutKind.Sequential)]
    public struct AttackOrder
    {
        /// <summary>The entity to attack. ⭐ <c>CE-2148</c> — required unless a <see cref="Position"/> is given (R-8316).</summary>
        [RequiredAnyOf("Objective")]
        [JsonPropertyName("Target")]
        public Fdp.Toolkit.Replication.EntityLink Target;

        /// <summary>⭐ R-8316 — the place to attack (local metres) when there is no entity; the objective is then the place itself.</summary>
        [RequiredAnyOf("Objective")]
        [JsonPropertyName("Position")]
        public Fdp.Toolkit.Replication.PointLink Position;

        /// <summary>The objective is taken when no enemy stands within this distance of it (m); 0 = the play's default.</summary>
        [JsonPropertyName("SearchRadius")]
        public float SearchRadius;

        /// <summary>The asked speed (m/s); 0 = the play's default.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>⭐ R-8303 — support by fire: engage from a distance and DO NOT close (the base of fire), never ending by itself. ⭐ CE-8302 (R-8341): with a
        /// <see cref="Position"/>, that position is WHERE the unit stands (the commander's support position — the same point its physical base of fire gets), not what it fires at.</summary>
        [JsonPropertyName("HoldBack")]
        public bool HoldBack;
    }

    /// <summary>
    /// ⭐ <c>CE-2140</c> — the HOLD order: stay where you are. Empty, but its OWN type, because the type identifies the order
    /// (<c>NoParams</c> means "this behaviour takes nothing", not "hold").
    /// </summary>
    [Order(nameof(OrderKind.Hold))]
    [StructLayout(LayoutKind.Sequential)]
    public struct HoldOrder
    {
    }

    /// <summary>
    /// ⭐ <c>CE-2173</c> (B4, R-2015; 📄 docs/designs/air-attack/DESIGN_Air_Operations.md §7) — the STRIKE order: hit <see cref="Location"/>
    /// (an optional <see cref="Target"/> entity there is tracked). Every aircraft fulfils it through <c>AirPlatformStrike</c>; the attack
    /// pattern is chosen from the aircraft and its weapons, never by the sender. ⭐ No RTS gesture: a ground click is a Move — Strike is
    /// a menu / HTTP order.
    /// </summary>
    [Order(nameof(OrderKind.Strike))]
    [StructLayout(LayoutKind.Sequential)]
    public struct StrikeOrder
    {
        /// <summary>Where to strike (geodetic). Required.</summary>
        [Required]
        [JsonPropertyName("Location")]
        public Fdp.Toolkit.Replication.PointLink Location;

        /// <summary>An entity at the place to track (optional).</summary>
        [JsonPropertyName("Target")]
        public Fdp.Toolkit.Replication.EntityLink Target;

        // ⭐⭐ CE-2176 IF-2 (IF-D, R-2018; 📄 docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §4) — the FIRE MISSION fields: a battery
        //   or a self-propelled gun reads them; an aircraft reads only Location / Target. A zero field keeps the RTS default
        //   (fire for effect, when ready, converged, 3 rounds).

        /// <summary>Fire for effect, or adjust first with one gun (IF-3; until then an Adjust fires for effect).</summary>
        [JsonPropertyName("Method")]
        public FireMissionMethod Method;

        /// <summary>Rounds per gun; 0 = 3.</summary>
        [JsonPropertyName("Rounds")]
        public int Rounds;

        /// <summary>Where each gun aims: all on the point, each at its own place in the battery, or spread over the width.</summary>
        [JsonPropertyName("Sheaf")]
        public FireMissionSheaf Sheaf;

        /// <summary>An open sheaf's width (m); 0 = guns × twice the burst's injury radius.</summary>
        [JsonPropertyName("SheafWidthM")]
        public float SheafWidthM;

        /// <summary>When the guns fire: as each is ready, or so every first round lands together.</summary>
        [JsonPropertyName("Control")]
        public FireMissionControl Control;

        /// <summary>Time on target: the impact this many seconds from now at the earliest; 0 = as soon as the slowest round can.</summary>
        [JsonPropertyName("ImpactInS")]
        public float ImpactInS;

        /// <summary>The observer who called it (IF-3).</summary>
        [JsonPropertyName("Observer")]
        public Fdp.Toolkit.Replication.EntityLink Observer;

        /// <summary>Fire although friends are within 600 m of the point (FM-5).</summary>
        [JsonPropertyName("DangerClose")]
        public bool DangerClose;

        /// <summary>⭐ <c>CE-2173</c> slice 6c (W1, R-2020) — what the point IS (why it is struck): the kind picks the weapon class — a
        /// building, a bunker, a bridge or a runway takes a heavy weapon, never a gun (Air Operations §9e). Unspecified = any.</summary>
        [JsonPropertyName("TargetKind")]
        public StrikeTargetKind TargetKind;

        /// <summary>⭐ <c>CE-7025</c> (City Battle §5, IF-5) — fire SMOKE (each gun loads its smoke round): a screen, no blast.</summary>
        [JsonPropertyName("Smoke")]
        public bool Smoke;
    }

    /// <summary>
    /// ⭐ agg-behaviors R-8300 (docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §23.5) — the DEFEND order: hold
    /// <see cref="Position"/> (the engagement area's origin). The ONE neutral DTO of the kind — the union of what the commanders send;
    /// ⭐ every play that fulfils Defend (<c>SquadDefend</c>, <c>PlatoonDefend</c>, <c>CompanyDefend</c>) accepts every field strictly (a
    /// rail walks the pairs), a field its echelon does not use is accepted and documented as unused there. A zero field keeps the
    /// fulfilling play's own default. Field names and types are the plays' own, so the JSON is the same whichever resolves it.
    /// </summary>
    [Order(nameof(OrderKind.Defend))]
    [StructLayout(LayoutKind.Sequential)]
    public struct DefendOrder
    {
        /// <summary>The position defended (local metres).</summary>
        [JsonPropertyName("Position")]
        public Fdp.Toolkit.Replication.PointLink Position;

        /// <summary>Posts / the line are looked for within this distance of the position (m); 0 = the play's default.</summary>
        [JsonPropertyName("SearchRadius")]
        public float SearchRadius;

        /// <summary>The asked speed (m/s); 0 = the play's default.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>Shoot and move between posts after this many seconds at one (CE-7007); 0 = the play's default.</summary>
        [JsonPropertyName("ShootAndMoveSeconds")]
        public float ShootAndMoveSeconds;

        /// <summary>The engagement area's left compass bearing (°, 0 = north, clockwise); Left = Right = 0 ⇒ the unit's own sector.</summary>
        [JsonPropertyName("Left")]
        public Fdp.Core.Bearing Left;

        /// <summary>The engagement area's right compass bearing (°).</summary>
        [JsonPropertyName("Right")]
        public Fdp.Core.Bearing Right;

        /// <summary>Where to fall back to when the position fails (local metres); zero = the play's own choice.</summary>
        [JsonPropertyName("FallBackTo")]
        public Fdp.Toolkit.Replication.PointLink FallBackTo;

        /// <summary>Where the enemy comes from (local metres); zero = the play's own choice.</summary>
        [JsonPropertyName("Threat")]
        public Fdp.Toolkit.Replication.PointLink Threat;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4686</c> ④ (the orders design's R-8300 pattern; a user-authorised cross-lane edit, `2026-11-24`) — the BOUND order: move to
    /// <see cref="Position"/> by bounds, one element covering the other, the covering element first setting at <see cref="SupportPoint"/>. The ONE
    /// neutral DTO of the kind; ⭐ every play that fulfils Bound (<c>PlatoonBound</c>, <c>BoundingOverwatch</c>) accepts every field strictly (the
    /// R-8300 rail walks the pairs), and a stochastic unit's generic attack (the table routes Bound there) reads the same <see cref="Position"/> — so
    /// ONE Bound order means the same to a physical and a stochastic subordinate. 📌 CE-4685: a whole unit's Bound carried <c>PlatoonBound</c>'s own
    /// fields (<c>Objective</c>), which a stochastic unit cannot read. Field names follow Attack / Defend (<c>Position</c> = the place).
    /// </summary>
    [Order(nameof(OrderKind.Bound))]
    [StructLayout(LayoutKind.Sequential)]
    public struct BoundOrder
    {
        /// <summary>Where the bounds end — the objective (local metres).</summary>
        [JsonPropertyName("Position")]
        public Fdp.Toolkit.Replication.PointLink Position;

        /// <summary>Where the covering element sets first (local metres); unset = the play's own choice (a platoon: the objective).</summary>
        [JsonPropertyName("SupportPoint")]
        public Fdp.Toolkit.Replication.PointLink SupportPoint;

        /// <summary>The asked speed (m/s); 0 = the play's default.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;
    }

    /// <summary>
    /// ⭐ agg-behaviors R-8300 — the BREAK CONTACT order: withdraw (to <see cref="To"/>, else to a point the enemy cannot see). The neutral
    /// DTO of the kind; <c>BreakContact</c> accepts it strictly.
    /// </summary>
    [Order(nameof(OrderKind.BreakContact))]
    [StructLayout(LayoutKind.Sequential)]
    public struct BreakContactOrder
    {
        /// <summary>Where to fall back to (local metres); zero = ask the terrain for a point the enemy cannot see.</summary>
        [JsonPropertyName("To")]
        public Fdp.Toolkit.Replication.PointLink To;

        /// <summary>The asked speed (m/s); 0 = the play's default.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;
    }

    /// <summary>⭐ <c>CE-2173</c> slice 6c (W1, R-2020) — the reason a point is struck (Air Operations §7b, §9e).</summary>
    public enum StrikeTargetKind : byte
    {
        Unspecified = 0,
        Personnel = 1,
        Vehicles = 2,
        Building = 3,
        Fortification = 4,
        Bridge = 5,
        Runway = 6,
    }

    /// <summary>
    /// ⭐ <c>CE-2176</c> IF-3 (IF-E, FM-6; 📄 docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §3.5, §3.8) — the OBSERVE FIRE order: a
    /// soldier who sees <see cref="Location"/> calls <see cref="Battery"/>'s fire on it, adjusts one gun's rounds onto it from what he sees
    /// burst, then calls fire for effect with the mission below. Every call is an ordinary <see cref="StrikeOrder"/> to the battery, so it
    /// crosses brain nodes the way any order does.
    /// </summary>
    [Order(nameof(OrderKind.ObserveFire))]
    [StructLayout(LayoutKind.Sequential)]
    public struct ObserveFireOrder
    {
        /// <summary>The target (geodetic). Required.</summary>
        [Required]
        [JsonPropertyName("Location")]
        public Fdp.Toolkit.Replication.PointLink Location;

        /// <summary>An entity at the place to track (optional).</summary>
        [JsonPropertyName("Target")]
        public Fdp.Toolkit.Replication.EntityLink Target;

        /// <summary>The battery (or self-propelled gun) to call. Required.</summary>
        [Required]
        [JsonPropertyName("Battery")]
        public Fdp.Toolkit.Replication.EntityLink Battery;

        /// <summary>How the observer located the target: the first round's error (map / eye ≈ 150 m, laser ≈ 10 m — IF-F).</summary>
        [JsonPropertyName("Locating")]
        public FireMissionLocating Locating;

        /// <summary>Fire for effect: rounds per gun; 0 = 3.</summary>
        [JsonPropertyName("Rounds")]
        public int Rounds;

        /// <summary>Fire for effect: the sheaf.</summary>
        [JsonPropertyName("Sheaf")]
        public FireMissionSheaf Sheaf;

        /// <summary>Fire for effect: an open sheaf's width (m).</summary>
        [JsonPropertyName("SheafWidthM")]
        public float SheafWidthM;

        /// <summary>Fire for effect: when the guns fire.</summary>
        [JsonPropertyName("Control")]
        public FireMissionControl Control;

        /// <summary>Fire although friends are within 600 m of the point (FM-5).</summary>
        [JsonPropertyName("DangerClose")]
        public bool DangerClose;
    }

    /// <summary>⭐ <c>CE-2176</c> IF-3 — how the observer located the target (IF-F).</summary>
    public enum FireMissionLocating : byte { Map = 0, Laser = 1 }

    /// <summary>⭐ <c>CE-2176</c> IF-2 — how a fire mission starts (IF-D).</summary>
    public enum FireMissionMethod : byte { FireForEffect = 0, Adjust = 1 }

    /// <summary>⭐ <c>CE-2176</c> IF-2 — where the guns aim (FM-3).</summary>
    public enum FireMissionSheaf : byte { Converged = 0, Parallel = 1, Open = 2 }

    /// <summary>⭐ <c>CE-2176</c> IF-2 — when the guns fire (FM-4).</summary>
    public enum FireMissionControl : byte { WhenReady = 0, TimeOnTarget = 1 }
}
