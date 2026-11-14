using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// ⭐ <c>CE-3110</c> I-2a — "everyone out of <see cref="Vehicle"/>": raised by <c>EjectPassengersExecutor</c> (the vehicle's HSM on
    /// MobilityLost, a blueprint node) and applied by <c>EmbarkationSystem</c> on the vehicle's owner. One command for the whole
    /// column — the dismount places them together (CE-321). 📄 docs/DESIGN_Entity_Interactions.md §7.
    /// </summary>
    [EventId(BehaviorConstants.EventId_EjectPassengers)]
    public struct EjectPassengersCommand
    {
        public Entity Vehicle;
        /// <summary>Set by the interaction ingress: this command came off the network.</summary>
        public bool IsRemote;
    }
}
