using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.Core.Network;
using Hrot.SimHost;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Replication.Components;
using EcsNavigationIntent = FDP.Toolkit.Navigation.NavigationIntent;
using EcsNavigationMode   = FDP.Toolkit.Navigation.NavigationMode;
using Fdp.ModuleHost_Core;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for the brain-aware right-click handler
    /// (<see cref="SimHostVisualization.HandleRightClickForEntity"/>).
    ///
    /// BD1-P2T1 success criteria (updated for PACK-I002):
    /// - Brain-dead plain-click path writes NavigationIntent with Mode=DirectPoint.
    /// - Brain-dead shift+click path still calls AddWaypoint.
    /// - Brain-active path calls ISimHostMissionSender.SendNavigateToPoint (BS1-T022).
    /// </summary>
    public class SimHostVisualizationTests : IDisposable
    {
        private static readonly Vector2 ClickPos = new(300f, 400f);

        private readonly EntityRepository _repo;

        public SimHostVisualizationTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<DoctrineState>();
            _repo.RegisterComponent<NetworkIdentity>();
            _repo.RegisterComponent<VehicleParams>();
            _repo.RegisterComponent<EcsNavigationIntent>();
        }

        public void Dispose() => _repo.Dispose();

        // ── Test 1: brain-dead plain click → NavigationIntent(DirectPoint) ────────

        /// <summary>
        /// Right-click on a brain-dead entity (no active doctrine) must write
        /// NavigationIntent{Mode=DirectPoint, FinalDestination, TargetSpeed=15f, ArrivalRadius=3f}
        /// and must NOT invoke <c>missionWriter</c> or <c>setDestination</c>.
        /// </summary>
        [Fact]
        public void RightClick_BrainDead_WritesNavigationIntentDirectPoint()
        {
            // Arrange: entity with no DoctrineState (brain-dead by absence of component).
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new EcsNavigationIntent { IntentId = 5u });

            bool setDestinationCalled = false;
            bool addWaypointCalled   = false;

            // Act: plain right-click (shift = false).
            SimHostVisualization.HandleRightClickForEntity(
                _repo, entity, ClickPos, shift: false,
                interp: TrajectoryInterpolation.CatmullRom,
                setDestination: (e, p, i) => { setDestinationCalled = true; },
                addWaypoint:    (e, p, i) => { addWaypointCalled    = true; },
                missionSender:  null);

            Assert.False(setDestinationCalled, "setDestination must NOT be called.");
            Assert.False(addWaypointCalled,    "addWaypoint must NOT be called.");

            var intent = _repo.GetComponent<EcsNavigationIntent>(entity);
            Assert.Equal(EcsNavigationMode.DirectPoint, intent.Mode);
            Assert.Equal(ClickPos, intent.FinalDestination);
            Assert.Equal(15f, intent.TargetSpeed);
            Assert.Equal(3.0f, intent.ArrivalRadius);
            Assert.Equal(6u, intent.IntentId); // 5 + 1
        }

        /// <summary>
        /// Same as above but with <c>DoctrineState { ActiveDoctrineHash = 0 }</c>
        /// (brain-dead via explicit None hash).
        /// </summary>
        [Fact]
        public void RightClick_BrainDead_ViaZeroHash_WritesNavigationIntent()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new DoctrineState { ActiveDoctrineHash = DoctrineIds.None });
            _repo.AddComponent(entity, new EcsNavigationIntent { IntentId = 0u });

            bool setDestinationCalled = false;

            SimHostVisualization.HandleRightClickForEntity(
                _repo, entity, ClickPos, shift: false,
                interp: TrajectoryInterpolation.CatmullRom,
                setDestination: (e, p, i) => { setDestinationCalled = true; },
                addWaypoint:    (e, p, i) => { },
                missionSender:  null);

            Assert.False(setDestinationCalled);
            var intent = _repo.GetComponent<EcsNavigationIntent>(entity);
            Assert.Equal(EcsNavigationMode.DirectPoint, intent.Mode);
            Assert.Equal(1u, intent.IntentId);
        }

        // ── Test 2: brain-dead shift+click → AddWaypoint ─────────────────────────

        /// <summary>
        /// Shift+right-click on a brain-dead entity must call <c>addWaypoint</c>, not
        /// <c>setDestination</c>, and must NOT mutate NavigationIntent.
        /// </summary>
        [Fact]
        public void ShiftRightClick_BrainDead_CallsAddWaypoint()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new EcsNavigationIntent { IntentId = 3u });
            // No DoctrineState → brain-dead.

            bool addWaypointCalled   = false;
            bool setDestinationCalled = false;

            SimHostVisualization.HandleRightClickForEntity(
                _repo, entity, ClickPos, shift: true,
                interp: TrajectoryInterpolation.CatmullRom,
                setDestination: (e, p, i) => { setDestinationCalled = true; },
                addWaypoint:    (e, p, i) => { addWaypointCalled    = true; },
                missionSender:  null);

            Assert.True(addWaypointCalled);
            Assert.False(setDestinationCalled);
            // NavigationIntent must NOT be mutated by the shift path
            var intent = _repo.GetComponent<EcsNavigationIntent>(entity);
            Assert.Equal(3u, intent.IntentId);
        }

        // ── Test 3: brain-active plain click → CMD_REPLACE_MISSION with trigger ──

        /// <summary>
        /// Right-click on a brain-active entity must call
        /// <see cref="ISimHostMissionSender.SendNavigateToPoint"/> with the entity's
        /// network ID and click position (BS1-T022).
        /// </summary>
        [Fact]
        public void RightClick_BrainActive_WritesMissionWithTrigger()
        {
            // Arrange: entity with active doctrine and a network identity.
            var stub = new StubMissionSender();
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new DoctrineState { ActiveDoctrineHash = 2001 });
            _repo.AddComponent(entity, new NetworkIdentity { Value = 99 });

            bool setDestinationCalled = false;

            // Act: plain right-click.
            SimHostVisualization.HandleRightClickForEntity(
                _repo, entity, ClickPos, shift: false,
                interp: TrajectoryInterpolation.CatmullRom,
                setDestination: (e, p, i) => { setDestinationCalled = true; },
                addWaypoint:    (e, p, i) => { },
                missionSender:  stub);

            // Neither muscle path should have been taken.
            Assert.False(setDestinationCalled);

            // Assert: SendNavigateToPoint called with the entity's network ID and click position.
            // No VehicleParams registered, so speed falls back to 15f.
            Assert.Single(stub.Sent);
            Assert.Equal(99L, stub.Sent[0].EntityId);
            Assert.Equal(ClickPos, stub.Sent[0].Destination);
            Assert.Equal(15f, stub.Sent[0].Speed);
            Assert.Equal(3.0f, stub.Sent[0].ArrivalRadius);
        }

        // ── Task BD1-P4T1: Camera offset set on Initialize ────────────────────

        /// <summary>
        /// BD1-P4T1 SC1: <see cref="SimHostVisualization.Initialize"/> must set
        /// <c>_map.Camera.Offset</c> to <c>new Vector2(640f, 360f)</c>, the centre of the
        /// default 1280×720 window, so "Center on entity" teleports to screen centre
        /// rather than pixel (0,0).
        /// </summary>
        [Fact]
        public void Initialize_SetsMapCameraOffset()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<DoctrineState>();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<VehicleParams>();
            repo.RegisterComponent<VehicleState>();

            var evtAcc    = new EventAccumulator();
            var kernel    = new ModuleHostKernel(repo, evtAcc);
            var road      = new RoadNetworkBlob();
            var traj      = new TrajectoryPoolManager();
            var formations = new FormationTemplateManager();

            var vis = new SimHostVisualization();
            using var missionSender = new StubMissionSender();
            vis.Initialize(repo, kernel, road, traj, formations, missionSender);

            var camera = vis.GetMapCamera();
            Assert.NotNull(camera);
            Assert.Equal(new Vector2(640f, 360f), camera.Offset);

            kernel.Dispose();
            repo.Dispose();
        }
    }

    internal sealed class StubMissionSender : ISimHostMissionSender
    {
        public readonly List<(long EntityId, Vector2 Destination, float Speed, float ArrivalRadius)> Sent = new();

        public void SendNavigateToPoint(long entityNetworkId, Vector2 destination, float speed, float arrivalRadius)
            => Sent.Add((entityNetworkId, destination, speed, arrivalRadius));

        public void Dispose() { }
    }
}
