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
    }
}
