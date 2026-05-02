namespace Hrot.Map.Definitions
{
    /// <summary>
    /// Project-wide ECS component ID registry for all Hrot-specific components.
    /// FDP + toolkit IDs (0–159) remain in <c>Fdp.Core.GlobalComponentIds</c>.
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

        /// <summary><c>ActiveMissionPlan</c> — domain POCO mission plan (replaces EntityMissionHolder).</summary>
        public const byte ActiveMissionPlan = 162;

        /// <summary><c>IgEntityData</c> — IG-internal entity metadata from EntityInfo.</summary>
        public const byte IgEntityData        = 164;

        /// <summary><c>IgHealthState</c> — IG-internal health state derived from EntityDamage.</summary>
        public const byte IgHealthState       = 165;

        /// <summary>
        /// <c>ActivePerspective</c> — managed singleton component selecting the active presentation
        /// view.  Used by <c>SimMapRenderSystem</c> and the perspective coordinator in the cluster
        /// runner to gate rendering per active perspective tier (MOD1-P4T2).
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


        // IDs 163 are reserved (see note below).
        // NOTE: InFormationTag (formerly 163) has been moved to FDP.Toolkit.Navigation
        //       (GlobalComponentIds.InFormationTag = 70) as part of CT-MOD1-I executor migration.

        // ── Zone authoring components (PACK3 / A011) ────────────────────────

        /// <summary><c>ZoneMembership</c> — managed component recording the zone name for an obstacle entity created by <c>SpawnZoneObstacleCommand</c>.</summary>
        public const byte ZoneMembership = 171;


        // ── Genesis Intent DTO components (cgf-scn-2 / Phase 4) ──────────────
        // Transient managed components written by scenario translators on Inject.
        // Resolved to structural components by GenesisMaterializationSystem.

        /// <summary><c>InitialPassengersIntent</c> — stores passenger Network IDs at scenario load; resolved to <c>PassengerBuffer</c>.</summary>
        public const byte InitialPassengersIntent = 177;

        /// <summary><c>InitialVehicleIntent</c> — stores vehicle Network ID at scenario load; resolved to <c>IsEmbarkedTag</c>.</summary>
        public const byte InitialVehicleIntent    = 178;

        /// <summary><c>InitialHierarchyIntent</c> — stores parent/first-child/next-sibling Network IDs; resolved to <c>VisHierarchyNode</c>.</summary>
        public const byte InitialHierarchyIntent  = 179;

        /// <summary><c>InitialRouteIntent</c> — stores personal-route Network ID; resolved to <c>PersonalRouteRef</c>.</summary>
        public const byte InitialRouteIntent      = 180;

        /// <summary><c>InitialTargetsIntent</c> — stores target-memory Network IDs + sensor data; resolved to <c>TargetMemory</c>.</summary>
        public const byte InitialTargetsIntent    = 181;


        // ── Commander-Subordinate hierarchy components (commander-subordinates workstream) ──

        /// <summary><c>UnitRoster</c> — fixed-capacity subordinate list on the commanding entity (AI tier); NoSave (derived from UnitSubordinate records).</summary>
        public const byte UnitRoster = 182;

        /// <summary><c>UnitSubordinate</c> — generation-safe commander reference and tactical designation on subordinate entities (AI tier).</summary>
        public const byte UnitSubordinate = 183;

        /// <summary><c>InitialUnitSubordinateIntent</c> — genesis intent DTO storing network commander ID at scenario load; resolved to <c>UnitSubordinate</c> by <c>GenesisMaterializationSystem</c> (Phase 4).</summary>
        public const byte InitialUnitSubordinateIntent = 184;
    }
}
