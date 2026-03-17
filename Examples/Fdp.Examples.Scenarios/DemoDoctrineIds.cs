namespace Fdp.Examples.Scenarios
{
    /// <summary>
    /// Stable compile-time integer IDs for doctrines used in demo scenarios.
    /// Must not overlap with <see cref="FDP.Toolkit.Behavior.DoctrineIds"/>.
    /// Uses the upper end of the military range (2001-2999) to avoid conflicts.
    /// </summary>
    public static class DemoDoctrineIds
    {
        /// <summary>
        /// Combat BTree doctrine used by <c>BehaviorValidationScenario</c>.
        /// Selector: Sequence(Condition_ThreatVisible, Condition_HasAmmo, Action_AimAndFire)
        /// or fallback Action_Flee.
        /// </summary>
        public const int Combat = 2900;
    }
}
