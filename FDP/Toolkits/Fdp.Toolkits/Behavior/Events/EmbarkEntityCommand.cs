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

        /// <summary>⭐ <c>CE-3110</c> I-2a (R-221) — set by the interaction ingress: this command came off the network (never sent again).</summary>
        public bool IsRemote;

        /// <summary>⭐ <c>CE-2151</c> B-b (R-2009 B4) — the seat role asked for: <c>Passenger</c> (the default — a plain boarding) or a crew role,
        /// which only that role's seat takes.</summary>
        public Fdp.Toolkit.Behavior.Components.SeatRole Role;
    }
}
