using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Systems;
using Xunit;

namespace Fdp.Toolkit.Physics.Tests
{
    /// <summary>
    /// Unit tests for <see cref="RaycastSolverSystem"/> (BCS-P4-T3).
    /// Each test uses a real <see cref="SpatialHashGrid"/> to exercise the broadphase path.
    /// Tests use the full event pipeline: publish RaycastRequestEvent, swap, solve, playback,
    /// swap, materialize — then check the ring buffer slot.
    /// </summary>
    public class RaycastSolverSystemTests : IDisposable
    {
        private readonly EntityRepository _world;
        private SpatialHashGrid _grid;

        public RaycastSolverSystemTests()
        {
            _world = PhysicsTestWorldFactory.Create();
            _grid  = PhysicsTestWorldFactory.CreateTestGrid();
        }

        public void Dispose()
        {
            _grid.Dispose();
            PhysicsTestWorldFactory.DisposeBatch(_world);
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates an entity at <paramref name="pos2D"/> (Z=0) with a
        /// <see cref="PhysicsCollider"/> and a <see cref="SimTransform"/>,
        /// adds it to the grid, then publishes the grid as the
        /// <see cref="SpatialGridData"/> singleton on the world.
        /// </summary>
        private Entity SpawnCollider(Vector2 pos2D, float radius, int layer)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform
            {
                Position = new Vector3(pos2D.X, pos2D.Y, 0f),
                Rotation = Quaternion.Identity,
            });
            _world.AddComponent(entity, new SimVelocity());
            _world.AddComponent(entity, new PhysicsCollider { Radius = radius, CollisionLayer = layer });

            // Add to spatial grid so broadphase can find it.
            _grid.Add(entity, pos2D);

            // Publish updated grid as singleton.
            _world.SetSingleton(new SpatialGridData { Grid = _grid });

            return entity;
        }

        /// <summary>
        /// Publishes a <see cref="RaycastRequestEvent"/>, runs the full solver pipeline,
        /// and returns the <see cref="RaycastHit"/> from the ring buffer slot for this request.
        /// </summary>
        private RaycastHit RunSolver(RaycastRequestEvent req)
        {
            var view = (ISimulationView)_world;

            // Publish request to the write buffer.
            _world.Bus.Publish(req);

            // Swap: requests are now in the read buffer for the solver.
            _world.Bus.SwapBuffers();

            // Solve: reads events, writes results to cmd buffer.
            var sys = new RaycastSolverSystem();
            sys.Execute(view, 0.016f);

            // Playback: result events move from cmd buffer to the bus write buffer.
            var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
            ecb.Playback(_world);

            // Swap: result events are now in the read buffer for materialization.
            _world.Bus.SwapBuffers();

            // Materialize: writes results into the RaycastBatchData ring buffer.
            new RaycastResultMaterializationSystem().Execute(view, 0.016f);

            // Return the ring buffer slot for this request.
            int slot = (int)((uint)req.RayId % (uint)PhysicsConstants.RaycastBatchCapacity);
            return _world.GetSingleton<RaycastBatchData>().Hits[slot];
        }

