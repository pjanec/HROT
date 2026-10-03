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

        /// <summary>⭐ CE-3026 — what MoveToExecutor writes (and what crosses the wire to SimHost on a cluster).</summary>
        private static NavigationIntent PathToPoint(uint id, Vector3 dest, uint layerMask = 0) => new NavigationIntent
        {
            Mode = NavigationMode.PathToPoint, IntentId = id, FinalDestination = dest,
            ArrivalRadius = 1f, TargetSpeed = 5f, LayerMask = layerMask,
        };

        // ── Test 1: a PathToPoint intent publishes exactly one PathfindingRequestEvent ─

        [Fact]
        public void MoveTo_PublishesExactlyOnePathRequest()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavState());
            _repo.AddComponent(entity, new NavigationStatus());
            _repo.AddComponent(entity, PathToPoint(1, new Vector3(10f, 20f, 0f)));

            _system.Execute(_repo, 0f);
            _repo.Bus.SwapBuffers();

            var events = _view.ReadEvents<PathfindingRequestEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(10f, events[0].End.X);
            Assert.Equal(20f, events[0].End.Y);
            // ⭐ No straight-line start while the path is being planned.
            Assert.Equal(KinematicsMode.None, _repo.GetComponent<NavState>(entity).Mode);
        }

        /// <summary>⭐ CE-3026 — DirectPoint means STRAIGHT: no path request, the vehicle drives at once.</summary>
        [Fact]
        public void DirectPoint_DrivesStraight_PublishesNoPathRequest()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavState());
            _repo.AddComponent(entity, new NavigationStatus());
            var intent = PathToPoint(1, new Vector3(10f, 20f, 0f));
            intent.Mode = NavigationMode.DirectPoint;
            _repo.AddComponent(entity, intent);

            _system.Execute(_repo, 0f);
            _repo.Bus.SwapBuffers();

            Assert.Equal(0, _view.ReadEvents<PathfindingRequestEvent>().Length);
            Assert.Equal(KinematicsMode.Direct, _repo.GetComponent<NavState>(entity).Mode);
        }

        /// <summary>⭐ CE-3026 — the Brain's LocomotionChannel no longer drives the bridge: a MoveTo on the channel ALONE
        /// (no intent) plans nothing. On a cluster the channel never reaches this node (R-180), so planning from it was an
        /// editor-only path.</summary>
        [Fact]
        public void MoveTo_OnTheChannelAlone_IsNotPlanned()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavState());
            var ch = new LocomotionChannel { ActiveAction = NavigationConstants.ActionIdMoveTo, ActionInstanceId = 1 };
            unsafe
            {
                LocomotionChannel* pCh = &ch;
                *(MoveToParams*)pCh->Params = new MoveToParams { Destination = new Vector3(10f, 20f, 0f), Speed = 5f };
            }
            _repo.AddComponent(entity, ch);

            _system.Execute(_repo, 0f);
            _repo.Bus.SwapBuffers();

            Assert.Equal(0, _view.ReadEvents<PathfindingRequestEvent>().Length);
        }

        /// <summary>⭐ CE-3026 — the explicit layer and backend ride the intent (they used to ride only the channel).</summary>
        [Fact]
        public void PathToPoint_CarriesExplicitLayerAndBackend()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavState());
            var intent = PathToPoint(1, new Vector3(10f, 20f, 0f), (uint)NavLayerMask.Naval);
            intent.BackendForce = (byte)NavigationBackend.Navmesh;
            intent.RouteHandle  = 7;
            _repo.AddComponent(entity, intent);

            _system.Execute(_repo, 0f);
            _repo.Bus.SwapBuffers();

            var events = _view.ReadEvents<PathfindingRequestEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal((int)NavLayerMask.Naval, events[0].NavLayerMask);
            Assert.Equal(NavigationBackend.Navmesh, events[0].BackendForce);
            Assert.Equal(7, events[0].RouteHandle);
        }

        // ── The layer a request plans on (live run 2026-10-03) ───────────────────────

        /// <summary>
        /// 🔴 Found by the 2026-10-03 live run: every request left with <c>NavLayerMask = 0</c> ("all layers"), and the
        /// provider took the FIRST layer that found a path — the infantry mesh — so a tank hugged a building at infantry
        /// clearance. ⭐ A MoveTo with no explicit mask now plans on Vehicle for a <see cref="VehicleState"/> entity and on
        /// Infantry otherwise (design: one bit, defaulting from the agent profile).
        /// </summary>
        [Theory]
        [InlineData(true,  NavLayerMask.Vehicle)]
        [InlineData(false, NavLayerMask.Infantry)]
        public void MoveTo_WithNoExplicitLayer_PlansOnTheEntitysOwnLayer(bool isVehicle, NavLayerMask expected)
        {
            if (!_repo.IsComponentTypeRegistered<VehicleState>()) _repo.RegisterComponent<VehicleState>();
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavState());
            _repo.AddComponent(entity, new NavigationStatus());
            if (isVehicle) _repo.AddComponent(entity, new VehicleState());

            _repo.AddComponent(entity, PathToPoint(1, new Vector3(10f, 20f, 0f)));

            _system.Execute(_repo, 0f);
            _repo.Bus.SwapBuffers();

            var events = _view.ReadEvents<PathfindingRequestEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal((int)expected, events[0].NavLayerMask);
        }

        /// <summary>The rule's precedence: an explicit mask wins, then the agent profile's preferred layer, then the kind.</summary>
        [Fact]
        public void NavLayerSelection_Precedence_ExplicitThenProfileThenKind()
        {
            if (!_repo.IsComponentTypeRegistered<VehicleState>()) _repo.RegisterComponent<VehicleState>();
            if (!_repo.IsComponentTypeRegistered<NavAgentProfile>()) _repo.RegisterComponent<NavAgentProfile>();
            var e = _repo.CreateEntity();
            _repo.AddComponent(e, new VehicleState());
            Assert.Equal(NavLayerMask.Vehicle, NavLayerSelection.For(_repo, e, 0));
            _repo.AddComponent(e, new NavAgentProfile { PreferredLayerMask = (uint)NavLayerMask.Naval });
            Assert.Equal(NavLayerMask.Naval, NavLayerSelection.For(_repo, e, 0));
            Assert.Equal(NavLayerMask.Infantry, NavLayerSelection.For(_repo, e, (uint)NavLayerMask.Infantry));
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

        // ── Test 4: Same IntentId on consecutive ticks → no new event ─────────────

        [Fact]
        public void IdempotencyOnUnchangedIntentId()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavState());
            _repo.AddComponent(entity, new NavigationStatus());
            _repo.AddComponent(entity, PathToPoint(1, new Vector3(1f, 1f, 0f)));

            // First tick: should publish event.
            _system.Execute(_repo, 0f);
            _repo.Bus.SwapBuffers();
            // Drain the first-tick event so it does not bleed into the second read.
            _view.ReadEvents<PathfindingRequestEvent>();

            // Second tick: IntentId unchanged (even with the intent re-written) — bridge must NOT publish again.
            _repo.Tick();
            _repo.SetComponent(entity, PathToPoint(1, new Vector3(1f, 1f, 0f)));
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

        /// <summary>⭐ CE-3026 — a MoveTo reaches the vehicle side as a PathToPoint intent (what MoveToExecutor writes).</summary>
        private static NavigationIntent MoveToIntent(uint intentId, Vector2 dest, float speed = 5f) => new NavigationIntent
        {
            Mode             = NavigationMode.PathToPoint,
            IntentId         = intentId,
            FinalDestination = new Vector3(dest.X, dest.Y, 0f),
            ArrivalRadius    = 1f,
            TargetSpeed      = speed,
        };

        // ── Test 1: Humanoid MoveTo adds CrowdAgent tag ───────────────────────────

        [Fact]
        public void Humanoid_MoveTo_TagsCrowdAgent()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavigationStatus());
            _repo.AddComponent(entity, MoveToIntent(1, new Vector2(10f, 0f)));

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
            _repo.AddComponent(entity, MoveToIntent(1, new Vector2(10f, 0f)));

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
            _repo.AddComponent(entity, new NavState());
            _repo.AddComponent(entity, MoveToIntent(1, new Vector2(10f, 0f)));

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

        // ── Test 15: Changed IntentId triggers re-routing ────────────────

        [Fact]
        public void IntentIdChange_TriggersRouting()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            _repo.AddComponent(entity, new NavigationStatus());
            _repo.AddComponent(entity, MoveToIntent(1, new Vector2(10f, 0f)));

            // First tick publishes one event.
            _system.Execute(_repo, 0f);
            _repo.Bus.SwapBuffers();
            _view.ReadEvents<PathfindingRequestEvent>(); // drain first event

            // Change IntentId to trigger new routing on second tick.
            _repo.Tick();
            _repo.SetComponent(entity, MoveToIntent(2, new Vector2(20f, 0f)));

            _system.Execute(_repo, 0f);
            _repo.Bus.SwapBuffers();

            var events = _view.ReadEvents<PathfindingRequestEvent>();
            Assert.Equal(1, events.Length);
        }
    }
}
