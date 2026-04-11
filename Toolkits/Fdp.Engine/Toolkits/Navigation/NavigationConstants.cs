namespace FDP.Toolkit.Navigation
{
    /// <summary>
    /// Shared constants for the Navigation toolkit.
    /// <para>
    /// <b>Action ID conventions:</b>
    /// Action IDs are written into <c>LocomotionChannel.ActiveAction</c> by BTree or HSM nodes
    /// and consumed by the corresponding executor each tick. IDs must be distinct so that
    /// executors can identify which parameters struct to read.
    /// </para>
    /// <para>
    /// <b>Frustration guard:</b>
    /// Used by <c>MoveToExecutor</c> (Phase 3 T2) to detect vehicles that are stuck.
    /// If <c>SimVelocity.Linear.Length() &lt; FrustrationSpeedThreshold</c> for more than
    /// <c>FrustrationTickThreshold</c> consecutive ticks while the destination has not been
    /// reached, the executor reports <c>Failure</c>.
    /// </para>
    /// </summary>
    public static class NavigationConstants
    {
        // ── Locomotion action IDs ─────────────────────────────────────────────────
        // Written into LocomotionChannel.ActiveAction by BTree/HSM nodes.

        /// <summary>Move to a fixed 2-D destination with a configurable arrival radius and speed.</summary>
        public const ushort ActionIdMoveTo          = 1;

        /// <summary>Flee from a threat entity, setting destination away from the threat.</summary>
        public const ushort ActionIdFlee            = 2;

        /// <summary>Follow a pre-computed trajectory from the trajectory pool.</summary>
        public const ushort ActionIdFollowRoute     = 3;

        /// <summary>Navigate along the road graph toward a specific node.</summary>
        public const ushort ActionIdFollowRoadGraph = 4;

        /// <summary>Join an existing formation led by another entity.</summary>
        public const ushort ActionIdJoinFormation   = 5;

        // ── Frustration guard ─────────────────────────────────────────────────────

        /// <summary>
        /// Number of consecutive ticks below <see cref="FrustrationSpeedThreshold"/> before
        /// <c>MoveToExecutor</c> reports Failure. At 60 Hz, 120 ticks ≈ 2 seconds.
        /// </summary>
        public const int FrustrationTickThreshold = 120;

        /// <summary>
        /// Speed threshold (m/s) below which a vehicle is considered stuck.
        /// Compared against <c>SimVelocity.Linear.Length()</c>.
        /// </summary>
        public const float FrustrationSpeedThreshold = 0.1f; // m/s

        // ── Flee executor ─────────────────────────────────────────────────────────

        /// <summary>
        /// Number of ticks between destination replans in <c>FleeExecutor</c>.
        /// At 60 Hz, 30 ticks ≈ 0.5 seconds between flee vector recalculations.
        /// </summary>
        public const int FleeReplanIntervalTicks = 30;
    }
}
