using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// Unmanaged command event requesting that a passenger entity boards a vehicle entity.
    /// Published by mission-layer systems (e.g. EmbarkExecutor) and consumed by
    /// <c>EmbarkationSystem</c> to execute the actual component-level boarding.
    /// </summary>
    [EventId(BehaviorConstants.EventId_EmbarkEntity)]
    public struct EmbarkEntityCommand
    {
        /// <summary>The entity that should board <see cref="Vehicle"/>.</summary>
        public Entity Passenger;

        /// <summary>The vehicle entity that <see cref="Passenger"/> will board.</summary>
        public Entity Vehicle;
    }
}
