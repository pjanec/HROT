namespace Fdp.Examples.Common.Events
{
    /// <summary>
    /// Event injected by scenario scripts to simulate external world-state changes
    /// (e.g. spawning an ambush, forcing a hold-fire condition) without depending on
    /// real AI or network triggers.
    /// </summary>
    public struct DemoScenarioTriggerEvent
    {
        /// <summary>
        /// Kind of trigger: 1 = ForceHoldFire, 2 = SpawnAmbush.
        /// </summary>
        public byte TriggerType;

        /// <summary>
        /// Entity index of the target affected by this trigger (entity array index, not ECS handle).
        /// </summary>
        public int TargetEntityIndex;
    }
}
