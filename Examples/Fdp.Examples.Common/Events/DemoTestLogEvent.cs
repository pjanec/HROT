using Fdp.Kernel;

namespace Fdp.Examples.Common.Events
{
    /// <summary>
    /// Synthetic logging event fired during scenario execution to record phase transitions
    /// and assertion checkpoints in the ECS event bus.
    /// </summary>
    public struct DemoTestLogEvent
    {
        /// <summary>Scenario identifier (matches <see cref="IScenario.ScenarioName"/>).</summary>
        public FixedString32 ScenarioName;

        /// <summary>Phase identifier at the time the event was raised.</summary>
        public int PhaseId;

        /// <summary>True when the event marks a successful phase completion.</summary>
        public bool IsSuccess;
    }
}
