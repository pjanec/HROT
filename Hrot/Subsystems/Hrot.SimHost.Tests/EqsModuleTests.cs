using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Providers;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.IG.Components;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="EqsModule"/> and the EQS area template (TASK-HA002, re-homed onto EQS when
    /// the AreaQuery pipeline was retired — EQS design §18).
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


        private static void DisposeEqsSingletons(EntityRepository world)
        {
            if (world.HasSingleton<EqsResultPool>())
            {
                var r = world.GetSingleton<EqsResultPool>();
                if (r.Results.IsCreated) r.Results.Dispose();
            }
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
            using var module = new CognitiveSpatialModule();
            var policy = module.Policy;

            Assert.Equal(RunMode.Asynchronous, policy.Mode);
            Assert.Equal(DataStrategy.SoD, policy.Strategy);
            Assert.Equal(10, policy.TargetFrequencyHz);
        }

        // ── The area query inside EQS 1.3 (design EQS §17) ─────────────────────────

        /// <summary>
        /// The template's baked id is the canonical hash of its AssetId — the id a blueprint
        /// <c>SpawnEqsSensor</c> node bakes — so blueprint and C# callers reach the same template.
        /// </summary>
        [Fact]
        public void EntitiesOfForceInArea_BlueprintId_IsTheCanonicalHashOfItsAssetId()
        {
            Assert.Equal(
                EqsTemplateRegistry.BlueprintIdOf(new Guid(EntitiesOfForceInArea.AssetId)),
                EntitiesOfForceInArea.BlueprintId);
        }

        /// <summary>
        /// CE-465 red-proof: the production install finds the [EqsTemplate] classes, so a sensor gets a
        /// real template instead of the empty-result stub.
        /// </summary>
        [Fact]
        public void InstallDefault_RegistersTheAreaTemplate_AndKeepsAnExistingRegistry()
        {
            var registry = EqsTemplateRegistry.InstallDefault(_world);

            Assert.True(registry.TryGetTemplate(EntitiesOfForceInArea.BlueprintId, out var t));
            Assert.IsType<EntitiesInAreaGenerator>(t.Generator);
            Assert.True(registry.TryGetTemplate(FindCoverFromTarget.BlueprintId, out _),
                "a hand-typed template id must keep resolving for its C# callers");
            Assert.Same(registry, EqsTemplateRegistry.InstallDefault(_world));
        }

        /// <summary>
        /// ⭐ The area template on one world: hostile + inside + not wrecked, over a polygon whose origin is
        /// OFFSET — covers the relative-to-origin polygon, force, wreck and outside cases at once.
        /// ⚠ Was a parity rail against the old AreaQuery solver (both answered this set) until the pipeline
        /// was retired — EQS design §18.
        /// </summary>
        [Fact]
        public void AreaTemplate_ReportsTheLiveHostilesInside()
        {
            var area = CreateAreaEntity(new List<Vector2>
            {
                new(-15f, -15f), new(15f, -15f), new(15f, 15f), new(-15f, 15f),
            });
            _world.GetComponentRW<SimTransform>(area).Position = new Vector3(50f, 50f, 0f); // offset origin

            var inside1  = CreateEnemyAt(new Vector2(50f, 50f));
            var inside2  = CreateEnemyAt(new Vector2(60f, 40f));
            CreateEnemyAt(new Vector2(80f, 80f));                       // outside
            var wreck    = CreateEnemyAt(new Vector2(45f, 55f));
            _world.AddComponent(wreck, new Health { Current = 0f, Max = 100f });
            var friendly = CreateEnemyAt(new Vector2(55f, 55f));
            _world.GetComponentRW<EntityInfo>(friendly).ForceId = ForceId.Friend;

            var sensor = RunEqsAreaSensor(area);

            Assert.Equal(new HashSet<long> { (long)inside1.PackedValue, (long)inside2.PackedValue }, BufferEntities(sensor));
        }

        /// <summary>
        /// Re-homed SC-HA002-5: polygon vertices are LOCAL to the area's <see cref="SimTransform"/>. A hostile
        /// inside the world-space polygon (local vertex + origin) is found; one at the raw local coordinates
        /// (ignoring the origin) is not.
        /// </summary>
        [Fact]
        public void AreaTemplate_PolygonIsLocalToTheAreaOrigin()
        {
            var area = CreateAreaEntity(new List<Vector2>
            {
                new(0f, 0f), new(10f, 0f), new(10f, 10f), new(0f, 10f),
            });
            _world.GetComponentRW<SimTransform>(area).Position = new Vector3(20f, 20f, 0f);

            var inside = CreateEnemyAt(new Vector2(25f, 25f));   // local (5, 5)
            CreateEnemyAt(new Vector2(5f, 5f));                   // local (-15, -15)

            var sensor = RunEqsAreaSensor(area);

            Assert.Equal(new HashSet<long> { (long)inside.PackedValue }, BufferEntities(sensor));
        }

        /// <summary>
        /// Re-homed SC-HA002-1: an area with a polygon and no hostile inside ⇒ a READY, EMPTY answer —
        /// "the area is clear" is a real answer once the area exists (contrast <c>AreaTemplate_WithNoArea_PublishesNothing</c>).
        /// </summary>
        [Fact]
        public void AreaTemplate_WithNoTargetInside_PublishesAReadyEmptyAnswer()
        {
            var area = CreateAreaEntity(new List<Vector2>
            {
                new(10f, 10f), new(20f, 10f), new(20f, 20f), new(10f, 20f),
            });
            CreateEnemyAt(new Vector2(50f, 50f));                 // outside

            var sensor = RunEqsAreaSensor(area);

            ref readonly var buffer = ref _world.GetComponentRO<EqsCognitiveBuffer>(sensor);
            Assert.True(buffer.IsReady);
            Assert.Equal(0, buffer.Count);
        }

        /// <summary>
        /// "No area ⇒ no answer": when the area entity is not (yet) present, the EQS solver publishes
        /// NOTHING — the buffer stays not-ready — instead of an empty result that reads as "area clear".
        /// </summary>
        [Fact]
        public void AreaTemplate_WithNoArea_PublishesNothing()
        {
            CreateEnemyAt(new Vector2(50f, 50f));
            var sensor = RunEqsAreaSensor(Entity.Null);
            Assert.False(_world.GetComponentRO<EqsCognitiveBuffer>(sensor).IsReady);
        }

        // ── EqsChildSensor — the ONE Brain-side child-sensor lifecycle (DESIGN_Hill_Attack_Eqs_Migration.md §4 D1–D3) ──

        /// <summary>Ensure creates through the command buffer (Null on the creating call — an ECB handle is a placeholder),
        /// with PartMetadata (parent + InstanceId), the sensor and an empty buffer; after playback it is FOUND, not re-created.</summary>
        [Fact]
        public void EqsChildSensor_Ensure_CreatesOnce_ThenFinds()
        {
            var parent = _world.CreateEntity();
            var cfg = EntitiesOfForceInArea.SensorFor(Entity.Null, ForceId.Hostile);
            var view = (ISimulationView)_world;

            Assert.True(EqsChildSensor.Ensure(view, parent, 7, cfg).IsNull);
            ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);

            var child = EqsChildSensor.Find(view, parent, 7);
            Assert.False(child.IsNull);
            Assert.Equal(parent, _world.GetComponentRO<Fdp.Toolkit.Replication.Components.PartMetadata>(child).ParentEntity);
            Assert.Equal(7, _world.GetComponentRO<Fdp.Toolkit.Replication.Components.PartMetadata>(child).InstanceId);
            Assert.Equal(cfg.BlueprintId, _world.GetComponentRO<EqsSensor>(child).BlueprintId);
            Assert.False(_world.GetComponentRO<EqsCognitiveBuffer>(child).IsReady);

            Assert.Equal(child, EqsChildSensor.Ensure(view, parent, 7, cfg));   // found — no second sensor
            Assert.True(EqsChildSensor.Find(view, parent, 8).IsNull);           // another InstanceId is another sensor
        }

        /// <summary>Refresh = a new epoch and a cleared buffer, so the solver's answer for the OLD epoch is dropped and
        /// IsReady turns true only on an answer computed after the refresh (EqsResultUpdateSystem's epoch filter).</summary>
        [Fact]
        public void EqsChildSensor_Refresh_DropsTheOldEpochsAnswer()
        {
            EqsTemplateRegistry.InstallDefault(_world);
            var area = CreateAreaEntity(new List<Vector2> { new(0f, 0f), new(100f, 0f), new(100f, 100f), new(0f, 100f) });
            CreateEnemyAt(new Vector2(50f, 50f));
            var sensor = RunEqsAreaSensor(area);
            Assert.Equal(1, _world.GetComponentRO<EqsCognitiveBuffer>(sensor).Count);

            // the solver answers epoch 1 ...
            var view = (ISimulationView)_world;
            new EqsSolverSystem().Execute(view, 0.1f);
            ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
            // ... but the sensor is refreshed before that answer is applied
            Assert.True(EqsChildSensor.Refresh(view, sensor));
            Assert.Equal(2u, _world.GetComponentRO<EqsSensor>(sensor).Epoch);
            _world.Bus.SwapBuffers();
            new EqsResultUpdateSystem().Execute(view, 0.1f);
            Assert.False(_world.GetComponentRO<EqsCognitiveBuffer>(sensor).IsReady);   // the epoch-1 answer did not count

            Assert.False(EqsChildSensor.Refresh(view, Entity.Null));
        }

        /// <summary>Destroy removes the sensor; a null or dead handle is a no-op.</summary>
        [Fact]
        public void EqsChildSensor_Destroy_RemovesIt_AndIgnoresNull()
        {
            var sensor = _world.CreateEntity();
            _world.AddComponent(sensor, EntitiesOfForceInArea.SensorFor(Entity.Null, ForceId.Hostile));
            var view = (ISimulationView)_world;
            EqsChildSensor.Destroy(view, Entity.Null);
            EqsChildSensor.Destroy(view, sensor);
            ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
            Assert.False(_world.IsAlive(sensor));
            EqsChildSensor.Destroy(view, sensor);   // dead ⇒ no-op
        }

        // Spawns a local EQS area sensor, runs the solver once and applies its result (Path B).
        private Entity RunEqsAreaSensor(Entity area)
        {
            EqsTemplateRegistry.InstallDefault(_world);
            var sensor = _world.CreateEntity();
            _world.AddComponent(sensor, EntitiesOfForceInArea.SensorFor(area, ForceId.Hostile));
            _world.AddComponent(sensor, new EqsCognitiveBuffer());

            var view = (ISimulationView)_world;
            new EqsSolverSystem().Execute(view, 0.1f);
            ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
            _world.Bus.SwapBuffers();
            new EqsResultUpdateSystem().Execute(view, 0.1f);
            return sensor;
        }

        private HashSet<long> BufferEntities(Entity sensor)
        {
            var set = new HashSet<long>();
            ref readonly var buf = ref _world.GetComponentRO<EqsCognitiveBuffer>(sensor);
            Assert.True(buf.IsReady, "the EQS buffer must be ready after one solver pass");
            var span = buf.GetSpanRO();
            for (int i = 0; i < buf.Count; i++) set.Add(span[i].EntityId);
            return set;
        }
    }
}


