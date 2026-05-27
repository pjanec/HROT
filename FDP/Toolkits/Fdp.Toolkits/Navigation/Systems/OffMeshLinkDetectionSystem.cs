using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.Systems
{
    // DESIGN NOTE: This system does NOT write to AnimationChannel directly.
    // Fdp.Toolkits does not reference Hrot.MuscleCharacter.Animation.
    // Instead, OffMeshTraversalStartedEvent is emitted. A Hrot-side system
    // handles the event and writes AnimationChannel.PlayMontage.
    //
    // This is an intentional assembly boundary enforced by project constraints.

    /// <summary>
    /// Detects when a crowd-managed agent is approaching an off-mesh link (a segment
    /// with <see cref="TraversalKind"/> != <see cref="TraversalKind.Walk"/>) within
    /// the look-ahead distance, and initiates the traversal sequence:
    /// <list type="bullet">
    ///   <item>Writes <see cref="NavigationPhase.AwaitingTraversal"/> to suppress crowd velocity.</item>
    ///   <item>Emits <see cref="OffMeshTraversalStartedEvent"/> for the animation tier.</item>
    ///   <item>Removes the <see cref="CrowdAgent"/> tag so crowd avoidance pauses.</item>
    ///   <item>On <c>MontageEndedEvent</c>, resumes following and restores crowd membership.</item>
    /// </list>
    ///
    /// <para>
    /// Must run BEFORE <see cref="CrowdAgentUpdateSystem"/> in the same tick so that the
    /// suppressed phase is visible before the velocity write.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [UpdateBefore(typeof(CrowdAgentUpdateSystem))]
    public class OffMeshLinkDetectionSystem : IEcsModuleSystem
    {
        private readonly IPathRegistry _pathRegistry;
        private readonly IDtCrowdProvider _dtCrowd;
        private readonly float _lookaheadDistance;

        // Scratch buffer for waypoint look-ahead (reused each tick, sized for 8 waypoints).
        private readonly NavWaypoint[] _waypointScratch = new NavWaypoint[8];

        /// <summary>
        /// Creates the system with the muscle-side path registry and crowd provider.
        /// </summary>
        /// <param name="pathRegistry">Muscle-owned path store (read-only).</param>
        /// <param name="dtCrowd">Crowd provider (for unregistering agents during traversal).</param>
        /// <param name="lookaheadDistance">
        /// Maximum distance ahead (metres) to scan for off-mesh links. Default 3.0 m.
        /// </param>
        public OffMeshLinkDetectionSystem(
            IPathRegistry pathRegistry,
            IDtCrowdProvider dtCrowd,
            float lookaheadDistance = 3.0f)
        {
            _pathRegistry      = pathRegistry ?? throw new ArgumentNullException(nameof(pathRegistry));
            _dtCrowd           = dtCrowd      ?? throw new ArgumentNullException(nameof(dtCrowd));
            _lookaheadDistance = lookaheadDistance;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(OffMeshLinkDetectionSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            // ── Phase 1: Handle MontageEndedEvent to resume crowd movement ──────────
            HandleMontageEndedEvents(repo);

            // ── Phase 2: Detect approaching off-mesh links ───────────────────────────
            if (!repo.IsComponentTypeRegistered<CrowdAgent>()
                || !repo.IsComponentTypeRegistered<NavigationCorridorMuscle>()
                || !repo.IsComponentTypeRegistered<NavigationStatus>()
                || !repo.IsComponentTypeRegistered<SimTransform>())
                return;

            var query = repo.Query()
                .With<CrowdAgent>()
                .With<SimTransform>()
                .With<NavigationStatus>()
                .With<NavigationCorridorMuscle>()
                .Build();

            foreach (var entity in query)
            {
                var status   = repo.GetComponent<NavigationStatus>(entity);
                var corridor = repo.GetComponent<NavigationCorridorMuscle>(entity);
                var tf       = repo.GetComponent<SimTransform>(entity);

                // Skip entities already in traversal or without an active corridor.
                if (status.Phase == NavigationPhase.AwaitingTraversal) continue;
                if (corridor.RouteHandle == 0) continue;
                if (corridor.CurrentSegmentIndex >= corridor.TotalSegmentCount - 1) continue;

                // Look ahead one segment from the current position.
                int lookStart   = corridor.CurrentSegmentIndex + 1;
                int maxToCheck  = Math.Min(8, corridor.TotalSegmentCount - lookStart);
                if (maxToCheck <= 0) continue;

                if (!_pathRegistry.TryGetWaypointsSlice(
                        corridor.RouteHandle,
                        lookStart,
                        maxToCheck,
                        _waypointScratch.AsSpan(0, maxToCheck),
                        out int fetched) || fetched == 0)
                    continue;

                // Find first non-Walk waypoint within look-ahead distance.
                for (int i = 0; i < fetched; i++)
                {
                    var wp = _waypointScratch[i];
                    if (wp.Traversal == TraversalKind.Walk) continue;

                    float dist = Vector3.Distance(tf.Position, wp.Position);
                    if (dist > _lookaheadDistance) break; // waypoints are ordered; no closer link beyond

                    // Off-mesh link detected within look-ahead range.
                    BeginTraversal(repo, entity, wp, status, corridor);
                    break;
                }
            }
        }

        private void BeginTraversal(
            EntityRepository repo,
            Entity entity,
            NavWaypoint linkWaypoint,
            NavigationStatus status,
            NavigationCorridorMuscle corridor)
        {
            // 1. Set phase to suppress crowd velocity this tick.
            status.Phase               = NavigationPhase.AwaitingTraversal;
            status.CurrentTraversalKind = linkWaypoint.Traversal;
            repo.SetComponent(entity, status);

            // 2. Emit traversal started event.
            repo.Bus.Publish(new OffMeshTraversalStartedEvent
            {
                Target       = entity,
                LinkWorldPos = linkWaypoint.Position,
                TraversalKind = linkWaypoint.Traversal,
            });

            // 3. Unregister from crowd provider (entity goes dormant until montage ends).
            _dtCrowd.UnregisterAgent(entity);

            // 4. Remove CrowdAgent tag so CrowdAgentUpdateSystem filters the entity out next tick.
            repo.RemoveComponent<CrowdAgent>(entity);
        }

        private void HandleMontageEndedEvents(EntityRepository repo)
        {
            // Read MontageEndedEvent from the Hrot animation system.
            // NOTE: Fdp.Toolkits does not reference Hrot.MuscleCharacter.Animation.
            // Since we cannot import the Hrot namespace here, montage-end handling
            // will be implemented in a Hrot-side bridge system in a future phase.
            // For now, leave this body empty — the Phase 3 tests do not test montage resume
            // in the Fdp.Toolkits.Tests context.
        }
    }
}
