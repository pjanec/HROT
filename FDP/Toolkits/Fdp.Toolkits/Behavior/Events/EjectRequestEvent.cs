using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// ⭐ Airborne insertion B-8 / B-9 (docs/designs/airborne/DESIGN_Airborne_Insertion.md §7d, D-14) — "the crew of <see cref="Aircraft"/>
    /// ejects": raised by <c>EjectExecutor</c> (a behaviour's Eject action) and read by <c>EjectionSystem</c> on the node owning the
    /// aircraft's Health, which spawns the pilot and then abandons the aircraft. Raised and read on the Brain of the aircraft.
    /// </summary>
    [EventId(BehaviorConstants.EventId_EjectRequest)]
    public struct EjectRequestEvent
    {
        public Entity Aircraft;
    }
}
