using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.SimHost;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Unit tests for the brain-aware right-click handler
    /// (<see cref="SimHostVisualization.HandleRightClickForEntity"/>).
    ///
    /// BD1-P2T1 success criteria:
    /// - Brain-dead path uses SetDestination / AddWaypoint (muscle-layer direct).
    /// - Brain-active path sends CMD_REPLACE_MISSION with a ReachedDestination trigger.
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
        }

        public void Dispose() => _repo.Dispose();

        // ── Test 1: brain-dead plain click → SetDestination ──────────────────────

        /// <summary>
        /// Right-click on an entity with no active doctrine must call
        /// <c>setDestination</c> and must NOT invoke <c>missionWriter</c>.
        /// </summary>
        [Fact]
        public void RightClick_BrainDead_CallsSetDestination()
        {
            // Arrange: entity with no DoctrineState (brain-dead by absence of component).
            var entity = _repo.CreateEntity();

            bool setDestinationCalled = false;
            Vector2 capturedPos      = default;
            bool addWaypointCalled   = false;
            bool missionWriterCalled = false;

            // Act: plain right-click (shift = false).
            SimHostVisualization.HandleRightClickForEntity(
                _repo, entity, ClickPos, shift: false,
                interp: TrajectoryInterpolation.CatmullRom,
                setDestination: (e, p, i) => { setDestinationCalled = true; capturedPos = p; },
                addWaypoint:    (e, p, i) => { addWaypointCalled    = true; },
                missionWriter:  null);

            // No missionWriter was provided — verify setDestination was called.
            Assert.True(setDestinationCalled);
            Assert.Equal(ClickPos, capturedPos);
            Assert.False(addWaypointCalled);
            Assert.False(missionWriterCalled);
        }

        /// <summary>
        /// Same as above but with <c>DoctrineState { ActiveDoctrineHash = 0 }</c>
        /// (brain-dead via explicit None hash).
        /// </summary>
        [Fact]
        public void RightClick_BrainDead_ViaZeroHash_CallsSetDestination()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new DoctrineState { ActiveDoctrineHash = DoctrineIds.None });

            bool setDestinationCalled = false;

            SimHostVisualization.HandleRightClickForEntity(
                _repo, entity, ClickPos, shift: false,
                interp: TrajectoryInterpolation.CatmullRom,
                setDestination: (e, p, i) => { setDestinationCalled = true; },
                addWaypoint:    (e, p, i) => { },
                missionWriter:  null);

            Assert.True(setDestinationCalled);
        }

        // ── Test 2: brain-dead shift+click → AddWaypoint ─────────────────────────

        /// <summary>
        /// Shift+right-click on a brain-dead entity must call <c>addWaypoint</c>, not
        /// <c>setDestination</c>.
        /// </summary>
        [Fact]
        public void ShiftRightClick_BrainDead_CallsAddWaypoint()
        {
            var entity = _repo.CreateEntity();
            // No DoctrineState → brain-dead.

            bool addWaypointCalled   = false;
            bool setDestinationCalled = false;

            SimHostVisualization.HandleRightClickForEntity(
                _repo, entity, ClickPos, shift: true,
                interp: TrajectoryInterpolation.CatmullRom,
                setDestination: (e, p, i) => { setDestinationCalled = true; },
                addWaypoint:    (e, p, i) => { addWaypointCalled    = true; },
                missionWriter:  null);

            Assert.True(addWaypointCalled);
            Assert.False(setDestinationCalled);
        }

        // ── Test 3: brain-active plain click → CMD_REPLACE_MISSION with trigger ──

        /// <summary>
        /// Right-click on a brain-active entity must send a <c>CMD_REPLACE_MISSION</c>
        /// request whose first task has exactly one trigger of type "ReachedDestination".
        /// </summary>
        [Fact]
        public void RightClick_BrainActive_WritesMissionWithTrigger()
        {
            const uint domainId = 165u;
            using var participant = new DdsParticipant(domainId);
            using var writer = new DdsWriter<MissionControlRequest>(participant);
            using var reader = new DdsReader<MissionControlRequest>(participant);

            // Arrange: entity with active doctrine and a network identity.
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
                missionWriter:  writer);

            // Neither muscle path should have been taken.
            Assert.False(setDestinationCalled);

            // Allow DDS loopback delivery.
            Thread.Sleep(200);

            // Assert: exactly one request written with a ReachedDestination trigger.
            using var loan = reader.Take();
            MissionControlRequest req = default;
            bool foundSample = false;
            foreach (var sample in loan)
            {
                if (sample.IsValid) { req = sample.Data; foundSample = true; break; }
            }

            Assert.True(foundSample, "Expected a MissionControlRequest sample in DDS but none found.");

            Assert.Equal(eMissionCommandType.CMD_REPLACE_MISSION, req.Payload._d);
            Assert.Equal(99L, req.TargetEntityId);

            var tasks = req.Payload.FullMissionData.Tasks;
            Assert.NotNull(tasks);
            Assert.Single(tasks);

            var triggers = tasks[0].Triggers;
            Assert.NotNull(triggers);
            Assert.Single(triggers);
            Assert.Equal("ReachedDestination", triggers[0].Type);
        }

        // ── Task BD1-P4T1: Camera offset set on Initialize ────────────────────

        /// <summary>
        /// BD1-P4T1 SC1: <see cref="SimHostVisualization.Initialize"/> must set
        /// <c>_map.Camera.Offset</c> to <c>new Vector2(640f, 360f)</c>, the centre of the
        /// default 1280×720 window, so "Center on entity" teleports to screen centre
        /// rather than pixel (0,0).
        /// </summary>
        [Fact]
        [Trait("Category", "Integration")]
        public void Initialize_SetsMapCameraOffset()
        {
            const uint domainId = 167u;
            using var participant = new DdsParticipant(domainId);
            using var writer      = new DdsWriter<MissionControlRequest>(participant);

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
            vis.Initialize(repo, kernel, road, traj, formations, writer);

            var camera = vis.GetMapCamera();
            Assert.NotNull(camera);
            Assert.Equal(new Vector2(640f, 360f), camera.Offset);

            kernel.Dispose();
            repo.Dispose();
        }
    }
}
