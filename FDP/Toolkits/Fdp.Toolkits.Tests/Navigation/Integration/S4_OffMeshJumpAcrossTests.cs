using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// S4 -- Off-mesh jump link detection integration test.
    /// Verifies that OffMeshLinkDetectionSystem fires OffMeshTraversalStartedEvent,
    /// sets Phase=AwaitingTraversal, and removes CrowdAgent when a Jump waypoint
    /// is within the look-ahead distance.
    /// </summary>
    public sealed class S4_OffMeshJumpAcrossTests
    {
        [Fact]
        public void OffMeshJump_OffMeshLinkDetected_EventFiresAndPhaseSetToAwaiting()
        {
            using var h = new NavTestHarness(NavTestMaps.LoadOffMeshJump());

            // Entity inside polygon 0 (X: 0..10, Z: 0..10), 2 m from the Jump waypoint.
            var entity = h.SpawnInfantry(new Vector2(7f, 0f));

            // Allocate a route handle and seed the path registry directly,
            // bypassing the solver so the test is hermetic.
            int handle = NavigationHandleAllocator.Allocate();
            h.PathRegistry.Muscle.StoreOrReplace(handle, new[]
            {
                new NavWaypoint { Position = new Vector3(5f, 0f, 0f), Traversal = TraversalKind.Walk },
                new NavWaypoint { Position = new Vector3(9f, 0f, 0f), Traversal = TraversalKind.Jump },
            });

            // Attach the corridor component (not added by SpawnInfantry).
            h.Repo.AddComponent(entity, new NavigationCorridorMuscle
            {
                RouteHandle         = handle,
                CurrentSegmentIndex = 0,
                TotalSegmentCount   = 2,
            });

            // Place the entity into Following phase so OffMeshDetect will process it.
            ref var status = ref h.Repo.GetComponentRW<NavigationStatus>(entity);
            status.Phase = NavigationPhase.Following;

            h.Tick();

            // OffMeshTraversalStartedEvent must have been captured.
            Assert.True(h.EventLog.HasOffMeshTraversalStarted(),
                "Expected OffMeshTraversalStartedEvent to be captured after detecting Jump waypoint.");

            // Phase must have been set to AwaitingTraversal.
            var updatedStatus = h.Repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationPhase.AwaitingTraversal, updatedStatus.Phase);

            // CrowdAgent tag must have been removed.
            Assert.False(h.Repo.HasComponent<CrowdAgent>(entity),
                "CrowdAgent should be removed when off-mesh traversal begins.");
        }
    }
}
