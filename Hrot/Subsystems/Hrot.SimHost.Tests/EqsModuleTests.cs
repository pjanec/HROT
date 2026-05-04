using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.IG.Components;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="AreaQuerySolverSystem"/> and <see cref="EqsModule"/>
    /// (TASK-HA002).
    /// </summary>
    public class EqsModuleTests : IDisposable
    {
        private readonly EntityRepository _world;
        private SpatialHashGrid _grid;

        public EqsModuleTests()
        {
            _world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(_world);

            // Build a small test grid (100 x 100 metres, 5 m cells).
            _grid = SpatialHashGrid.Create(100, 100, 5f, 1000, Allocator.Persistent);
            _grid.Clear();
            _world.SetSingleton(new SpatialGridData { Grid = _grid });
        }

        public void Dispose()
        {
            DisposeEqsSingletons(_world);
            _grid.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private Entity CreateAreaEntity(IList<Vector2> polygon)
        {
            var entity = _world.CreateEntity();
            var ecb = (Fdp.Core.EntityCommandBuffer)((ISimulationView)_world).GetCommandBuffer();
            ecb.AddManagedComponent(entity, new EditablePolyline
            {
                Points  = new List<Vector2>(polygon),
                Version = 1,
            });
            ecb.Playback(_world);
            return entity;
        }

        private Entity CreateEnemyAt(Vector2 pos)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform
            {
                Position = new Vector3(pos.X, pos.Y, 0f),
                Rotation = Quaternion.Identity,
            });
            _world.AddComponent(entity, new EntityInfo
            {
                ForceId = ForceId.Hostile,
            });
            _grid.Add(entity, pos);
            _world.SetSingleton(new SpatialGridData { Grid = _grid });
            return entity;
        }

        private static void DisposeEqsSingletons(EntityRepository world)
        {
            if (world.HasSingleton<AreaQueryBatchData>())
            {
                ref var b = ref world.GetSingleton<AreaQueryBatchData>();
                if (b.Requests.IsCreated) b.Requests.Dispose();
                if (b.Results.IsCreated)  b.Results.Dispose();
            }
            if (world.HasSingleton<EqsTargetPool>())
            {
                var p = world.GetSingleton<EqsTargetPool>();
                if (p.Targets.IsCreated) p.Targets.Dispose();
            }
        }

        // ── SC-HA002-3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// The solver must return early without modifying any state when the batch
        /// contains zero requests.
        /// </summary>
        [Fact]
        public void Solver_DoesNothing_WhenNoPendingRequests()
        {
            // Arrange — fresh world, batch.Count == 0 (default from registry)
            var solver = new AreaQuerySolverSystem();

            ref var batch = ref _world.GetSingleton<AreaQueryBatchData>();
            int countBefore = batch.Count;
            Assert.Equal(0, countBefore);

            // Act
            solver.Execute(_world, 0.1f);

            // Assert — count unchanged
            Assert.Equal(0, _world.GetSingleton<AreaQueryBatchData>().Count);
        }

        // ── SC-HA002-1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When a request targets a polygon with no hostile entities inside, the solver
        /// must mark the result <c>IsReady == true</c> with <c>TargetCount == 0</c>.
        /// </summary>
        [Fact]
        public void Solver_SetsIsReadyTrue_WhenNoViableTargetsFound()
        {
            // Arrange — create a small square polygon with no entities inside
            var polygon = new List<Vector2>
            {
                new(10f, 10f), new(20f, 10f), new(20f, 20f), new(10f, 20f),
            };
            var areaEntity      = CreateAreaEntity(polygon);
            var requestingEntity = _world.CreateEntity();

            long requestId = AreaQueryBatchHelper.RequestAreaQuery(
                _world, requestingEntity, areaEntity, ForceId.Hostile);
            Assert.True(requestId >= 0);

            // Act
            var solver = new AreaQuerySolverSystem();
            solver.Execute(_world, 0.016f);

            // Assert
            var result = AreaQueryBatchHelper.GetAreaQueryResult(_world, requestId);
            Assert.True(result.IsReady);
            Assert.Equal(0, result.TargetCount);
        }

        // ── SC-HA002-2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// The solver must include entities within the polygon and exclude entities outside.
        /// </summary>
        [Fact]
        public void Solver_FindsEntitiesInsidePolygon()
        {
            // Arrange — 30x30 m polygon centred at (50,50)
            var polygon = new List<Vector2>
            {
                new(35f, 35f), new(65f, 35f), new(65f, 65f), new(35f, 65f),
            };
            var areaEntity       = CreateAreaEntity(polygon);
            var requestingEntity = _world.CreateEntity();

            // One hostile INSIDE, one hostile OUTSIDE, one friendly INSIDE
            var inside1  = CreateEnemyAt(new Vector2(50f, 50f)); // hostile, inside  -> should appear
            var outside1 = CreateEnemyAt(new Vector2(80f, 80f)); // hostile, outside -> must NOT appear

            var friendlyInside = _world.CreateEntity();
            _world.AddComponent(friendlyInside, new SimTransform
            {
                Position = new Vector3(45f, 45f, 0f),
                Rotation = Quaternion.Identity,
            });
            _world.AddComponent(friendlyInside, new EntityInfo { ForceId = ForceId.Friend });
            _grid.Add(friendlyInside, new Vector2(45f, 45f));
            _world.SetSingleton(new SpatialGridData { Grid = _grid });

            long requestId = AreaQueryBatchHelper.RequestAreaQuery(
                _world, requestingEntity, areaEntity, ForceId.Hostile);

            // Act
            var solver = new AreaQuerySolverSystem();
            solver.Execute(_world, 0.016f);

            // Assert
            var result = AreaQueryBatchHelper.GetAreaQueryResult(_world, requestId);
            Assert.True(result.IsReady, "Result must be marked ready");
            Assert.Equal(1, result.TargetCount);

            long storedHandle = AreaQueryBatchHelper.GetTargetFromPool(
                _world, result.TargetGroupHandle, 0);
            Assert.Equal((long)inside1.PackedValue, storedHandle);
        }

        // ── SC-HA002-4 ────────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="EqsModule.Policy"/> must return <see cref="ExecutionPolicy.SlowBackground"/>
        /// with a 10 Hz tick rate.
        /// </summary>
        [Fact]
        public void EqsModule_Policy_IsSlowBackground10Hz()
        {
            var module = new EqsModule();
            var policy = module.Policy;

            Assert.Equal(RunMode.Asynchronous, policy.Mode);
            Assert.Equal(10, policy.TargetFrequencyHz);
        }
    }
}
