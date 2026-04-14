namespace Hrot.CGF.Configuration
{
    /// <summary>
    /// Stable compile-time integer constants for the CGF Brain-tier doctrines.
    ///
    /// These are passed to <see cref="Fdp.Toolkit.Behavior.DoctrineRegistry.Register"/> at
    /// application startup and stored in
    /// <see cref="Fdp.Toolkit.Behavior.Components.DoctrineState.ActiveDoctrineHash"/>.
    ///
    /// <b>Range:</b> 3001-3099 (navigation and wander behaviours).
    /// Values must be globally unique and must never change once published.
    ///
    /// See <see cref="Fdp.Toolkit.Behavior.DoctrineIds"/> for framework-level doctrine IDs.
    /// </summary>
    public static class CgfDoctrineIds
    {
        // ── Navigation behaviours (BTree, 3001-3009) ──────────────────────────

        /// <summary>
        /// Move to a fixed 2-D location (BTree).
        /// BehaviorId string: <c>"MoveToLocation"</c>.
        /// Params: <c>MoveToLocationParams { X, Y, Speed, ArrivalRadius }</c>.
        /// </summary>
        public const int MoveTo_BT = 3001;

        /// <summary>
        /// Follow a pre-computed waypoint route (BTree).
        /// BehaviorId string: <c>"FollowRoute"</c>.
        /// Params: <c>FollowRouteParams { Waypoints[], Speed, Loop }</c>.
        /// </summary>
        public const int FollowRoute_BT = 3002;

        /// <summary>
        /// Join an existing formation led by another entity (BTree).
        /// BehaviorId string: <c>"JoinFormation"</c>.
        /// Params: <c>JoinFormationParams { LeaderNetworkId, FormationType }</c>.
        /// </summary>
        public const int JoinFormation_BT = 3003;

        // ── Idle behaviour (HSM, 3010-3019) ───────────────────────────────────

        /// <summary>
        /// Idle / stand-still behaviour (HSM).
        /// BehaviorId string: <c>"Idle"</c>.
        /// No configurable parameters.
        /// </summary>
        public const int Idle_HSM = 3010;

        // ── Wander behaviour (BTree, 3011-3019) ───────────────────────────────

        /// <summary>
        /// Military wander behaviour (BTree).
        /// BehaviorId string: <c>"WanderMilitary"</c>.
        /// The entity continuously moves to random destinations within 1000 units of (0, 0).
        /// Once a destination is reached a new random destination is selected automatically.
        /// No configurable parameters (params block is ignored).
        /// </summary>
        public const int WanderMilitary_BT = 3011;
    }
}
