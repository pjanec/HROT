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

        /// <summary>IG-specific visual configuration payload.</summary>
        public const string Ig = "Hrot.IG";
    }
}
