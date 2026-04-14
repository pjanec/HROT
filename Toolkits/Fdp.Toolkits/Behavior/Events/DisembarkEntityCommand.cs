using Fdp.Kernel;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// Unmanaged command event requesting that a passenger entity exits their current vehicle.
    /// Published by mission-layer systems and consumed by <c>EmbarkationSystem</c>
    /// to execute the actual component-level disembarkation.
    /// </summary>
    [EventId(BehaviorConstants.EventId_DisembarkEntity)]
    public struct DisembarkEntityCommand
    {
        /// <summary>The entity that should exit its current vehicle.</summary>
        public Entity Passenger;
    }
}
