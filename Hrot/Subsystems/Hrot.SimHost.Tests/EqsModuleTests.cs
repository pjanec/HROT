using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Providers;
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

        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private Entity CreateAreaEntity(IList<Vector2> polygon)
        {
            var entity = _world.CreateEntity();
            // Polygon vertices are relative to the area entity's SimTransform position.
            // Place the origin at (0,0,0) so local space equals world space in these tests.
            _world.AddComponent(entity, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
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

        // Runs the full event pipeline: swap (requests now readable) -> solve -> playback
        // -> swap (results now readable) -> materialize.
        private void RunSolverPipeline(float dt = 0.016f)
        {
            var view = (ISimulationView)_world;
            _world.Bus.SwapBuffers();
            var solver = new AreaQuerySolverSystem();
            solver.Execute(view, dt);
            var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
            ecb.Playback(_world);
            _world.Bus.SwapBuffers();
            new AreaQueryResultMaterializationSystem().Execute(view, dt);
        }

        private static void DisposeEqsSingletons(EntityRepository world)
        {
            if (world.HasSingleton<AreaQueryBatchData>())
            {
                ref var b = ref world.GetSingleton<AreaQueryBatchData>();
                if (b.Results.IsCreated)  b.Results.Dispose();
            }
            if (world.HasSingleton<EqsTargetPool>())
            {
                var p = world.GetSingleton<EqsTargetPool>();
                if (p.Targets.IsCreated) p.Targets.Dispose();
            }
            if (world.HasSingleton<EqsResultPool>())
            {
                var r = world.GetSingleton<EqsResultPool>();
                if (r.Results.IsCreated) r.Results.Dispose();
            }
        }

        // â”€â”€ SC-HA002-3 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// When no AreaQueryRequestEvent has been published, running the full pipeline
        /// must leave all result slots in their default (not-ready) state.
        /// </summary>
        [Fact]
        public void Solver_DoesNothing_WhenNoPendingRequests()
        {
            // Act â€” pipeline with no events published
            RunSolverPipeline();

            // Assert â€” no result slot should have IsReady set
            ref readonly var batch = ref _world.GetSingleton<AreaQueryBatchData>();
            Assert.False(batch.Results[0].IsReady);
        }

        // â”€â”€ SC-HA002-1 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// When a request targets a polygon with no hostile entities inside, the solver
        /// must mark the result <c>IsReady == true</c> with <c>TargetCount == 0</c>.
        /// </summary>
        [Fact]
        public void Solver_SetsIsReadyTrue_WhenNoViableTargetsFound()
        {
            // Arrange â€” create a small square polygon with no entities inside
            var polygon = new List<Vector2>
            {
                new(10f, 10f), new(20f, 10f), new(20f, 20f), new(10f, 20f),
            };
            var areaEntity       = CreateAreaEntity(polygon);
            var requestingEntity = _world.CreateEntity();

            long requestId = AreaQueryBatchHelper.RequestAreaQuery(
                _world, requestingEntity, areaEntity, ForceId.Hostile);
            Assert.True(requestId >= 0, "RequestAreaQuery must succeed (returns slot index 0..63, or -1 when full)");

            // Act
            RunSolverPipeline();

            // Assert
            var result = AreaQueryBatchHelper.GetAreaQueryResult(_world, requestId);
            Assert.True(result.IsReady);
            Assert.Equal(0, result.TargetCount);
        }

        // â”€â”€ SC-HA002-2 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// The solver must include entities within the polygon and exclude entities outside.
        /// </summary>
        [Fact]
        public void Solver_FindsEntitiesInsidePolygon()
        {
            // Arrange â€” 30x30 m polygon centred at (50,50)
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
            RunSolverPipeline();

            // Assert
            var result = AreaQueryBatchHelper.GetAreaQueryResult(_world, requestId);
            Assert.True(result.IsReady, "Result must be marked ready");
            Assert.Equal(1, result.TargetCount);

            long storedHandle = AreaQueryBatchHelper.GetTargetFromPool(
                _world, result.TargetGroupHandle, 0);
            Assert.Equal((long)inside1.PackedValue, storedHandle);
        }

        // â”€â”€ SC-HA002-4 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        // ── SC-HA002-5 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Polygon vertices are stored in local space relative to the area entity's
        /// <see cref="SimTransform"/>.  An entity inside the world-space polygon
        /// (local vertex + area origin) must be found; an entity at the raw local
        /// vertex coordinates (ignoring the origin offset) must be excluded.
        /// </summary>
        [Fact]
        public void Solver_RespectsSimTransformOffset_PolygonIsLocalToAreaOrigin()
        {
            // Area entity at world position (20, 20).
            // Local polygon: (0,0)-(10,0)-(10,10)-(0,10).
            // World-space polygon: (20,20)-(30,20)-(30,30)-(20,30).
            var areaEntity = _world.CreateEntity();
            _world.AddComponent(areaEntity, new SimTransform
            {
                Position = new Vector3(20f, 20f, 0f),
                Rotation = Quaternion.Identity,
            });
            var ecb = (Fdp.Core.EntityCommandBuffer)((ISimulationView)_world).GetCommandBuffer();
            ecb.AddManagedComponent(areaEntity, new EditablePolyline
            {
                Points = new List<Vector2>
                {
                    new(0f, 0f), new(10f, 0f), new(10f, 10f), new(0f, 10f),
                },
                Version = 1,
            });
            ecb.Playback(_world);

            var requestingEntity = _world.CreateEntity();

            // Hostile at world (25, 25) = local (5, 5): inside the polygon.
            var inside = CreateEnemyAt(new Vector2(25f, 25f));
            // Hostile at world (5, 5) = local (-15, -15): outside (raw local coords, no offset).
            CreateEnemyAt(new Vector2(5f, 5f));

            long requestId = AreaQueryBatchHelper.RequestAreaQuery(
                _world, requestingEntity, areaEntity, ForceId.Hostile);

            RunSolverPipeline();

            var result = AreaQueryBatchHelper.GetAreaQueryResult(_world, requestId);
            Assert.True(result.IsReady, "Result must be ready");
            Assert.Equal(1, result.TargetCount);
            long stored = AreaQueryBatchHelper.GetTargetFromPool(_world, result.TargetGroupHandle, 0);
            Assert.Equal((long)inside.PackedValue, stored);
        }

        // ── SC-HA002-6 ────────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="CognitiveSpatialModule.Policy"/> must return
        /// <see cref="ExecutionPolicy.SlowBackground"/> at 10 Hz.
        /// Both <c>CognitiveSpatialModule</c> and <c>NavigationSolverModule</c> run
        /// at 10 Hz SoD and share a <see cref="SharedSnapshotProvider"/>.  Event
        /// delivery to all convoy members is guaranteed by the provider's
        /// <c>FlushToReplica</c> call — no frequency offset hack is needed.
        /// </summary>
        [Fact]
        public void CognitiveSpatialModule_Policy_IsSlowBackground10Hz()
        {
            using var module = new CognitiveSpatialModule(new EntityRepository());
            var policy = module.Policy;

            Assert.Equal(RunMode.Asynchronous, policy.Mode);
            Assert.Equal(DataStrategy.SoD, policy.Strategy);
            Assert.Equal(10, policy.TargetFrequencyHz);
        }

        // ── SC-HA002-7 ────────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="CognitiveSpatialModule"/> resolves an area query when the
        /// snapshot passed to its <c>Tick</c> method contains entity positions (via
        /// <see cref="SimTransform"/>) and the area polygon coordinates (via
        /// <see cref="EditablePolyline"/> + <see cref="SimTransform"/>).
        ///
        /// <para>Verifies the full per-module pipeline: <c>LocalGridBuilderSystem</c>
        /// rebuilds the private spatial hash grid from entity SimTransforms in the
        /// snapshot; <see cref="AreaQuerySolverSystem"/> queries that grid against
        /// the polygon; the result is materialized into
        /// <see cref="AreaQueryBatchData"/>.</para>
        /// </summary>
        [Fact]
        public void CognitiveSpatialModule_ResolvesAreaQuery_WhenSnapshotHasEntityPositionsAndAreaCoordinates()
        {
            // Arrange: area polygon at world origin; 30x30 m square.
            var areaEntity = _world.CreateEntity();
            _world.AddComponent(areaEntity, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            var ecb = (Fdp.Core.EntityCommandBuffer)((ISimulationView)_world).GetCommandBuffer();
            ecb.AddManagedComponent(areaEntity, new EditablePolyline
            {
                Points = new List<Vector2>
                {
                    new(35f, 35f), new(65f, 35f), new(65f, 65f), new(35f, 65f),
                },
                Version = 1,
            });
            ecb.Playback(_world);

            var inside = CreateEnemyAt(new Vector2(50f, 50f));  // inside polygon
            CreateEnemyAt(new Vector2(80f, 80f));               // outside polygon

            var requestingEntity = _world.CreateEntity();
            long requestId = AreaQueryBatchHelper.RequestAreaQuery(
                _world, requestingEntity, areaEntity, ForceId.Hostile);

            // Pass _world as liveWorld so the injected AreaQuerySolverSystem can access
            // EqsTargetPool and write result events via ECB.
            using var module = new CognitiveSpatialModule(_world);
            module.RegisterSystems(new CapturingRegistry());

            // Swap so the module Tick can read the queued request event.
            _world.Bus.SwapBuffers();

            // Tick: LocalGridBuilderSystem rebuilds the private grid from entity SimTransforms
            // in the snapshot; AreaQuerySolverSystem queries that grid and publishes results.
            module.Tick(_world, 0.1f);

            // Playback the ECB to publish result events to the bus write buffer.
            ((Fdp.Core.EntityCommandBuffer)((ISimulationView)_world).GetCommandBuffer())
                .Playback(_world);

            // Swap so the materialization system can read the result events.
            _world.Bus.SwapBuffers();
            new AreaQueryResultMaterializationSystem().Execute(_world, 0.1f);

            var result = AreaQueryBatchHelper.GetAreaQueryResult(_world, requestId);
            Assert.True(result.IsReady, "EQS result must be ready after CognitiveSpatialModule tick");
            Assert.Equal(1, result.TargetCount);
            long stored = AreaQueryBatchHelper.GetTargetFromPool(_world, result.TargetGroupHandle, 0);
            Assert.Equal((long)inside.PackedValue, stored);
        }

        // ── SC-HA002-8 ────────────────────────────────────────────────────────────

        /// <summary>
        /// Reproduces the real Hill-Attack execution path end-to-end through the
        /// <see cref="SharedSnapshotProvider"/> convoy.
        ///
        /// <para>
        /// In production, both <c>CognitiveSpatialModule</c> (10 Hz SoD) and
        /// <c>NavigationSolverModule</c> (10 Hz SoD) are grouped by the kernel into
        /// a single <see cref="SharedSnapshotProvider"/>.  The first module to call
        /// <see cref="SharedSnapshotProvider.AcquireView"/> creates the snapshot and
        /// advances <c>_lastSeenTick</c>; the second reuses the same snapshot.
        /// Both modules hold the snapshot simultaneously until they both release.
        /// </para>
        ///
        /// <para>
        /// The previous implementation called <c>Bus.SwapBuffers()</c> after
        /// <c>FlushToReplica</c>, which moved injected events from the READ buffer to
        /// the WRITE buffer and immediately cleared them — making the bus appear
        /// empty to every convoy member.  This test verifies the fix: events flushed
        /// into the READ buffer survive and are visible to all convoy members.
        /// </para>
        ///
        /// <para>Entity coordinates are exact values from the Hill-Attack scenario:
        /// area entity at (670, 473.5), two hostile infantry units at (668, 427)
        /// and (668, 522), both geometrically inside the area polygon.</para>
        /// </summary>
        [Fact]
        public void CognitiveSpatialModule_ResolvesAreaQuery_ThroughSharedSnapshotProvider_ConvoyWithNavigationSolver()
        {
            // --- Arrange: exact Hill-Attack scenario coordinates ---

            // Area entity at world (670, 473.5, 0).
            var areaEntity = _world.CreateEntity();
            _world.AddComponent(areaEntity, new SimTransform
            {
                Position = new Vector3(670f, 473.5f, 0f),
                Rotation = Quaternion.Identity,
            });
            {
                var ecb = (EntityCommandBuffer)((ISimulationView)_world).GetCommandBuffer();
                ecb.AddManagedComponent(areaEntity, new EditablePolyline
                {
                    Points = new List<Vector2>
                    {
                        new(-53f, -88.5f), new(-54f, 90.5f),
                        new( 51f,  85.5f), new( 56f, -87.5f),
                    },
                    Version = 1,
                });
                ecb.Playback(_world);
            }

            // Hostile 1: local (-2, -46.5) => inside polygon.
            var hostile1 = _world.CreateEntity();
            _world.AddComponent(hostile1, new SimTransform
            {
                Position = new Vector3(668f, 427f, 0f),
                Rotation = Quaternion.Identity,
            });
            _world.AddComponent(hostile1, new EntityInfo { ForceId = ForceId.Hostile });

            // Hostile 2: local (-2, 48.5) => inside polygon.
            var hostile2 = _world.CreateEntity();
            _world.AddComponent(hostile2, new SimTransform
            {
                Position = new Vector3(668f, 522f, 0f),
                Rotation = Quaternion.Identity,
            });
            _world.AddComponent(hostile2, new EntityInfo { ForceId = ForceId.Hostile });

            var requester = _world.CreateEntity();
            long requestId = AreaQueryBatchHelper.RequestAreaQuery(
                _world, requester, areaEntity, ForceId.Hostile);

            // --- Simulate ModuleHostKernel.CaptureFrame ---
            // Kernel swaps the live bus so published events enter the READ buffer,
            // then CaptureFrame snapshots that READ buffer into the accumulator.
            _world.Bus.SwapBuffers();
            var accumulator = new EventAccumulator();
            accumulator.CaptureFrame(_world.Bus, _world.GlobalVersion);

            // --- Build SharedSnapshotProvider (mirrors kernel convoy setup) ---
            // The schema setup pre-registers typed NativeEventStream<T> entries,
            // exactly as SimHostComponentRegistry.RegisterAll does on every pool repo.
            // Without pre-registration InjectIntoCurrentBySize falls back to the
            // UntypedNativeEventStream path whose Swap() is a no-op, masking the bug.
            Action<EntityRepository> schemaSetup = repo =>
            {
                repo.RegisterEvent<AreaQueryRequestEvent>();
                repo.RegisterEvent<AreaQueryResultEvent>();
            };
            var pool = new SnapshotPool(schemaSetup, warmupCount: 0);
            // Use the full snapshotable mask — same as CalculateUnionMask for the convoy.
            var unionMask512 = _world.GetSnapshotableMask();
            var provider = new SharedSnapshotProvider(_world, accumulator, unionMask512, pool);

            // --- Simulate convoy: NavigationSolverModule acquires the view FIRST ---
            // This is the race the old 11Hz hack worked around.
            // AcquireView creates the shared snapshot, flushes events, and advances
            // _lastSeenTick.  With the bug present (SwapBuffers after FlushToReplica)
            // those events would be cleared before any module can read them.
            var navView = provider.AcquireView();   // _activeReaders = 1

            // CognitiveSpatialModule acquires the SAME snapshot concurrently.
            // In production both modules hold views simultaneously on background threads;
            // in this test we serialize the acquisition for determinism.
            var cogView = provider.AcquireView();   // _activeReaders = 2, same snapshot

            // Both acquisitions must return the identical EntityRepository.
            Assert.Same(navView, cogView);

            // NavSolver ticks (no EQS interest — we just verify it doesn't disturb events).
            // cogView == navView so the snapshot is shared; we tick with the same object.
            using var module = new CognitiveSpatialModule(_world);
            module.RegisterSystems(new CapturingRegistry());
            module.Tick(cogView, 1f / 10f);

            // Simulate ModuleHostKernel.HarvestEntry: play the snapshot ECB back to
            // the live world so the AreaQueryResultEvent lands in _world's WRITE buffer.
            ((EntityCommandBuffer)((ISimulationView)cogView).GetCommandBuffer())
                .Playback(_world);

            // Release both convoy members (order does not matter).
            provider.ReleaseView(navView);   // _activeReaders = 1
            provider.ReleaseView(cogView);   // _activeReaders = 0, snapshot returned to pool

            // --- Next kernel frame: swap + materialize ---
            _world.Bus.SwapBuffers();
            new AreaQueryResultMaterializationSystem().Execute(_world, 1f / 10f);

            // --- Assert ---
            var result = AreaQueryBatchHelper.GetAreaQueryResult(_world, requestId);
            Assert.True(result.IsReady,
                "EQS result must be ready after CognitiveSpatialModule tick through the " +
                "SharedSnapshotProvider convoy path (hill-attack scenario coordinates).");
            Assert.Equal(2, result.TargetCount);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Minimal <see cref="ISystemRegistry"/> that returns each system unchanged so that
        /// <see cref="CognitiveSpatialModule.RegisterSystems"/> can initialise its internal
        /// system references without requiring the full <c>ModuleHostKernel</c>.
        /// </summary>
        private sealed class CapturingRegistry : ISystemRegistry
        {
            public void RegisterSystem<T>(T system) where T : IEcsModuleSystem { }
            public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem
                => system;
        }
    }
}