        // ── Test 1 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// A ray that crosses an entity's bounding circle must produce a hit result
        /// with <c>HasHit == 1</c> and <c>HitEntity</c> equal to the spawned entity.
        /// </summary>
        [Fact]
        public void RaycastSolver_DetectsHit_WhenBulletPathCrossesCollider()
        {
            // Arrange: entity at (5,0), radius 1, layer bit 0.
            var entity = SpawnCollider(new Vector2(5f, 0f), radius: 1f, layer: 1);

            var hit = RunSolver(new RaycastRequestEvent
            {
                Start     = new Vector3(-5f, 0f, 0f),
                End       = new Vector3(10f, 0f, 0f),
                RayId     = PhysicsConstants.PackBulletRayId(99),
                LayerMask = 1,
            });

            Assert.Equal(1, hit.HasHit);
            Assert.Equal(entity, hit.HitEntity);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// With no entities in the world, every ray must return <c>HasHit == 0</c>.
        /// </summary>
        [Fact]
        public void RaycastSolver_ReturnsNoHit_WhenNoEntitiesInPath()
        {
            // No entities -- just publish an empty grid singleton.
            _world.SetSingleton(new SpatialGridData { Grid = _grid });

            var hit = RunSolver(new RaycastRequestEvent
            {
                Start     = new Vector3(-5f, 0f, 0f),
                End       = new Vector3( 5f, 0f, 0f),
                RayId     = PhysicsConstants.PackBulletRayId(0),
                LayerMask = 1,
            });

            Assert.Equal(0, hit.HasHit);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// An entity on <c>CollisionLayer = 2</c> (bit 1) must not be hit by a ray with
        /// <c>LayerMask = 1</c> (bit 0) because the bitmask AND is zero.
        /// </summary>
        [Fact]
        public void RaycastSolver_RespectsLayerMask()
        {
            // Entity on layer 2 (bit 1). Ray uses mask 1 (bit 0) -- no shared bits.
            SpawnCollider(new Vector2(5f, 0f), radius: 1f, layer: 2);

            var hit = RunSolver(new RaycastRequestEvent
            {
                Start     = new Vector3(-5f, 0f, 0f),
                End       = new Vector3(10f, 0f, 0f),
                RayId     = PhysicsConstants.PackBulletRayId(1),
                LayerMask = 1,  // bit 0 -- does not overlap with CollisionLayer=2 (bit 1)
            });

            Assert.Equal(0, hit.HasHit);
        }

        // ── Test 4 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Setting <c>IgnoreEntity</c> to the spawned entity must cause the
        /// solver to skip that entity (no hit), simulating a shooter ignoring itself.
        /// </summary>
        [Fact]
        public void RaycastSolver_IgnoresIgnoreEntity()
        {
            var entity = SpawnCollider(new Vector2(5f, 0f), radius: 1f, layer: 1);

            var hit = RunSolver(new RaycastRequestEvent
            {
                Start        = new Vector3(-5f, 0f, 0f),
                End          = new Vector3(10f, 0f, 0f),
                RayId        = PhysicsConstants.PackBulletRayId(2),
                LayerMask    = 1,
                IgnoreEntity = entity,  // exclude the only entity in the world
            });

            Assert.Equal(0, hit.HasHit);
        }

        // ── Test 5 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// When two entities lie along the ray, the solver must return the one closest
        /// to the ray origin (smallest t).
        /// </summary>
        [Fact]
        public void RaycastSolver_ReturnsClosestHit_WhenMultipleInPath()
        {
            // Two entities along the X-axis; near one at x=3, far one at x=7.
            var nearEntity = SpawnCollider(new Vector2(3f, 0f), radius: 0.5f, layer: 1);
            var farEntity  = SpawnCollider(new Vector2(7f, 0f), radius: 0.5f, layer: 1);

            var hit = RunSolver(new RaycastRequestEvent
            {
                Start     = new Vector3(0f, 0f, 0f),
                End       = new Vector3(10f, 0f, 0f),
                RayId     = PhysicsConstants.PackBulletRayId(3),
                LayerMask = 1,
            });

            Assert.Equal(1, hit.HasHit);
            // Closest entity (at x=3) must be selected, not the farther one (x=7).
            Assert.Equal(nearEntity, hit.HitEntity);
            Assert.NotEqual(farEntity, hit.HitEntity);
        }

        // ── Test 6: SourceNodeId propagation ─────────────────────────────────────

        /// <summary>
        /// Verifies that <see cref="RaycastSolverSystem"/> copies
        /// <see cref="RaycastRequestEvent.SourceNodeId"/> verbatim into the corresponding
        /// <see cref="RaycastHit.SourceNodeId"/>.
        /// </summary>
        [Fact]
        public void RaycastSolver_PropagatesSourceNodeId_ToHit()
        {
            // Arrange: entity at (5,0) so we get a real hit; SourceNodeId = 7.
            SpawnCollider(new Vector2(5f, 0f), radius: 1f, layer: 1);

            var hit = RunSolver(new RaycastRequestEvent
            {
                Start        = new Vector3(-5f, 0f, 0f),
                End          = new Vector3(10f, 0f, 0f),
                RayId        = PhysicsConstants.PackBulletRayId(1),
                LayerMask    = 1,
                SourceNodeId = 7,
            });

            Assert.Equal(7, hit.SourceNodeId);
        }

        /// <summary>
        /// SourceNodeId is propagated even on a miss (HasHit == 0), so the
        /// solver-egress translator can route the empty hit record back to the correct Brain.
        /// </summary>
        [Fact]
        public void RaycastSolver_PropagatesSourceNodeId_OnMiss()
        {
            // No entities -- ray will miss.
            _world.SetSingleton(new SpatialGridData { Grid = _grid });

            var hit = RunSolver(new RaycastRequestEvent
            {
                Start        = new Vector3(-5f, 0f, 0f),
                End          = new Vector3(5f, 0f, 0f),
                RayId        = PhysicsConstants.PackBulletRayId(2),
                LayerMask    = 1,
                SourceNodeId = 42,
            });

            Assert.Equal(0, hit.HasHit);
            Assert.Equal(42, hit.SourceNodeId);
        }
    }
}

