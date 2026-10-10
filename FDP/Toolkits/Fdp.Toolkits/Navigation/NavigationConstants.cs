namespace Fdp.Toolkit.Navigation
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
        // Subsumed by MoveTo+BackendForce=RoadGraph -- see NAV-P4-T2
        [System.Obsolete("Use ActionIdMoveTo with MoveToParams.BackendForce=2 instead. See NAV-P4-T2.")]
        public const ushort ActionIdFollowRoadGraph = 4;

        /// <summary>Join an existing formation led by another entity.</summary>
        public const ushort ActionIdJoinFormation   = 5;

        /// <summary>Plan a path to a destination using the nav subsystem v2 solver; returns a route handle.</summary>
        public const ushort ActionIdPlanRoute        = 6;

        /// <summary>Follow a previously planned path by route handle.</summary>
        public const ushort ActionIdFollowPath       = 7;

        /// <summary>Fetch detailed waypoint data for a route handle into <c>NavigationPathDetailsBuffer</c>.</summary>
        public const ushort ActionIdFetchPathDetails = 8;

        /// <summary>Release a route handle allocated by <see cref="ActionIdPlanRoute"/>.</summary>
        public const ushort ActionIdReleasePath      = 9;

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

        // ── Replan policy defaults ─────────────────────────────────────────────────

        /// <summary>
        /// Default maximum number of Muscle-internal replans per intent episode when
        /// <see cref="MoveToParams.MaxReplans"/> is 0 (caller did not specify a limit).
        /// </summary>
        public const byte DefaultMaxReplans = 3;

        // ── Intent Flags bits ──────────────────────────────────────────────────────

        /// <summary>Bit index in <see cref="NavigationIntent.Flags"/>: allow internal Muscle replan.</summary>
        public const byte FlagBitAllowReplan = 0;

        /// <summary>Bit index in <see cref="NavigationIntent.Flags"/>: fire auto-refresh path details on replan.</summary>
        public const byte FlagBitAutoSendPathOnReplan = 4;

        /// <summary>
        /// ⭐ CE-1035 Q0b (<c>docs/DESIGN_World_Query_Seam.md</c> WQ-H, R-252) — bit index in <see cref="NavigationIntent.Flags"/> /
        /// <see cref="MoveToParams.Flags"/>: the destination's HEIGHT IS NOT GIVEN (a 2-D intent — "go to (x, y)"). The motion side puts
        /// it on the surface nearest the mover's level (<see cref="NavigationDestination.Of"/>). Clear (the default) = the Z is real.
        /// ⛔ Never encode "on the ground" as Z = 0: on a hill, a bridge or an upper floor it names the wrong place.
        /// </summary>
        public const byte FlagBitDestinationOnSurface = 1;

        /// <summary>The <see cref="FlagBitDestinationOnSurface"/> bit as a <see cref="MoveToParams.Flags"/> value. ⚠ A flag bit, not a property
        /// on <see cref="MoveToParams"/>: a property would add a pin to every blueprint MoveTo node (the pin schema reflects properties).</summary>
        public const byte FlagDestinationOnSurface = 1 << FlagBitDestinationOnSurface;

        /// <summary>
        /// Bit index in <see cref="NavigationIntent.Flags"/>: stream the 8-waypoint
        /// corridor preview to Brain via <see cref="NavigationCorridorPreview"/>.
        /// </summary>
        public const byte FlagBitStreamCorridorPreview = 3;

        /// <summary>
        /// ⭐ <c>CE-3128</c> (R-230) — bits 5–7 of <see cref="NavigationIntent.Flags"/> / <see cref="MoveToParams.Flags"/> hold the
        /// order's <see cref="RoadUse"/>. ⚠ Packed here because <see cref="MoveToParams"/> is at the 32-byte channel limit; the
        /// flags byte already rides from the params through the intent onto the wire, so the field needs no struct or IDL change.
        /// 📄 docs/designs/navig-2/Navigation_Design_v2_0.md §5.2a D1.
        /// </summary>
        public const int FlagShiftRoadUse = 5;

        /// <summary>The mask of <see cref="FlagShiftRoadUse"/>'s three bits.</summary>
        public const byte FlagMaskRoadUse = 0b1110_0000;

        /// <summary>The <see cref="RoadUse"/> packed in <paramref name="flags"/>.</summary>
        public static RoadUse RoadUseOf(byte flags) => (RoadUse)((flags & FlagMaskRoadUse) >> FlagShiftRoadUse);

        /// <summary><paramref name="flags"/> with its <see cref="RoadUse"/> bits set to <paramref name="roadUse"/>.</summary>
        public static byte WithRoadUse(byte flags, RoadUse roadUse)
            => (byte)((flags & ~FlagMaskRoadUse) | (((byte)roadUse << FlagShiftRoadUse) & FlagMaskRoadUse));
    }
}
