using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation.Fake;

namespace Fdp.Toolkit.Navigation.Systems
{
    /// <summary>
    /// Reads <see cref="NavigationPathDetailsResponseEvent"/>s published by the Muscle-side
    /// path-details system and ingests them into the Brain-side path cache.
    ///
    /// <para>Per event the system:</para>
    /// <list type="number">
    ///   <item>Queries waypoints from <paramref name="muscleRegistry"/>.</item>
    ///   <item>Calls <see cref="BrainPathRegistry.TryIngestResponse"/> to populate the Brain cache.</item>
    ///   <item>Updates <see cref="NavigationPathDetailsBuffer"/> on the target entity.</item>
    ///   <item>Emits <see cref="NavigationPathDetailsArrivedEvent"/> on the bus.</item>
    /// </list>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class NavigationPathDetailsUpdateSystem : IEcsModuleSystem
    {
        private readonly IPathRegistry     _muscleRegistry;
        private readonly BrainPathRegistry _brainRegistry;

        // Scratch buffer for waypoint copy; avoids per-event heap allocation.
        private readonly NavWaypoint[] _waypointScratch;

        /// <param name="muscleRegistry">Source of stored waypoints (Muscle side).</param>
        /// <param name="brainRegistry">Brain-side LRU cache to populate.</param>
        /// <param name="maxWaypointsPerPath">Scratch buffer capacity (default 256).</param>
        public NavigationPathDetailsUpdateSystem(
            IPathRegistry     muscleRegistry,
            BrainPathRegistry brainRegistry,
            int               maxWaypointsPerPath = 256)
        {
            _muscleRegistry  = muscleRegistry;
            _brainRegistry   = brainRegistry;
            _waypointScratch = new NavWaypoint[maxWaypointsPerPath];
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var events = view.ReadEvents<NavigationPathDetailsResponseEvent>();
            if (events.IsEmpty) return;

            if (view is not EntityRepository repo) return;

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];

                var entity = evt.Target;
                if (!repo.IsAlive(entity)) continue;

                // Get summary for distance/version/backend metadata.
                if (!_muscleRegistry.TryGetSummary(evt.RouteHandle, out var summary))
                    continue;

                // Copy waypoints from Muscle registry into scratch.
                if (!_muscleRegistry.TryGetWaypoints(
                        evt.RouteHandle, _waypointScratch.AsSpan(), out int count))
                    continue;

                var waypoints = new NavWaypoint[count];
                Array.Copy(_waypointScratch, waypoints, count);

                // Ingest into Brain LRU cache.
                _brainRegistry.TryIngestResponse(
                    entity,
                    evt.RouteHandle,
                    waypoints,
                    evt.ReplanCount,
                    summary.TotalDistanceMeters,
                    summary.NavmeshVersionAtPlan,
                    summary.PrimaryBackend);

                // Update NavigationPathDetailsBuffer if the component is present.
                if (repo.IsComponentTypeRegistered<NavigationPathDetailsBuffer>()
                    && repo.HasComponent<NavigationPathDetailsBuffer>(entity))
                {
                    var buf = repo.GetComponent<NavigationPathDetailsBuffer>(entity);
                    buf.RouteHandle        = evt.RouteHandle;
                    buf.ReplanCountAtFetch = (ushort)evt.ReplanCount;
                    buf.WaypointCount      = (ushort)count;
                    buf.TotalDistance      = summary.TotalDistanceMeters;
                    repo.SetComponent(entity, buf);
                }

                // Emit arrived notification.
                repo.Bus.Publish(new NavigationPathDetailsArrivedEvent
                {
                    Target        = entity,
                    RouteHandle   = evt.RouteHandle,
                    IsAutoRefresh = evt.IsAutoRefresh,
                });
            }
        }
    }
}
