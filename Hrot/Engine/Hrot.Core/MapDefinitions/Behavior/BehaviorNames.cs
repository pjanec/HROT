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

        /// <summary>⭐ <c>CE-2123</c> S3 — the squad marches to a point in File / Wedge / Line, falling back to file on the lead's trail.</summary>
        public const string SquadMarch = "SquadMarch";

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

        /// <summary>⭐ <c>CE-1056</c> I-2d — a soldier walks to a vehicle and boards it (DESIGN_Entity_Interactions §7.3).</summary>
        public const string Embark = "Embark";

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

        /// <summary>⭐ <c>CE-2158</c> F-b — a commander closes on an objective in contact by halves: one suppresses, the other rushes to
        /// cover, then they swap (DESIGN_Squad_Tactics §7.6).</summary>
        public const string FireAndMovement = "FireAndMovement";

        /// <summary>⭐ <c>CE-1056</c> I-2d — the squad gets out of its vehicles; done when nobody is aboard (DESIGN_Squad_Tactics §5a).</summary>
        public const string SquadDismount = "SquadDismount";

        /// <summary>⭐ CE-3172 (R-270, M-7) — what a POSSESSED unit runs: the station's controls drive its body
        /// (DESIGN_Entity_Control §8b). Assigned by <c>PossessionSystem</c> at origin Possession only.</summary>
        public const string OperatorControl = "OperatorControl";
    }
}
