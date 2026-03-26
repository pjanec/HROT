namespace Fdp.Examples.Scenarios
{
    /// <summary>
    /// Stable compile-time integer IDs for doctrines used specifically in
    /// <c>BehaviorValidationScenario</c>.  Uses the upper military range (2001-2999)
    /// to avoid conflicts with <see cref="FDP.Toolkit.Behavior.DoctrineIds"/> and
    /// <see cref="Fdp.Examples.Common.Constants.DemoDoctrineIds"/>.
    /// </summary>
    public static class BehaviorValidationDoctrineIds
    {
        /// <summary>
        /// Combat BTree doctrine used by <c>BehaviorValidationScenario</c>.
        /// Selector: Sequence(Condition_ThreatVisible, Condition_HasAmmo, Action_AimAndFire)
        /// or fallback Action_Flee.
        /// </summary>
        public const int Combat = 2900;
    }
}
