namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// Single source of truth for behavior identity names. Referenced instead of raw
    /// string literals so a typo is a compile error, not a silent <c>BehaviorHash.FromName</c>
    /// mismatch.
    /// </summary>
    public static class BehaviorNames
    {
        public const string MoveToLocation = "MoveToLocation";
        public const string FollowRoute = "FollowRoute";
        public const string JoinFormation = "JoinFormation";
        public const string Idle = "Idle";
        public const string WanderMilitary = "WanderMilitary";
        public const string FireAtTarget = "FireAtTarget";
        public const string HullDownAttackRun = "HullDownAttackRun";
        /// <summary>⭐ CE-2132 — the danger-crossing tree (its blueprint twin is DangerCrossingBp); takes <c>DangerCrossingParams</c>.</summary>
        public const string DangerCrossing = "DangerCrossing";
        public const string PlatoonHillAttack = "PlatoonHillAttack";
        public const string InfantryCombat = "InfantryCombat";
        public const string Ambush = "Ambush";
        public const string ConvoyEscort = "ConvoyEscort";
        public const string DefendArea = "DefendArea";
        /// <summary>⭐ CE-472 — the tactical intent a commander sends a tank; mapped to <see cref="HullDownAttackRun"/>.</summary>
        public const string HullDownAttack = "HullDownAttack";
        /// <summary>⭐ Buildings 5d-2 — walk to a door, unlock it, open it, walk through (the curated door test behaviour, R-223).</summary>
        public const string DoorLocksmith = "DoorLocksmith";
        /// <summary>⭐ Buildings Stage 6 (<c>CE-1032</c>, W-8) — fire a mount at a ground point (a thrown grenade, a mortar).</summary>
        public const string FireAtPoint = "FireAtPoint";
        /// <summary>⭐ WL-4 L-8 — keep the gun on the top threat without firing (DESIGN_Weapon_Layers §5f).</summary>
        public const string TrackTarget = "TrackTarget";
        /// <summary>⭐ <c>CE-1032</c> — a TEST behaviour: hold a posture (stand, crouch, lie) and do nothing else — a target for the warhead demos.</summary>
        public const string HoldStance = "HoldStance";
        /// <summary>⭐ <c>CE-3136</c> P-8 (D10) — the window duel: <c>PeekAndFire</c> with a shape — <c>window</c> (A: stance peeks, rotates
        /// windows) or <c>street</c> (B: step peeks round a cover, suppresses and bounds). 📄 docs/DESIGN_Peek_And_Fire.md §8.4.</summary>
        public const string WindowDuel = "WindowDuel";

        /// <summary>⭐ CE-3160 (R-266) — the library's combat posture, a C#-built tree (DESIGN_Decision_Layer §3.3h).</summary>
        public const string CombatPosture = "CombatPosture";

        /// <summary>⭐ CE-3160 / CE-507 D2 (a) (R-264) — a squad commander's bounding overwatch (DESIGN_Squad_Wiring §6).</summary>
        public const string BoundingOverwatch = "BoundingOverwatch";

        /// <summary>⭐ CE-2123 — a squad commander's suppress-and-manoeuvre: base of fire + flank + assault through (DESIGN_Squad_Tactics §3).</summary>
        public const string SuppressAndManeuver = "SuppressAndManeuver";

        /// <summary>⭐ CE-2123 — go to a point firing at the top threat, no posture decision: a superior's MOVE order (DESIGN_Squad_Tactics §3.6).</summary>
        public const string AdvanceTo = "AdvanceTo";

        /// <summary>⭐ <c>CE-2123</c> S2 — the squad leader's reaction to contact: hold and return fire, decide once, run a drill.</summary>
        public const string ReactToContact = "ReactToContact";

        /// <summary>⭐ <c>CE-2123</c> S2 — the squad gets out of the enemy's sight, element covering element.</summary>
        public const string BreakContact = "BreakContact";

        /// <summary>⭐ <c>CE-2123</c> S2 — the squad holds where it stands and fights.</summary>
        public const string SquadHold = "SquadHold";

        /// <summary>⭐ <c>CE-2275</c> D-1 — the squad defends a position: posts per man, his arc toward the squad's sector, displacing under fire.</summary>
        public const string SquadDefend = "SquadDefend";
        /// <summary>⭐ <c>CE-2257</c> BC-1 — a squad clears one room (Battle Drill 6).</summary>
        public const string ClearRoom = "ClearRoom";
        /// <summary>⭐ <c>CE-2259</c> BC-3 (CB-57) — a squad clears a building room by room (Building Clearing §7.2).</summary>
        public const string ClearBuilding = "ClearBuilding";
        /// <summary>⭐ <c>CE-2257</c> BC-1 (CB-53) — a man opens a door, and breaches it when it is locked.</summary>
        public const string BreachDoor = "BreachDoor";

        /// <summary>⭐ <c>CE-2123</c> S3 — the squad marches to a point in File / Wedge / Line, falling back to file on the lead's trail.</summary>
        public const string SquadMarch = "SquadMarch";

        /// <summary>⭐ <c>CE-2294</c> CC-D (Command Chain §1, §6) — a platoon's task: its support squad moves to the support point; once
        /// that squad reports SET, its assault squad moves to the objective; done when the assault squad reports SET. An HSM.</summary>
        public const string PlatoonBound = "PlatoonBound";

        /// <summary>⭐ <c>CE-2294</c> CC-D — a platoon's reaction to a reported enemy element: every squad holds, until no such element is
        /// fresh in the platoon's picture (then the paused task resumes where it was).</summary>
        public const string PlatoonHold = "PlatoonHold";

        /// <summary>⭐ <c>CE-2276</c> E-1 (DA-E) — a platoon's task: each squad defends its point, the line squads each watch a slice of
        /// the engagement area and hold fire until an enemy is reported inside it. An HSM.</summary>
        public const string PlatoonDefend = "PlatoonDefend";

        /// <summary>⭐ <c>CE-2141</c> (D2) — an infantry squad fulfils the MOVE order: it marches there (Wedge, or File on a road).</summary>
        public const string InfantrySquadMove = "InfantrySquadMove";

        /// <summary>⭐ <c>CE-2141</c> (D2) — an infantry squad fulfils the HOLD order: it holds where it stands and fights, until told otherwise.</summary>
        public const string InfantrySquadHold = "InfantrySquadHold";

        /// <summary>⭐ <c>CE-2141</c> (D2) — an infantry squad fulfils the ATTACK order: suppress and manoeuvre on the named target.</summary>
        public const string InfantrySquadAttack = "InfantrySquadAttack";

        // ⭐ CE-2143 (D4-B) — the broad classes' order behaviours, so every entity the menu ordered before keeps its orders:
        //   Person (DIS Kind 3) and Platform (Kind 1), each over the behaviour the sender used to name directly.
        public const string PersonMove = "PersonMove";
        public const string PersonAttack = "PersonAttack";
        public const string PersonHold = "PersonHold";
        public const string PlatformMove = "PlatformMove";
        public const string PlatformAttack = "PlatformAttack";
        public const string PlatformHold = "PlatformHold";

        /// <summary>⭐ <c>CE-2165</c> V4 — a LAND vehicle (DIS 1.1) fulfils the ATTACK order: it fights from cover — hide, expose, fire,
        /// withdraw (DESIGN_Vehicle_Exposure_Cycle §4). More specific than <see cref="PlatformAttack"/>.</summary>
        public const string LandPlatformAttack = "LandPlatformAttack";

        /// <summary>⭐ <c>CE-2165</c> V4 — the library vehicle behaviour: the exposure cycle against the top threat.</summary>
        public const string VehicleFightFromCover = "VehicleFightFromCover";

        /// <summary>⭐ <c>CE-2165</c> V8 — a vehicle with a drone about: under overhead cover, else keep moving; fire at it when seen.</summary>
        public const string ShelterFromAir = "ShelterFromAir";

        /// <summary>⭐ <c>CE-2191</c> (Air Ops §7f) — a unit with an air-defence weapon stands and fires at the aircraft it sees.</summary>
        public const string EngageAircraft = "EngageAircraft";

        /// <summary>⭐ <c>CE-1056</c> I-2d — a soldier walks to a vehicle and boards it (DESIGN_Entity_Interactions §7.3).</summary>
        public const string Embark = "Embark";
        /// <summary>⭐ <c>CE-2176</c> (Weapon Systems §4b) — walk to a crew weapon and take its Gunner seat.</summary>
        public const string ManWeapon = "ManWeapon";
        /// <summary>⭐ WS-6 (Weapon Systems §10) — a soldier sets a weapon system up: puts it down if he carries it, mans it, deploys it.</summary>
        public const string SetUpWeapon = "SetUpWeapon";
        /// <summary>⭐ WS-6 (Weapon Systems §10) — a soldier packs his weapon system up (Stowed) and steps off it.</summary>
        public const string PackUpWeapon = "PackUpWeapon";
        /// <summary>⭐ WS-6 (Weapon Systems §10) — a soldier picks a portable weapon system up and carries it (one-man; two-man is T-c, later).</summary>
        public const string CarryWeapon = "CarryWeapon";
        /// <summary>⭐ WS-6 (Weapon Systems §10, R-278 <c>&lt;Class&gt;&lt;Order&gt;</c>) — a weapon system's ATTACK: it fights the named
        /// target from where it stands (it cannot move) until the target is down.</summary>
        public const string WeaponSystemAttack = "WeaponSystemAttack";
        /// <summary>⭐ WS-6 (Weapon Systems §10, W-E) — a weapon system's HOLD: it stays and fires at whatever threat comes (an automatic
        /// turret's standing behaviour) — not <c>Idle</c>.</summary>
        public const string WeaponSystemHold = "WeaponSystemHold";

        /// <summary>⭐ <c>CE-1056</c> I-2d — a passenger leaves its vehicle, beside the hull (DESIGN_Entity_Interactions §7.3).</summary>
        public const string Disembark = "Disembark";

        /// <summary>⭐ <c>CE-2152</c> C-b — a soldier walks to a man who is down, drags (or carries) him to a point and puts him down
        /// (DESIGN_Entity_Interactions §7.4.10).</summary>
        public const string CarryCasualty = "CarryCasualty";

        /// <summary>⭐ <c>CE-2152</c> C-c (R-b) — hold and put rounds on a named contact, seen or not (DESIGN_Squad_Tactics §7.5).</summary>
        public const string Suppress = "Suppress";

        /// <summary>⭐ <c>CE-2152</c> C-c (R-c) — a commander's casualty care: the squad covers, the medic brings a man who is down to
        /// cover (DESIGN_Squad_Tactics §7.5).</summary>
        public const string CasualtyCare = "CasualtyCare";
        /// <summary>⭐ CE-2173 slice 3c — the squad brings in a man who is down (any man, a downed pilot too) and keeps him carried.</summary>
        public const string SquadRecover = "SquadRecover";

        /// <summary>⭐ <c>CE-2158</c> F-b — a commander closes on an objective in contact by halves: one suppresses, the other rushes to
        /// cover, then they swap (DESIGN_Squad_Tactics §7.6).</summary>
        public const string FireAndMovement = "FireAndMovement";

        /// <summary>⭐ <c>CE-2159</c> X-b — a commander crosses a danger area: security watches the near side, the rest cross one pair
        /// at a time, watch from the far side, then security follows (DESIGN_Squad_Tactics §7.7).</summary>
        public const string SquadDangerCrossing = "SquadDangerCrossing";

        /// <summary>⭐ <c>CE-1056</c> I-2d — the squad gets out of its vehicles; done when nobody is aboard (DESIGN_Squad_Tactics §5a).</summary>
        public const string SquadDismount = "SquadDismount";

        /// <summary>⭐ <c>CE-2173</c> slice 3 (B10, R-2015) — the squad boards one vehicle; done when every live member is aboard it
        /// (docs/designs/air-attack/DESIGN_Air_Operations.md §9).</summary>
        public const string SquadEmbark = "SquadEmbark";

        /// <summary>⭐ <c>CE-2173</c> slice 3 (B8) — the squad RIDES: every live member is aboard a vehicle; it does nothing, keeps the task
        /// paused, and ends the moment somebody is out (Air Operations §9b).</summary>
        public const string SquadRide = "SquadRide";

        /// <summary>⭐ <c>CE-2173</c> slice 3 — a soldier carrying a casualty seats him in a vehicle, then boards it himself (Air Operations §9).</summary>
        public const string LoadCasualty = "LoadCasualty";

        /// <summary>⭐ <c>CE-2173</c> (B4, R-278) — MOVE for any aircraft: fly to the point at cruise height above the ground (Air Operations §7).</summary>
        public const string AirPlatformMove = "AirPlatformMove";

        /// <summary>⭐ <c>CE-2173</c> slice 5 (B4, R-278) — STRIKE for any aircraft: the one air attack on the order's point, the profile
        /// picked from the aircraft and its weapons (Air Operations §7, §9c).</summary>
        public const string AirPlatformStrike = "AirPlatformStrike";

        /// <summary>⭐ <c>CE-2173</c> slice 6c (B12–B14, R-278, R-2020) — ATTACK for any aircraft: the one air attack on the order's ENTITY —
        /// direct fire first, led at the moving target, passes until it is destroyed (Air Operations §9e).</summary>
        public const string AirPlatformAttack = "AirPlatformAttack";

        /// <summary>⭐ <c>CE-2176</c> IF-2 (FM-1, R-278) — STRIKE for an artillery battery: the virtual battery commander's fire mission —
        /// every gun's aim point (the sheaf), its rounds and its fire time (time on target) (Indirect Fire §3.4).</summary>
        public const string ArtilleryUnitStrike = "ArtilleryUnitStrike";

        /// <summary>⭐ <c>CE-2176</c> IF-2 (FM-1, R-278) — STRIKE for one self-propelled gun: its rounds on the point (Indirect Fire §4).</summary>
        public const string SelfPropelledArtilleryStrike = "SelfPropelledArtilleryStrike";
        /// <summary>⭐ <c>CE-2176</c> IF-3 — a soldier observes and adjusts a battery's fire (ObserveFire).</summary>
        public const string PersonObserveFire = "PersonObserveFire";

        /// <summary>⭐ CE-3172 (R-270, M-7) — what a POSSESSED unit runs: the station's controls drive its body
        /// (DESIGN_Entity_Control §8b). Assigned by <c>PossessionSystem</c> at origin Possession only.</summary>
        public const string OperatorControl = "OperatorControl";

        /// <summary>⭐ CE-1118 H-1 (Airborne Insertion §6) — the squad goes down its hovering helicopter's ropes, one man per rope at a time,
        /// and takes a 360° ring round the rope point (on a roof, inside its outline).</summary>
        public const string SquadFastRope = "SquadFastRope";

        /// <summary>⭐ CE-1118 H-1 (§6) — the jumpmaster: the stick leaves one per second at each man's release point along the Drop leg,
        /// each with his landing point; then the squad assembles.</summary>
        public const string SquadAirborneExit = "SquadAirborneExit";

        /// <summary>⭐ CE-1118 H-1 (§6, D-22) — the squad rallies in a ring round a point once every man is down.</summary>
        public const string AssembleAfterDrop = "AssembleAfterDrop";

        /// <summary>⭐ CE-1118 H-3 (§6, D-24) — an aircraft's crew ejects (interaction action 11); Success once the aircraft is down.</summary>
        public const string Eject = "Eject";

        /// <summary>⭐ CE-1118 H-3 (§6, D-25) — a downed pilot, once out of his harness, gets away from the nearest known enemy into cover,
        /// goes prone and stays down.</summary>
        public const string EvadeAndSignal = "EvadeAndSignal";

        /// <summary>⭐ CE-2235 (civilians CIV-1, CV-3; DESIGN_Civilians_And_Crowd_Control §5d) — a civilian's one behaviour: his routine,
        /// alarm and casualty (slice 1), later panic, shelter, protest and riot — modes of one HSM, never types.</summary>
        public const string CivilianHsm = "CivilianHsm";
        /// <summary>⭐ <c>CE-2322</c> (AQ97 D, R-2062) — a civilian's fear reaction on ANY task: the alarm / panic / shelter / cower / casualty
        /// part of <see cref="CivilianHsm"/>, run by his SOP (<c>CivilianSop</c>) when his task is something else. Ends when he calms.</summary>
        public const string CivilianReact = "CivilianReact";
        /// <summary>⭐ <c>CE-2241</c> ① (AW-5, AW-21; docs/DESIGN_Civilians_And_Crowd_Control.md §5h.5) — lay every weapon down and raise the hands; after
        /// <c>Seconds</c> (0 = for good) take them up again and finish (the feint).</summary>
        public const string Surrender = "Surrender";
        /// <summary>⭐ <c>CE-2239</c> part 2 (CV-11, FL-5; docs/DESIGN_Civilians_And_Crowd_Control.md §5j) — a policeman holds his post in
        /// the line, fires at the nearest Hostile within <c>Reach</c> (his ROE's force level picks the weapon) and strikes with the baton
        /// within arm's reach. The security types run it by default.</summary>
        public const string HoldTheLine = "HoldTheLine";
        /// <summary>⭐ <c>CE-2238</c> part 4 (AW-11, AW-16; docs/DESIGN_Civilians_And_Crowd_Control.md §5h.7 CO-2) — put the carried weapon in a
        /// carry station: Hands (draw), Back (sling) or Hidden (conceal); hold still <c>Seconds</c>, then finish.</summary>
        public const string StationWeapon = "StationWeapon";
        /// <summary>⭐ <c>CE-2238</c> part 5 (AW-12, §5l BM-1) — he sets off the charge he carries.</summary>
        public const string DetonateCharge = "DetonateCharge";

        /// <summary>⭐ <c>CE-2360</c> (AQ102 I) — the grenade reflex <c>GrenadeDangerSystem</c> starts as a reaction: run clear, drop flat, or
        /// throw it back.</summary>
        public const string EvadeGrenade = "EvadeGrenade";
        /// <summary>⭐ <c>CE-2241</c> ② (CV-14, §5m PV-4) — a police order: go at the point and strike the protesters within reach of it.</summary>
        public const string BatonCharge = "BatonCharge";
        /// <summary>⭐ <c>CE-2306</c> (DA-93, Defend &amp; Attack §8e) — the trigger man sets off a placed charge by phone when a hostile vehicle he
        /// sees is in its kill zone.</summary>
        public const string TriggerIed = "TriggerIed";
        /// <summary>⭐ <c>CE-2307</c> (CH-1, Squad Tactics §5b) — a carrier hit while it drives with troops aboard keeps going, out of the
        /// kill zone.</summary>
        public const string PushThrough = "PushThrough";
        /// <summary>⭐ <c>CE-2308</c> (SP-1, Indirect Fire §3.11) — an observer who picks his own target and calls fire on it, mission after
        /// mission.</summary>
        public const string SpotForFire = "SpotForFire";
        /// <summary>⭐ <c>CE-2311</c> (HQ-9, Command Chain §8d) — an aircraft searches an area for targets and attacks what it finds.</summary>
        public const string AirArmedSearch = "AirArmedSearch";
        /// <summary>⭐ <c>CE-2312</c> (HQ-6, Command Chain §8d) — the headquarters answers FireSupport and Reinforce with its assets.</summary>
        public const string SupportCommand = "SupportCommand";
        /// <summary>⭐ <c>CE-2277</c> DA-F (Defend &amp; Attack §8f) — the platoon attacks an AREA: base of fire, approach, assault, objective by
        /// objective, then consolidates.</summary>
        public const string PlatoonAttack = "PlatoonAttack";
        /// <summary>⭐ <c>CE-2347</c> (R-2069, Defend &amp; Attack §11a) — the platoon rides a route: the squads in their vehicles, a halt on
        /// contact, secure when it is quiet, remount and carry on.</summary>
        public const string PlatoonConvoy = "PlatoonConvoy";
        /// <summary>⭐ <c>CE-2284</c> (DA-17) — a platoon moves through town on foot along legs (Defend and Attack §11b).</summary>
        public const string UrbanMovement = "UrbanMovement";
        /// <summary>⭐ <c>CE-2284</c> U-2 (UM-12) — the platoon SOP's answer to fire from a building on the move: the attack play, preset 2.</summary>
        public const string ReactToFireFromBuilding = "ReactToFireFromBuilding";
        /// <summary>⭐ <c>CE-2331</c> (AQ98 A/B, R-2070) — the soldier's cover REACTION: fights from cover (<c>PeekAndFire</c>) and ends once he is
        /// in cover with no hit or near miss on him for a quiet lapse, so his task resumes.</summary>
        public const string FightFromCover = "FightFromCover";
    }
}
