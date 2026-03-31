namespace Hrot.Map.Definitions
{
    /// <summary>
    /// Project-wide ECS component ID registry for all Hrot-specific components.
    /// FDP + toolkit IDs (0–159) remain in <c>Fdp.Kernel.GlobalComponentIds</c>.
    ///
    /// <para><b>ID block allocation</b></para>
    /// <list type="table">
    ///   <item><term>160–199</term><description>Application-level: SimHost + IG application components.</description></item>
    /// </list>
    /// </summary>
    public static class HrotComponentIds
    {
        // ── Application-level descriptor components (160–199) ────────────────────

        /// <summary><c>EntityDamage</c> — DDS damage descriptor stored as an ECS component for IG rendering.</summary>
        public const byte EntityDamage        = 161;

        /// <summary><c>EntityMissionHolder</c> — managed wrapper carrying an <c>EntityMission</c> payload.</summary>
        public const byte EntityMissionHolder = 162;

        /// <summary><c>IgEntityData</c> — IG-internal entity metadata from EntityInfo.</summary>
        public const byte IgEntityData        = 164;

        /// <summary><c>IgHealthState</c> — IG-internal health state derived from EntityDamage.</summary>
        public const byte IgHealthState       = 165;

        /// <summary>
        /// <c>ActivePerspective</c> — singleton component selecting the active presentation
        /// view (IG window vs. Sim Map).  Used by <c>IgMapRenderSystem</c>,
        /// <c>SimMapRenderSystem</c>, and <c>PerspectiveCoordinatorSystem</c>
        /// to gate rendering per active perspective tier (MOD1-P4T2).
        /// </summary>
        public const byte ActivePerspective   = 166;

        /// <summary><c>IgSymbolOverride</c> — ExCon-sourced per-entity visual override (style-set, affiliation, texture; DB-MOD1-22).</summary>
        public const byte IgSymbolOverride    = 167;


        // ── Route planning components (ROUTES1) ──────────────────────────────────

        /// <summary><c>RoutePlan</c> — managed component storing the ordered waypoint list and loop flag for a route entity.</summary>
        public const byte RoutePlan            = 168;

        /// <summary><c>PersonalRouteRef</c> — blittable component on a vehicle entity providing an O(1) lookup to its personal child route entity.</summary>
        public const byte PersonalRouteRef     = 169;

        /// <summary><c>RouteTrajectoryCache</c> — blittable component caching the compiled TrajectoryPoolManager entry for a route entity. Not replicated over DDS.</summary>
        public const byte RouteTrajectoryCache = 170;


        // IDs 163 and 171–199 are reserved for future application-level components.
        // NOTE: InFormationTag (formerly 163) has been moved to FDP.Toolkit.Navigation
        //       (GlobalComponentIds.InFormationTag = 70) as part of CT-MOD1-I executor migration.
    }
}
