namespace FDP.Toolkit.Navigation
{
    /// <summary>
    /// ECS component ID catalog for <c>FDP.Toolkit.Navigation.Contracts</c>.
    /// IDs 67–68 were previously defined in <c>Fdp.Kernel.GlobalComponentIds</c> and
    /// have been moved here as part of DB-MOD1-23.  The numeric values are unchanged to
    /// preserve ECS registry compatibility.
    /// </summary>
    public static class NavigationContractsComponentIds
    {
        // IDs 67–68: formerly in GlobalComponentIds, moved here (DB-MOD1-23).
        // The 20–49 toolkit block is full; these IDs remain in the 50–79 range
        // where they were originally allocated to avoid circular assembly dependencies.

        /// <summary><c>NavigationIntent</c> — CQRS command component carrying the Brain's navigation order.</summary>
        public const byte NavigationIntent = 67;

        /// <summary><c>NavigationStatus</c> — CQRS status component carrying the Muscle's navigation result.</summary>
        public const byte NavigationStatus = 68;
    }
}
