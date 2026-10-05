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

        // ⛔ 4001 (AudioStimulusEvent) is RETIRED with the old hearing pipeline (CE-3062) — do not reuse.

        // 4002 was LosCheckRequestEvent — retired with the toolkit's vision chain (CE-3052). Not reused.

        // 4003 was TargetVisibleEvent — retired (CE-3052): a raw per-tick sighting with no producer left but blocked EQS
        //   cover rays, i.e. the opposite meaning. Not reused. Sightings are the visual sensor's contact list now.

        // ⛔ 4004 (TargetHeardEvent) is RETIRED with the old hearing pipeline (CE-3062) — do not reuse.

        /// <summary>Event ID for <see cref="Events.SeedTargetCommand"/>.</summary>
        public const int SeedTargetCommandId = 4101;

        /// <summary>Event ID for <see cref="Events.SensorTrackStateEvent"/>.</summary>
        public const int SensorTrackStateEventId = 4005;

        /// <summary>Event ID for <see cref="Events.SensorChangedEvent"/> (CE-3039).</summary>
        public const int SensorChangedEventId = 4006;

        /// <summary>Event ID for <see cref="Events.SoundContactEvent"/> (<c>CE-3062</c>).</summary>
        public const int SoundContactEventId = 4007;

        // ── Threat score dynamics ─────────────────────────────────────────────────

        /// <summary>
        /// Fraction of the current threat score that decays each second in
        /// <see cref="Systems.ThreatEvaluationSystem"/>.
        /// A value of 0.1 means a score of 100 drops to 90 after 1 second.
        /// </summary>
        public const float ThreatScoreDecayPerSecond = 0.1f;

        /// <summary>
        /// ⭐ CE-3046 — an entry no sensor tracks is FORGOTTEN once its score fades below this. With the 10 %/s decay that is
        /// ~44 s after a contact seen for one second, ~66 s after one seen long enough to saturate (score 500): the score
        /// already IS a function of unseen time, so no second clock is kept. 📄 docs/DESIGN_Sensors_And_Doctrine.md §5.4.
        /// </summary>
        public const float ForgetThreatScore = 0.5f;

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
