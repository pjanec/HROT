namespace Hrot.Map.Definitions.Doctrine
{
    /// <summary>
    /// Compile-time integer IDs for all doctrine parameter DTOs defined in this assembly.
    /// Values mirror <c>Hrot.CGF.Configuration.CgfDoctrineIds</c> and must stay in sync.
    /// Range 3001-3099 is reserved for CGF doctrines.
    /// </summary>
    internal static class DoctrineIds
    {
        // Navigation BTree (3001-3009)
        public const int MoveTo_BT         = 3001;
        public const int FollowRoute_BT    = 3002;
        public const int JoinFormation_BT  = 3003;

        // Idle / Wander (3010-3011)
        public const int Idle_HSM          = 3010;
        public const int WanderMilitary_BT = 3011;

        // Combat BTree (3012-3019)
        public const int FireAtTarget_BT   = 3012;
        public const int ConvoyEscort_BT   = 3013;
        public const int InfantryCombat_BT = 3014;
        public const int Ambush_BT         = 3015;
    }
}
