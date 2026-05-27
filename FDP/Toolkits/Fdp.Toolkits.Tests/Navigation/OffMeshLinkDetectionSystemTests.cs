using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Fdp.Toolkit.Navigation.Systems;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// DD-Tests-Nav §4.1 — <see cref="OffMeshLinkDetectionSystem"/> unit tests.
    /// Seven rows covering the zero-frame-suppression mechanism.
    ///
    /// Note: MontageEndedEvent handling is a Hrot-side concern (assembly boundary);
    /// tests here cover detection only. The "PlayMontageWritten" test verifies
    /// that OffMeshTraversalStartedEvent carries the correct TraversalKind discriminant
    /// (the event is what triggers animation-tier montage selection).
    /// </summary>
    public class OffMeshLinkDetectionSystemTests
    {
        private const int RouteHandle = 10;
        private const float Lookahead = 3.0f;

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<NavigationStatus>();
            repo.RegisterComponent<NavigationCorridorMuscle>();
            repo.RegisterComponent<CrowdAgent>();
            repo.RegisterEvent<OffMeshTraversalStartedEvent>();
            return repo;
        }

        /// <summary>
        /// Creates a MusclePathRegistry with a two-waypoint path: Walk -> JumpAcross.
        /// The Walk waypoint is at (0,0,0) and the Jump waypoint is at (5,0,0).
        /// </summary>
        private static MusclePathRegistry CreateRegistryWithOffMeshLink(
            Vector3 walkPos, Vector3 jumpPos)
        {
            var registry = new MusclePathRegistry();
            registry.StoreOrReplace(RouteHandle, new[]
            {
                new NavWaypoint { Position = walkPos, Traversal = TraversalKind.Walk },
                new NavWaypoint { Position = jumpPos, Traversal = TraversalKind.Jump },
            });
            return registry;
        }

        /// <summary>
        /// Creates a MusclePathRegistry with only Walk waypoints.
        /// </summary>
        private static MusclePathRegistry CreateRegistryAllWalk(
            Vector3 from, Vector3 to)
        {
            var registry = new MusclePathRegistry();
            registry.StoreOrReplace(RouteHandle, new[]
            {
                new NavWaypoint { Position = from, Traversal = TraversalKind.Walk },
                new NavWaypoint { Position = to,   Traversal = TraversalKind.Walk },
            });
            return registry;
        }

        private static (Entity entity, FakeDtCrowdProvider crowd) CreateCrowdAgentEntity(
            EntityRepository repo, Vector3 position, int currentSegment = 0, int totalSegments = 2)
        {
            var crowd = new FakeDtCrowdProvider();
            var entity = repo.CreateEntity();

            repo.AddComponent(entity, new SimTransform { Position = position });
            repo.AddComponent(entity, new SimVelocity());
            repo.AddComponent(entity, new NavigationStatus { Phase = NavigationPhase.Following });
            repo.AddComponent(entity, default(CrowdAgent));
            repo.AddComponent(entity, new NavigationCorridorMuscle
            {
                RouteHandle          = RouteHandle,
                CurrentSegmentIndex  = currentSegment,
                TotalSegmentCount    = totalSegments,
            });

            crowd.RegisterAgent(entity, new CrowdAgentParams
            {
                Radius = 0.4f, Height = 1.8f, MaxSpeed = 5f, MaxAcceleration = 20f,
            });

            return (entity, crowd);
        }

        // ── Test 1: No off-mesh link in path -> phase unchanged ─────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.1 row 1: NoLink_PhaseUnchanged.
        /// No off-mesh link in corridor — Phase must not be modified.
        /// </summary>
        [Fact]
        public void NoLink_PhaseUnchanged()
        {
            using var repo = CreateWorld();
            var registry = CreateRegistryAllWalk(new Vector3(0, 0, 0), new Vector3(5, 0, 0));
            var (entity, crowd) = CreateCrowdAgentEntity(repo, position: Vector3.Zero);
            var system = new OffMeshLinkDetectionSystem(registry, crowd, Lookahead);

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            var status = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationPhase.Following, status.Phase);
        }

        // ── Test 2: Off-mesh link beyond look-ahead -> phase unchanged ───────────────

        /// <summary>
        /// DD-Tests-Nav §4.1 row 2: LinkBeyondLookahead_PhaseUnchanged.
        /// Link is in path but agent is far away (outside look-ahead) — no write.
        /// </summary>
        [Fact]
        public void LinkBeyondLookahead_PhaseUnchanged()
        {
            using var repo = CreateWorld();
            // Jump link is at (5,0,0). Agent at (0,0,0) — distance = 5m > Lookahead (3m).
            var registry = CreateRegistryWithOffMeshLink(
                walkPos: new Vector3(0, 0, 0), jumpPos: new Vector3(5, 0, 0));
            var (entity, crowd) = CreateCrowdAgentEntity(repo, position: Vector3.Zero);
            var system = new OffMeshLinkDetectionSystem(registry, crowd, Lookahead);

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            var status = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationPhase.Following, status.Phase);
        }

        // ── Test 3: Off-mesh link within look-ahead -> AwaitingTraversal ────────────

        /// <summary>
        /// DD-Tests-Nav §4.1 row 3: LinkWithinLookahead_PhaseSetToAwaitingTraversal.
        /// Agent at (4,0,0), Jump link at (5,0,0) — distance = 1m (within 3m lookahead).
        /// </summary>
        [Fact]
        public void LinkWithinLookahead_PhaseSetToAwaitingTraversal()
        {
            using var repo = CreateWorld();
            var registry = CreateRegistryWithOffMeshLink(
                walkPos: new Vector3(0, 0, 0), jumpPos: new Vector3(5, 0, 0));
            // Agent close to jump link.
            var (entity, crowd) = CreateCrowdAgentEntity(repo, position: new Vector3(4, 0, 0));
            var system = new OffMeshLinkDetectionSystem(registry, crowd, Lookahead);

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            var status = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationPhase.AwaitingTraversal, status.Phase);
        }

        // ── Test 4: Detection emits event with TraversalKind discriminant ───────────

        /// <summary>
        /// DD-Tests-Nav §4.1 row 4: LinkDetected_PlayMontageWritten.
        /// When link is detected, OffMeshTraversalStartedEvent carries the TraversalKind
        /// discriminant (the animation tier uses this to select the montage).
        /// </summary>
        [Fact]
        public void LinkDetected_TraversalStartedEventCarriesKind()
        {
            using var repo = CreateWorld();
            var registry = CreateRegistryWithOffMeshLink(
                walkPos: new Vector3(0, 0, 0), jumpPos: new Vector3(5, 0, 0));
            var (entity, crowd) = CreateCrowdAgentEntity(repo, position: new Vector3(4, 0, 0));
            var system = new OffMeshLinkDetectionSystem(registry, crowd, Lookahead);

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            // Event must have been published with TraversalKind.Jump.
            repo.Bus.SwapBuffers();
            var events = repo.Bus.Read<OffMeshTraversalStartedEvent>().ToArray();
            Assert.Single(events);
            Assert.Equal(TraversalKind.Jump, events[0].TraversalKind);
        }

        // ── Test 5: CrowdAgent tag removed after detection ──────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.1 row 5: LinkDetected_CrowdAgentTagRemovedViaECB.
        /// After detection, entity no longer has CrowdAgent (removed directly or via ECB flush).
        /// </summary>
        [Fact]
        public void LinkDetected_CrowdAgentTagRemoved()
        {
            using var repo = CreateWorld();
            var registry = CreateRegistryWithOffMeshLink(
                walkPos: new Vector3(0, 0, 0), jumpPos: new Vector3(5, 0, 0));
            var (entity, crowd) = CreateCrowdAgentEntity(repo, position: new Vector3(4, 0, 0));
            var system = new OffMeshLinkDetectionSystem(registry, crowd, Lookahead);

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            // CrowdAgent must be gone.
            Assert.False(repo.HasComponent<CrowdAgent>(entity));
        }

        // ── Test 6: Event emitted with LinkWorldPos ─────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.1 row 6: LinkDetected_OffMeshTraversalStartedEventEmitted.
        /// Event carries correct TraversalKind and LinkWorldPos.
        /// </summary>
        [Fact]
        public void LinkDetected_OffMeshTraversalStartedEventEmitted()
        {
            using var repo = CreateWorld();
            var jumpPos = new Vector3(5, 0, 0);
            var registry = CreateRegistryWithOffMeshLink(
                walkPos: new Vector3(0, 0, 0), jumpPos: jumpPos);
            var (entity, crowd) = CreateCrowdAgentEntity(repo, position: new Vector3(4, 0, 0));
            var system = new OffMeshLinkDetectionSystem(registry, crowd, Lookahead);

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            // Events are visible after the next SwapBuffers.
            repo.Bus.SwapBuffers();
            var events = repo.Bus.Read<OffMeshTraversalStartedEvent>().ToArray();
            Assert.Single(events);
            Assert.Equal(entity, events[0].Target);
            Assert.Equal(jumpPos, events[0].LinkWorldPos);
            Assert.Equal(TraversalKind.Jump, events[0].TraversalKind);
        }

        // ── Test 7: Multiple agents at same link -> both detected ────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.1 row 7: MultipleAgentsAtSameLink_AllDetectedSameTick.
        /// Two agents close to the same jump link; both trigger detection in the same tick.
        /// </summary>
        [Fact]
        public void MultipleAgentsAtSameLink_AllDetectedSameTick()
        {
            using var repo = CreateWorld();
            var registry = CreateRegistryWithOffMeshLink(
                walkPos: new Vector3(0, 0, 0), jumpPos: new Vector3(5, 0, 0));

            var (entity1, crowd1) = CreateCrowdAgentEntity(
                repo, position: new Vector3(4, 0, 0));
            var (entity2, crowd2) = CreateCrowdAgentEntity(
                repo, position: new Vector3(3.5f, 0, 0));

            // Use entity1's crowd provider for both; entity2 registered with crowd1 too.
            crowd1.RegisterAgent(entity2, new CrowdAgentParams
            {
                Radius = 0.4f, Height = 1.8f, MaxSpeed = 5f, MaxAcceleration = 20f,
            });

            var system = new OffMeshLinkDetectionSystem(registry, crowd1, Lookahead);

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            // Both entities should be in AwaitingTraversal.
            Assert.Equal(NavigationPhase.AwaitingTraversal,
                repo.GetComponent<NavigationStatus>(entity1).Phase);
            Assert.Equal(NavigationPhase.AwaitingTraversal,
                repo.GetComponent<NavigationStatus>(entity2).Phase);
        }
    }
}
