namespace Fdp.Toolkit.Perception
{
    /// <summary>
    /// Shared numeric constants for the Perception toolkit.
    /// Using named constants throughout ensures a single point of truth for all
    /// magic numbers; raw literals in production code are forbidden.
    /// </summary>
    public static class PerceptionConstants
    {
        /// <summary>Maximum number of tracked targets stored in a single <see cref="Components.TargetMemory"/>.</summary>
        public const int MaxTrackedTargets = 16;

        // ── Event IDs ────────────────────────────────────────────────────────────
        // Range 4001–4099 is reserved for FDP.Toolkit.Perception events (see DESIGN.md §4.1).

        /// <summary>Event ID for <see cref="Events.AudioStimulusEvent"/>.</summary>
        public const int AudioStimulusEventId = 4001;

        /// <summary>Event ID for <see cref="Events.LosCheckRequestEvent"/>.</summary>
        public const int LosCheckRequestEventId = 4002;

        /// <summary>Event ID for <see cref="Events.TargetVisibleEvent"/>.</summary>
        public const int TargetVisibleEventId = 4003;

        /// <summary>Event ID for <see cref="Events.TargetHeardEvent"/>.</summary>
        public const int TargetHeardEventId = 4004;

        /// <summary>Event ID for <see cref="Events.SeedTargetCommand"/>.</summary>
        public const int SeedTargetCommandId = 4101;

        /// <summary>Event ID for <see cref="Events.SensorTrackStateEvent"/>.</summary>
        public const int SensorTrackStateEventId = 4005;

        // ── Threat score dynamics ─────────────────────────────────────────────────

        /// <summary>
        /// Fraction of the current threat score that decays each second in
        /// <see cref="Systems.ThreatEvaluationSystem"/>.
        /// A value of 0.1 means a score of 100 drops to 90 after 1 second.
        /// </summary>
        public const float ThreatScoreDecayPerSecond = 0.1f;

        // ── LocalGridBuilderSystem grid dimensions ────────────────────────────────
        // These values define the module-private SpatialHashGrid owned by PerceptionModule.
        // 200×200 cells × 5 m/cell = 1 000 m × 1 000 m coverage.

        /// <summary>Number of cells along the X axis of the module-private spatial grid.</summary>
        public const int LocalGridWidth = 200;

        /// <summary>Number of cells along the Y axis of the module-private spatial grid.</summary>
        public const int LocalGridHeight = 200;

        /// <summary>Side length (metres) of each cell in the module-private spatial grid.</summary>
        public const float LocalGridCellSize = 5.0f;

        /// <summary>Maximum number of entities that can be stored in the module-private spatial grid per tick.</summary>
        public const int LocalGridMaxEntities = 50_000;
    }
}
