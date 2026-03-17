namespace Fdp.Examples.Common.Components
{
    /// <summary>
    /// ECS component attached to a "Scenario Master" entity to track phase progression
    /// and boolean latches during deterministic scenario execution.
    /// </summary>
    public struct DemoScenarioTracker
    {
        /// <summary>Current scenario phase index (0-based).</summary>
        public int CurrentPhase;

        /// <summary>Number of ticks elapsed within the current phase.</summary>
        public uint TicksInPhase;

        /// <summary>
        /// Up to 32 sequential boolean latches expressed as bit flags.
        /// Bit N is set when latch N has been triggered.
        /// </summary>
        public int LatchMask;
    }
}
