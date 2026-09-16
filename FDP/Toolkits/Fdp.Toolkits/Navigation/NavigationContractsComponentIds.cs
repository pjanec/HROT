namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// ECS component ID catalog for <c>FDP.Toolkit.Navigation.Contracts</c>.
    /// IDs 67–68 were previously defined in <c>Fdp.Core.GlobalComponentIds</c> and
    /// have been moved here as part of DB-MOD1-23.  The numeric values are unchanged to
    /// preserve ECS registry compatibility.
    /// </summary>
    public static class NavigationContractsComponentIds
    {
        // IDs 67–68: formerly in GlobalComponentIds, moved here (DB-MOD1-23).
        // The 20–49 toolkit block is full; these IDs remain in the 50–79 range
        // where they were originally allocated to avoid circular assembly dependencies.

        /// <summary><c>NavigationIntent</c> — CQRS command component carrying the Brain's navigation order.</summary>
        public const int NavigationIntent = 67;

        /// <summary><c>NavigationStatus</c> — CQRS status component carrying the Muscle's navigation result.</summary>
        public const int NavigationStatus = 68;

        // IDs 257-261: Nav subsystem v2 components (NAV-P0-T5).
        // IDs 69-73 cannot be used — they are occupied by FrustrationTicks, InFormationTag,
        // Faction, PerceptionReceptor, and TargetMemory in GlobalComponentIds.
        // The 257-261 block is in the extended 256-511 range (ecs-512-comps).

        /// <summary><c>NavAgentProfile</c> — locomotion profile used by the nav solver.</summary>
        public const int NavAgentProfile                = 257;

        /// <summary><c>NavigationCorridorMuscle</c> — Muscle-owned active corridor runtime state.</summary>
        public const int NavigationCorridorMuscle       = 258;

        /// <summary><c>NavigationCorridorPreview</c> — Brain-readable look-ahead of first 8 corridor waypoints.</summary>
        public const int NavigationCorridorPreview      = 259;

        /// <summary><c>NavigationPathDetailsBuffer</c> — full waypoint snapshot fetched from the path registry.</summary>
        public const int NavigationPathDetailsBuffer    = 260;

        /// <summary><c>CrowdAgent</c> — tag component opting the entity into Detour crowd avoidance.</summary>
        public const int CrowdAgent                     = 261;

        // IDs 262-264 are occupied by GlobalComponentIds (DangerAreaSensor, DangerAreaCognitiveBuffer,
        // MovementModeIntent). CrowdMotorIntent is placed at 265, continuing the navigation block.
        //
        // ST-PORT: 265 was NOT free, contrary to the port design's "coord ids stop at 264". NavFakeIds
        // declares its block as 262-279 and RESERVED 265 for FakeVolumetricState -- a constant with no
        // component attached to it (measured: the declaration is its only reference). The reservation
        // has been moved to 269 so this id has exactly one claimant. See NavFakeIds for the other half.

        /// <summary>
        /// <c>CrowdMotorIntent</c> — engine-agnostic steering output written by
        /// <c>CrowdAgentUpdateSystem</c> (P2-T4) and read by <c>BulletCharacterMotor</c>
        /// (P1-T3, design §5.3).
        /// </summary>
        public const int CrowdMotorIntent               = 265;
    }
}
