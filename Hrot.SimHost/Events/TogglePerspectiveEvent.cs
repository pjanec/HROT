using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Components;

namespace Hrot.SimHost.Events
{
    /// <summary>
    /// ECS event published when the operator requests a perspective switch
    /// (e.g. by pressing a toggle button in the ImGui UI).
    ///
    /// <para>Consumed by <c>PerspectiveCoordinatorSystem</c> which flips
    /// <see cref="Components.ActivePerspective.Current"/> and snaps the
    /// incoming camera to the outgoing camera's state.</para>
    /// </summary>
    [EventId(SimHostEventIds.TogglePerspective)]
    public struct TogglePerspectiveEvent
    {
        // Intentionally empty — the event carries no payload; its presence is
        // sufficient to trigger the perspective swap.
    }
}
