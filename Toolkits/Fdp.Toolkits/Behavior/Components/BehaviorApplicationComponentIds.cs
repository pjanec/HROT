namespace FDP.Toolkit.Behavior.Components
{
    /// <summary>
    /// Application-level ECS component IDs for FDP.Toolkit.Behavior managed components.
    /// These IDs fall in the 160-199 application-level descriptor block defined in
    /// GlobalComponentIds.cs but are declared here because the toolkit layer cannot
    /// reference project-specific ID files.
    /// </summary>
    public static class BehaviorApplicationComponentIds
    {
        /// <summary>
        /// <c>ActiveMissionPlan</c> — managed component holding the current active mission plan.
        /// ID 162 reuses the slot formerly occupied by the deleted <c>EntityMissionHolder</c>.
        /// </summary>
        public const int ActiveMissionPlan = 162;
    }
}
