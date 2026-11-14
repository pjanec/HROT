namespace Hrot.Common.Scenario
{
    /// <summary>
    /// Stable string identifiers for HROT subsystem serialization contracts.
    /// Used in the ScenarioHeader to ensure load handlers only process matching data.
    /// </summary>
    public static class HrotSubsystemTypes
    {
        /// <summary>Cross-node, engine-agnostic scenario payload.</summary>
        public const string Scenario = "Hrot.Scenario";

        /// <summary>SimHost-authoritative snapshot or scenario payload.</summary>
        public const string SimHost = "Hrot.SimHost";

        /// <summary>CGF-authoritative snapshot or scenario payload.</summary>
        public const string Cgf = "Hrot.CGF";

        /// <summary>Player-specific visual configuration payload.</summary>
        public const string Player = "Hrot.Player";

        /// <summary>The editor (a migration writer).</summary>
        public const string Editor = "Hrot.Editor";

        // ⭐ These are persisted IDS (scenario headers, migration writers) that happen to read like assembly names; every use goes through
        //   these constants — ⛔ never a string literal at the use site (a literal is how a rename left half the ids behind).
    }
}
