using System.Numerics;
using System.Runtime.CompilerServices;
using CarKinem.Core;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Fdp.Toolkit.Navigation.Systems;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Layer-1 tests for <see cref="NavigationIntentBridgeSystem"/> LocomotionChannel
    /// action routing (BATCH-03 T2).
    /// </summary>
    public sealed class NavigationIntentBridgePipelineTests
    {
        private readonly EntityRepository _repo;
        private readonly TrajectoryPoolManager _pool;
        private readonly NavigationIntentBridgeSystem _system;
        private readonly ISimulationView _view;

        public NavigationIntentBridgePipelineTests()
        {
            _repo   = NavigationTestWorldFactory.Create();
            _pool   = new TrajectoryPoolManager();
            _system = new NavigationIntentBridgeSystem(_pool);
            _view   = (ISimulationView)_repo;

            _repo.RegisterEvent<PathfindingRequestEvent>();
        }

        // ── Test 1: MoveTo publishes exactly one PathfindingRequestEvent ───────────

        [Fact]
        public void MoveTo_PublishesExactlyOnePathRequest()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavState());
            _repo.AddComponent(entity, new NavigationStatus());

            var ch = new LocomotionChannel
            {
                ActiveAction     = NavigationConstants.ActionIdMoveTo,
                ActionInstanceId = 1,
            };
            unsafe
            {
                LocomotionChannel* pCh = &ch;
                *(MoveToParams*)pCh->Params = new MoveToParams
                {
                    Destination   = new Vector3(10f, 20f, 0f),
                    ArrivalRadius = 1f,
                    Speed         = 5f,
                };
            }
            _repo.AddComponent(entity, ch);

            _system.Execute(_repo, 0f);
            _repo.Bus.SwapBuffers();

            var events = _view.ReadEvents<PathfindingRequestEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(10f, events[0].End.X);
            Assert.Equal(20f, events[0].End.Y);
        }

        // ── Test 2: PlanRoute carries the Brain-allocated RouteHandle ──────────────

        [Fact]
        public void PlanRoute_PublishesRequestWithBrainHandle()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavigationIntent { RouteHandle = 99 });
            _repo.AddComponent(entity, new NavigationStatus());

            var ch = new LocomotionChannel
            {
                ActiveAction     = NavigationConstants.ActionIdPlanRoute,
                ActionInstanceId = 1,
            };
            unsafe
            {
                LocomotionChannel* pCh = &ch;
                *(PlanRouteParams*)pCh->Params = new PlanRouteParams
                {
                    Destination = new Vector3(5f, 5f, 0f),
                };
            }
            _repo.AddComponent(entity, ch);

            _system.Execute(_repo, 0f);
            _repo.Bus.SwapBuffers();

            var events = _view.ReadEvents<PathfindingRequestEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(99, events[0].RouteHandle);
        }

        // ── Test 3: FollowPath with unknown handle writes FailedInvalidHandle ──────

        [Fact]
        public void FollowPath_UnknownHandle_SetsFailedInvalidHandle()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavigationStatus());

            var ch = new LocomotionChannel
            {
                ActiveAction     = NavigationConstants.ActionIdFollowPath,
                ActionInstanceId = 1,
            };
            unsafe
            {
                LocomotionChannel* pCh = &ch;
                *(FollowPathParams*)pCh->Params = new FollowPathParams
                {
                    RouteHandle = 42,
                };
            }
            _repo.AddComponent(entity, ch);

            _system.Execute(_repo, 0f);

            var status = _repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationResult.FailedInvalidHandle, status.Result);
        }

        // ── Test 4: Same ActionInstanceId on consecutive ticks → no new event ─────

        [Fact]
        public void IdempotencyOnUnchangedActionInstanceId()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavState());
            _repo.AddComponent(entity, new NavigationStatus());

            var ch = new LocomotionChannel
            {
                ActiveAction     = NavigationConstants.ActionIdMoveTo,
                ActionInstanceId = 1,
            };
            unsafe
            {
                LocomotionChannel* pCh = &ch;
                *(MoveToParams*)pCh->Params = new MoveToParams
                {
                    Destination = new Vector3(1f, 1f, 0f),
                };
            }
            _repo.AddComponent(entity, ch);

            // First tick: should publish event.
            _system.Execute(_repo, 0f);
            _repo.Bus.SwapBuffers();
            // Drain the first-tick event so it does not bleed into the second read.
            _view.ReadEvents<PathfindingRequestEvent>();

            // Second tick: ActionInstanceId unchanged — bridge must NOT publish again.
            _system.Execute(_repo, 0f);
            _repo.Bus.SwapBuffers();

            var events2 = _view.ReadEvents<PathfindingRequestEvent>();
            Assert.Equal(0, events2.Length);
        }
    }

    // ── Crowd-side tests for NavigationIntentBridgeSystem (DD-Tests-Nav §4.3) ─────

    public sealed class NavigationIntentBridgeCrowdTests : IDisposable
    {
        private readonly EntityRepository _repo;
        private readonly FakeDtCrowdProvider _crowd;
        private readonly TrajectoryPoolManager _pool;
        private readonly NavigationIntentBridgeSystem _system;
        private readonly ISimulationView _view;

        public NavigationIntentBridgeCrowdTests()
        {
            _repo   = NavigationTestWorldFactory.Create();
            _repo.RegisterComponent<VehicleState>();
            _repo.RegisterEvent<PathfindingRequestEvent>();
            _crowd  = new FakeDtCrowdProvider();
            _pool   = new TrajectoryPoolManager();
            _system = new NavigationIntentBridgeSystem(_pool, _crowd);
            _view   = (ISimulationView)_repo;
        }

        public void Dispose() => _repo.Dispose();

        private unsafe LocomotionChannel MoveToChannel(uint instanceId, Vector2 dest, float speed = 5f)
        {
            var ch = new LocomotionChannel
            {
                ActiveAction     = NavigationConstants.ActionIdMoveTo,
                ActionInstanceId = instanceId,
            };
            LocomotionChannel* pCh = &ch;
            *(MoveToParams*)pCh->Params = new MoveToParams
            {
                Destination   = new Vector3(dest.X, dest.Y, 0f),
                ArrivalRadius = 1f,
                Speed         = speed,
            };
            return ch;
        }

        // ── Test 1: Humanoid MoveTo adds CrowdAgent tag ───────────────────────────

        [Fact]
        public void Humanoid_MoveTo_TagsCrowdAgent()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavigationStatus());
            _repo.AddComponent(entity, MoveToChannel(1, new Vector2(10f, 0f)));

            _system.Execute(_repo, 0f);

            Assert.True(_repo.HasComponent<CrowdAgent>(entity),
                "Humanoid MoveTo should add CrowdAgent tag");
        }

        // ── Test 2: Humanoid MoveTo registers with crowd provider ─────────────────

        [Fact]
        public void Humanoid_MoveTo_RegistersWithCrowdProvider()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavigationStatus());
            _repo.AddComponent(entity, MoveToChannel(1, new Vector2(10f, 0f)));

            _system.Execute(_repo, 0f);

            var api = (IFakeDtCrowdProviderTestApi)_crowd;
            Assert.Contains(entity.Index, api.RegisteredEntityIndices);
        }

        // ── Test 4: Wheeled MoveTo does NOT add CrowdAgent tag ────────────────────

        [Fact]
        public void Wheeled_MoveTo_NoCrowdTag()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavigationStatus());
            _repo.AddComponent(entity, default(VehicleState)); // marks entity as wheeled
            _repo.AddComponent(entity, MoveToChannel(1, new Vector2(10f, 0f)));

            _system.Execute(_repo, 0f);

            Assert.False(_repo.HasComponent<CrowdAgent>(entity),
                "Wheeled MoveTo must not add CrowdAgent");
        }

        // ── Test 6: FollowRoute does NOT register with crowd ──────────────────────

        [Fact]
        public void FollowRoute_AnyMobility_NoCrowdTag()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavigationStatus());
            var ch = new LocomotionChannel
            {
                ActiveAction     = NavigationConstants.ActionIdFollowRoute,
                ActionInstanceId = 1,
            };
            _repo.AddComponent(entity, ch);

            _system.Execute(_repo, 0f);

            Assert.False(_repo.HasComponent<CrowdAgent>(entity),
                "FollowRoute must not add CrowdAgent");
            var api = (IFakeDtCrowdProviderTestApi)_crowd;
            Assert.Empty(api.RegisteredEntityIndices);
        }

        // ── Test 7: PlanRoute does NOT register with crowd ────────────────────────

        [Fact]
        public unsafe void PlanRoute_NoFollowingStarted_NoCrowdRegistration()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavigationStatus());
            var ch = new LocomotionChannel
            {
                ActiveAction     = NavigationConstants.ActionIdPlanRoute,
                ActionInstanceId = 1,
            };
            LocomotionChannel* pCh = &ch;
            *(PlanRouteParams*)pCh->Params = new PlanRouteParams { Destination = new Vector3(5f, 5f, 0f) };
            _repo.AddComponent(entity, ch);

            _system.Execute(_repo, 0f);

            Assert.False(_repo.HasComponent<CrowdAgent>(entity));
            var api = (IFakeDtCrowdProviderTestApi)_crowd;
            Assert.Empty(api.RegisteredEntityIndices);
        }

        // ── Test 9: FollowPath with valid pool handle does not fail ───────────────

        [Fact]
        public unsafe void FollowPath_LooksUpHandleInMusclePool_StartsFollowing()
        {
            // Pre-populate the trajectory pool with handle 42.
            _pool.RegisterTrajectoryWithKey(
                new Vector2[] { Vector2.Zero, Vector2.One }, key: 42);

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavigationStatus());
            var ch = new LocomotionChannel
            {
                ActiveAction     = NavigationConstants.ActionIdFollowPath,
                ActionInstanceId = 1,
            };
            LocomotionChannel* pCh = &ch;
            *(FollowPathParams*)pCh->Params = new FollowPathParams { RouteHandle = 42 };
            _repo.AddComponent(entity, ch);

            _system.Execute(_repo, 0f);

            // With a valid handle, no FailedInvalidHandle should be written.
            var status = _repo.GetComponent<NavigationStatus>(entity);
            Assert.NotEqual(NavigationResult.FailedInvalidHandle, status.Result);
        }

        // ── Test 13: ReleasePath removes the handle from the trajectory pool ──────

        [Fact]
        public unsafe void ReleasePath_FreesMusclePoolEntry()
        {
            _pool.RegisterTrajectoryWithKey(
                new Vector2[] { Vector2.Zero, Vector2.One }, key: 77);

            Assert.True(_pool.TryGetTrajectory(77, out _));

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavigationCorridorMuscle());
            var ch = new LocomotionChannel
            {
                ActiveAction     = NavigationConstants.ActionIdReleasePath,
                ActionInstanceId = 1,
            };
            LocomotionChannel* pCh = &ch;
            *(ReleasePathParams*)pCh->Params = new ReleasePathParams { RouteHandle = 77 };
            _repo.AddComponent(entity, ch);

            _system.Execute(_repo, 0f);

            Assert.False(_pool.TryGetTrajectory(77, out _),
                "ReleasePath should remove handle 77 from the trajectory pool");
        }

        // ── Test 14: ReleasePath does NOT halt movement ───────────────────────────

        [Fact]
        public unsafe void ReleasePath_DoesNotStopMovement()
        {
            _pool.RegisterTrajectoryWithKey(
                new Vector2[] { Vector2.Zero, Vector2.One }, key: 88);

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavigationIntent { Mode = NavigationMode.DirectPoint });
            _repo.AddComponent(entity, new NavState { Mode = KinematicsMode.Direct, TargetSpeed = 5f });
            _repo.AddComponent(entity, new NavigationCorridorMuscle());
            var ch = new LocomotionChannel
            {
                ActiveAction     = NavigationConstants.ActionIdReleasePath,
                ActionInstanceId = 1,
            };
            LocomotionChannel* pCh = &ch;
            *(ReleasePathParams*)pCh->Params = new ReleasePathParams { RouteHandle = 88 };
            _repo.AddComponent(entity, ch);

            _system.Execute(_repo, 0f);

            // NavState.Mode must still be Direct — release does not stop movement.
            var nav = _repo.GetComponent<NavState>(entity);
            Assert.Equal(KinematicsMode.Direct, nav.Mode);
        }

        // ── Test 15: Changed ActionInstanceId triggers re-routing ────────────────

        [Fact]
        public void ActionInstanceIdMismatch_TriggersRouting()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavigationStatus());
            _repo.AddComponent(entity, MoveToChannel(1, new Vector2(10f, 0f)));

            // First tick publishes one event.
            _system.Execute(_repo, 0f);
            _repo.Bus.SwapBuffers();
            _view.ReadEvents<PathfindingRequestEvent>(); // drain first event

            // Change ActionInstanceId to trigger new routing on second tick.
            _repo.SetComponent(entity, MoveToChannel(2, new Vector2(20f, 0f)));

            _system.Execute(_repo, 0f);
            _repo.Bus.SwapBuffers();

            var events = _view.ReadEvents<PathfindingRequestEvent>();
            Assert.Equal(1, events.Length);
        }
    }
}
