namespace FDP.Toolkit.Perception
{
    /// <summary>
    /// Shared numeric constants for the Perception toolkit.
    /// Using named constants throughout ensures a single point of truth for all
    /// magic numbers; raw literals in production code are forbidden.
    /// </summary>
    public static class PerceptionConstants
    {
        /// <summary>Maximum number of tracked targets stored in a single <see cref="Components.TargetMemory"/>.</summary>
        public const int MaxTrackedTargets = 4;

        // ── Event IDs ────────────────────────────────────────────────────────────
        // Range 4001–4099 is reserved for FDP.Toolkit.Perception events (see DESIGN.md §4.1).

        /// <summary>Event ID for <see cref="Events.AudioStimulusEvent"/>.</summary>
        public const int AudioStimulusEventId = 4001;

        /// <summary>Event ID for <see cref="Events.LosCheckRequestEvent"/>.</summary>
        public const int LosCheckRequestEventId = 4002;

        /// <summary>Event ID for <see cref="Events.TargetVisibleEvent"/>.</summary>
        public const int TargetVisibleEventId = 4003;

        // ── Threat score dynamics ─────────────────────────────────────────────────

        /// <summary>
        /// Fraction of the current threat score that decays each second in
        /// <see cref="Systems.ThreatEvaluationSystem"/>.
        /// A value of 0.1 means a score of 100 drops to 90 after 1 second.
        /// </summary>
        public const float ThreatScoreDecayPerSecond = 0.1f;
    }
}
