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

        /// <summary>⭐ CE-3172 (R-270, M-7) — what a POSSESSED unit runs: the station's controls drive its body
        /// (DESIGN_Entity_Control §8b). Assigned by <c>PossessionSystem</c> at origin Possession only.</summary>
        public const string OperatorControl = "OperatorControl";
    }
}
