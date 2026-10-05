using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Providers;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Perception.Translators;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Interfaces;
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
            using var module = new EqsModule();   // it owns a perception grid since CE-3038
            var policy = module.Policy;

            Assert.Equal(RunMode.Asynchronous, policy.Mode);
            Assert.Equal(10, policy.TargetFrequencyHz);
        }

        // ── SC-HA002-6 — ⛔ CognitiveSpatialModule_Policy_IsSlowBackground10Hz deleted with the module (CE-3038): vision runs
        //    in EqsModule, whose policy the test above pins. ──────────────────────────────────────────────────────────

        [Fact]
        public void EntitiesOfForceInArea_BlueprintId_IsTheCanonicalHashOfItsAssetId()
        {
            Assert.Equal(
                EqsTemplateRegistry.BlueprintIdOf(new Guid(EntitiesOfForceInArea.AssetId)),
                EntitiesOfForceInArea.BlueprintId);
        }

        /// <summary>
        /// ⭐ CE-2034 — the starter template's id is the canonical hash too (it was a hand-typed constant that matched no
        /// hash), so a C# caller, a blueprint <c>SpawnEqsSensor</c> and the generated registrar all name one id.
        /// </summary>
        [Fact]
        public void CE2034_FindCoverFromTarget_BlueprintId_IsTheCanonicalHashOfItsAssetId()
        {
            Assert.Equal(
                EqsTemplateRegistry.BlueprintIdOf(new Guid(FindCoverFromTarget.AssetId)),
                FindCoverFromTarget.BlueprintId);
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
                "the starter template resolves by its canonical id");
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

        private Entity BehaviourParent(uint runInstanceId)
        {
            // SimHostComponentRegistry registers no BehaviorState (the cognitive registry does) — register it for these rails.
            if (!_world.TryGetTable(typeof(Fdp.Toolkit.Behavior.Components.BehaviorState), out _))
                _world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();
            var parent = _world.CreateEntity();
            _world.AddComponent(parent, new Fdp.Toolkit.Behavior.Components.BehaviorState { InstanceId = runInstanceId });
            return parent;
        }

        private int PartIdOf(Entity child)
            => _world.GetComponentRO<Fdp.Toolkit.Replication.Components.PartMetadata>(child).InstanceId;

        /// <summary>⭐ CE-485 — on the live world Ensure creates the child AT ONCE (no placeholder), stamped with the owning run
        /// and its site, with part id 1 (the lowest free) and the run in the epoch's high 16 bits; a second call FINDS it.</summary>
        [Fact]
        public void EqsChildSensor_Ensure_CreatesImmediately_Stamped_ThenFinds()
        {
            var parent = BehaviourParent(5);
            var cfg = EntitiesOfForceInArea.SensorFor(Entity.Null, ForceId.Hostile);
            var view = (ISimulationView)_world;

            var child = EqsChildSensor.Ensure(view, parent, 7, cfg);
            Assert.False(child.IsNull);
            Assert.True(_world.IsAlive(child));
            Assert.Equal(parent, _world.GetComponentRO<Fdp.Toolkit.Replication.Components.PartMetadata>(child).ParentEntity);
            Assert.Equal(1, PartIdOf(child));                                         // allocated, not the site id
            var stamp = _world.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorOwnedPart>(child);
            Assert.Equal(5u, stamp.OwnerInstanceId);
            Assert.Equal(7, stamp.SiteId);
            Assert.Equal((5u << 16) | (cfg.Epoch & 0xFFFFu), _world.GetComponentRO<EqsSensor>(child).Epoch);
            Assert.Equal(cfg.BlueprintId, _world.GetComponentRO<EqsSensor>(child).BlueprintId);
            Assert.False(_world.GetComponentRO<EqsCognitiveBuffer>(child).IsReady);

            Assert.Equal(child, EqsChildSensor.Ensure(view, parent, 7, cfg));      // found — no second sensor
            Assert.Equal(child, EqsChildSensor.Find(view, parent, 7));
            Assert.True(EqsChildSensor.Find(view, parent, 8).IsNull);              // another site is another sensor
        }

        /// <summary>⭐ CE-485 — two creations in ONE frame get DIFFERENT part ids (the second sees the first); a key separates
        /// two sensors of one site; a freed id is REUSED by the next creation.</summary>
        [Fact]
        public void EqsChildSensor_AllocatesLowestFreePartId_SameFrame_Keys_AndReuse()
        {
            var parent = BehaviourParent(3);
            var cfg = EntitiesOfForceInArea.SensorFor(Entity.Null, ForceId.Hostile);
            var view = (ISimulationView)_world;

            var a = EqsChildSensor.Ensure(view, parent, 10, cfg);
            var b = EqsChildSensor.Ensure(view, parent, 20, cfg);
            var c = EqsChildSensor.Ensure(view, parent, 20, cfg, key: 42);
            Assert.Equal(new[] { 1, 2, 3 }, new[] { PartIdOf(a), PartIdOf(b), PartIdOf(c) });
            Assert.NotEqual(b, c);

            _world.DestroyEntity(a);                                                  // part id 1 is free again
            var d = EqsChildSensor.Ensure(view, parent, 30, cfg);
            Assert.Equal(1, PartIdOf(d));

            var other = BehaviourParent(3);                                           // ids are per parent
            Assert.Equal(1, PartIdOf(EqsChildSensor.Ensure(view, other, 10, cfg)));
        }

        /// <summary>⭐ CE-485 — a sensor of an EARLIER run is never this run's sensor: after the run changes, Find misses it and
        /// Ensure creates a fresh one (with a fresh owner in the epoch).</summary>
        [Fact]
        public void EqsChildSensor_Find_IsScopedToTheCurrentRun()
        {
            var parent = BehaviourParent(1);
            var cfg = EntitiesOfForceInArea.SensorFor(Entity.Null, ForceId.Hostile);
            var view = (ISimulationView)_world;
            var first = EqsChildSensor.Ensure(view, parent, 7, cfg);

            _world.GetComponentRW<Fdp.Toolkit.Behavior.Components.BehaviorState>(parent).InstanceId = 2;
            Assert.True(EqsChildSensor.Find(view, parent, 7).IsNull);
            var second = EqsChildSensor.Ensure(view, parent, 7, cfg);
            Assert.NotEqual(first, second);
            Assert.Equal(2u, _world.GetComponentRO<EqsSensor>(second).Epoch >> 16);
        }

        /// <summary>⭐ CE-485 — Release destroys exactly the ending run's parts of that parent: not another run's, not another
        /// parent's.</summary>
        [Fact]
        public void BehaviorOwnedParts_Release_DestroysOnlyTheEndingRunsParts()
        {
            var parent = BehaviourParent(4);
            var cfg = EntitiesOfForceInArea.SensorFor(Entity.Null, ForceId.Hostile);
            var view = (ISimulationView)_world;
            var mine1 = EqsChildSensor.Ensure(view, parent, 1, cfg);
            var mine2 = EqsChildSensor.Ensure(view, parent, 2, cfg);
            var otherParent = BehaviourParent(4);
            var theirs = EqsChildSensor.Ensure(view, otherParent, 1, cfg);
            _world.GetComponentRW<Fdp.Toolkit.Behavior.Components.BehaviorState>(parent).InstanceId = 5;
            var nextRun = EqsChildSensor.Ensure(view, parent, 1, cfg);

            Assert.Equal(2, Fdp.Toolkit.Behavior.Components.BehaviorOwnedParts.Release(_world, parent, 4));
            Assert.False(_world.IsAlive(mine1));
            Assert.False(_world.IsAlive(mine2));
            Assert.True(_world.IsAlive(theirs));
            Assert.True(_world.IsAlive(nextRun));
            Assert.Equal(0, Fdp.Toolkit.Behavior.Components.BehaviorOwnedParts.Release(_world, parent, 0));   // 0 = unowned
        }

        /// <summary>⭐ CE-485 — Refresh counts in the epoch's LOW 16 bits only; the owner in the high 16 never changes.</summary>
        [Fact]
        public void EqsChildSensor_Refresh_KeepsTheOwnerBits()
        {
            var parent = BehaviourParent(9);
            var view = (ISimulationView)_world;
            var child = EqsChildSensor.Ensure(view, parent, 1, EntitiesOfForceInArea.SensorFor(Entity.Null, ForceId.Hostile));
            uint before = _world.GetComponentRO<EqsSensor>(child).Epoch;
            Assert.True(EqsChildSensor.Refresh(view, child));
            uint after = _world.GetComponentRO<EqsSensor>(child).Epoch;
            Assert.Equal(before >> 16, after >> 16);
            Assert.Equal((before & 0xFFFFu) + 1u, after & 0xFFFFu);
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

        /// <summary>
        /// ⭐⭐ CE-3056 — two QUERY sensors asking the identical question (same unit, template, area, force) are solved ONCE:
        /// the second copies the first's answer this tick and both buffers hold it. A sensor over a DIFFERENT area is solved on
        /// its own. 📄 DESIGN_Sensors_And_Doctrine.md §5.6.
        /// </summary>
        [Fact]
        public void CE3056_IdenticalQueries_AreSolvedOnce_AndBothOwnersGetTheAnswer()
        {
            EqsTemplateRegistry.InstallDefault(_world);
            var areaA = CreateAreaEntity(new List<Vector2> { new(-15f, -15f), new(15f, -15f), new(15f, 15f), new(-15f, 15f) });
            _world.GetComponentRW<SimTransform>(areaA).Position = new Vector3(50f, 50f, 0f);
            var areaB = CreateAreaEntity(new List<Vector2> { new(-5f, -5f), new(5f, -5f), new(5f, 5f), new(-5f, 5f) });
            _world.GetComponentRW<SimTransform>(areaB).Position = new Vector3(10f, 10f, 0f);
            var inside = CreateEnemyAt(new Vector2(50f, 50f));

            // The unit the queries are FOR (context slot 0 — a placed self, as a real unit's query sensor has).
            var unit = _world.CreateEntity();
            _world.AddComponent(unit, new SimTransform { Position = new Vector3(20f, 20f, 0f), Rotation = Quaternion.Identity });

            Entity Sensor(Entity area)
            {
                var e = _world.CreateEntity();
                var cfg = EntitiesOfForceInArea.SensorFor(area, ForceId.Hostile);
                cfg.ContextSlot0 = unit;
                _world.AddComponent(e, cfg);
                _world.AddComponent(e, new EqsCognitiveBuffer());
                return e;
            }
            var owner1 = Sensor(areaA);
            var owner2 = Sensor(areaA);   // the twin
            var other  = Sensor(areaB);

            var solver = new EqsSolverSystem();
            var view = (ISimulationView)_world;
            solver.Execute(view, 0.1f);
            ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
            _world.Bus.SwapBuffers();
            new EqsResultUpdateSystem().Execute(view, 0.1f);

            Assert.Equal(1, solver.LastSharedCopies);                       // exactly the twin copied
            var expected = new HashSet<long> { (long)inside.PackedValue };
            Assert.Equal(expected, BufferEntities(owner1));
            Assert.Equal(expected, BufferEntities(owner2));                 // the copy reached its OWN buffer
            Assert.True(_world.GetComponentRO<EqsCognitiveBuffer>(other).IsReady);
            Assert.Empty(BufferEntities(other));                            // solved on its own: nothing in area B
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
        // ── CE-3036 (S3) — a NEW sensor kind, in tests only (R-185 I ①) ─────────────────────────────────────────────

        private static readonly Guid TestRadarTemplate = new("5e2c0d19-0000-4000-8000-0000000c3036");

        /// <summary>A test-only sensing filter: keeps candidates within the sensor's CAPABILITY range (not its query radius)
        /// — it proves the per-kind parameters reach the solver on the carrier.</summary>
        private sealed class CapabilityRangeTest : IEqsTest
        {
            public EqsTestPhase Phase => EqsTestPhase.FilterCheap;
            public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
            {
                float range = view.GetManagedComponentRO<SensorCapability>(observer).Current.Range;
                EqsContext.SelfPosition(view, observer, sensor, out var self);
                for (int i = 0; i < candidates.Length; i++)
                {
                    ref var c = ref candidates[i];
                    if (c.EntityId == -1L) continue;
                    if (Vector2.Distance(new Vector2(self.X, self.Y), new Vector2(c.PositionX, c.PositionY)) > range) c.EntityId = -1L;
                }
            }
        }

        private static SensorEntryDto TestRadar(float range) => new()
        {
            Kind = SensorModality.Radar, Template = TestRadarTemplate, SearchRadius = 400f,
            Radar = new RadarSensorDto { Range = range },
        };

        private void SolveOnce()
        {
            var view = (ISimulationView)_world;
            new EqsSolverSystem().Execute(view, 0.1f);
            ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
            _world.Bus.SwapBuffers();
            new EqsResultUpdateSystem().Execute(view, 0.1f);
        }

        /// <summary>
        /// ⭐⭐ CE-3036 — the whole S3 path on one node, with a sensor kind that exists only in this test: the unit's TKB lists a
        /// radar → the translator builds its child (part 1000) → the REAL solver runs the template, whose filter reads the
        /// sensor's capability on the carrier → the answer reaches the buffer <see cref="UnitSensors"/> finds by kind.
        /// Then an override (a longer range) changes the answer, switching it off stops it, and clearing the override
        /// restores the TKB default. 📄 docs/DESIGN_Sensors_And_Doctrine.md §5.1, §5.2.
        /// </summary>
        [Fact]
        public void ATkbSensorOfANewKind_IsBuilt_Solved_Read_Overridden_Switched_AndRestored_CE3036()
        {
            var registry = (EqsTemplateRegistry)EqsTemplateRegistry.InstallDefault(_world);
            registry.Register(TestRadarTemplate, new EqsQueryTemplate
            {
                BlueprintId   = EqsTemplateRegistry.BlueprintIdOf(TestRadarTemplate),
                Generator     = new EntitiesInRadiusGenerator(),
                FilterCheap   = new IEqsTest[] { new CapabilityRangeTest() },
                ScoreCheap    = new IEqsTest[] { new DistanceScoreTest() },
                MaxCandidates = 64,
            }, "TestRadar");

            var unit = _world.CreateEntity();
            _world.AddComponent(unit, new SimTransform { Position = new Vector3(10f, 10f, 0f), Rotation = Quaternion.Identity });
            _world.AddComponent(unit, new EntityInfo { ForceId = ForceId.Friend });
            _world.AddComponent(unit, new NetworkIdentity { Value = 4242 });   // a spawned unit has one — the sensor's wire key
            _grid.Add(unit, new Vector2(10f, 10f));
            CreateEnemyAt(new Vector2(60f, 10f));    //  50 m
            CreateEnemyAt(new Vector2(160f, 10f));   // 150 m
            CreateEnemyAt(new Vector2(260f, 10f));   // 250 m

            var template = new TkbTemplate("RadarUnit", 77);
            template.AddDescriptor(new SensorCapabilitiesDto { Sensors = new List<SensorEntryDto> { TestRadar(100f) } });
            new PerceptionTkbTranslator().Inject(_world, unit, template);

            var sensor = UnitSensors.Of(_world, unit, SensorModality.Radar);
            Assert.False(sensor.IsNull);
            Assert.Equal(1000, _world.GetComponentRO<PartMetadata>(sensor).InstanceId);

            SolveOnce();
            Assert.True(UnitSensors.TryGetResults(_world, unit, SensorModality.Radar, out var r1));
            Assert.Equal(1, r1.Count);                       // capability range 100 m, query radius 400 m

            UnitSensors.Configure(_world, sensor, TestRadar(200f));
            SolveOnce();
            Assert.True(UnitSensors.TryGetResults(_world, unit, SensorModality.Radar, out var r2));
            Assert.Equal(2, r2.Count);                       // the override's range is the one in force

            UnitSensors.SetEnabled(_world, sensor, false);
            int before = _world.GetComponentRO<EqsCognitiveBuffer>(sensor).Count;
            SolveOnce();
            Assert.True(_world.GetComponentRO<EqsSensor>(sensor).Suspended);   // an off sensor is skipped by the solver

            UnitSensors.ClearOverride(_world, sensor);
            SolveOnce();
            Assert.True(UnitSensors.TryGetResults(_world, unit, SensorModality.Radar, out var r3));
            Assert.Equal(1, r3.Count);                       // back to the TKB default: 100 m, ON
            _ = before;
        }

        // ── CE-3037 (S4) — counted-work budget + the memory stage ──────────────────────────────────────────────────────

        /// <summary>Generates <c>n</c> positional candidates — a sensor whose cost is exactly <c>EqsCost.Sensor + n</c> units
        /// (light = 110, heavy = 1100).</summary>
        private sealed class FixedCostGenerator : IEqsGenerator
        {
            private readonly int _n;
            public FixedCostGenerator(int n) => _n = n;
            public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
            {
                for (int i = 0; i < _n; i++) candidates[i] = new EqsResult { EntityId = 0L, PositionX = i, Score = 1f };
                return _n;
            }
        }

        private const uint Light = 0x5401u, Heavy = 0x5402u;

        private static EntityRepository ScheduleWorld()
        {
            var w = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(w);
            var reg = new SimpleTemplates();
            reg.Add(new EqsQueryTemplate { BlueprintId = Light, Generator = new FixedCostGenerator(10), MaxCandidates = 16 });
            reg.Add(new EqsQueryTemplate { BlueprintId = Heavy, Generator = new FixedCostGenerator(1000), MaxCandidates = 1024 });
            w.SetSingletonManaged<IEqsTemplateRegistry>(reg);
            return w;
        }

        private sealed class SimpleTemplates : IEqsTemplateRegistry
        {
            private readonly Dictionary<uint, EqsQueryTemplate> _t = new();
            public void Add(EqsQueryTemplate t) => _t[t.BlueprintId] = t;
            public bool TryGetTemplate(uint id, out EqsQueryTemplate t) => _t.TryGetValue(id, out t!);
        }

        private static Entity LocalSensor(EntityRepository w, uint template, EqsPriorityBand band = EqsPriorityBand.Normal)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new EqsSensor { BlueprintId = template, Epoch = 1, SearchRadius = 10f, Priority = (byte)band });
            w.AddComponent(e, new EqsCognitiveBuffer());
            return e;
        }

        // One solver tick on w; returns the run order as entity indices.
        private static List<int> Tick(EntityRepository w, EqsSolverSystem solver)
        {
            var view = (ISimulationView)w;
            solver.Execute(view, 0.1f);
            ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(w);
            w.Bus.SwapBuffers();
            w.Tick();
            var order = new List<int>();
            foreach (var e in solver.LastSchedule) order.Add(e.Index);
            return order;
        }

        /// <summary>
        /// ⭐⭐ CE-3037 — the schedule is DETERMINISTIC: the same sensors scheduled twice give the same run order, tick by
        /// tick, and every sensor gets its turn (oldest first). 🔴 Before: wall-clock slicing ran whatever fitted in 4 ms —
        /// a different set on every run and every machine. 📄 DESIGN_Sensors_And_Doctrine.md §5.3–§5.4.
        /// </summary>
        [Fact]
        public void S4_TheSchedule_IsTheSameOnEveryRun_AndEverySensorGetsItsTurn()
        {
            List<List<int>> Run()
            {
                var w = ScheduleWorld();
                try
                {
                    for (int i = 0; i < 6; i++) LocalSensor(w, Light);
                    var solver = new EqsSolverSystem { BudgetUnits = 275 };
                    var ticks = new List<List<int>>();
                    for (int t = 0; t < 6; t++) ticks.Add(Tick(w, solver));
                    return ticks;
                }
                finally { DisposeEqsSingletons(w); }
            }

            var a = Run();
            var b = Run();
            Assert.Equal(a.Count, b.Count);
            for (int t = 0; t < a.Count; t++) Assert.Equal(a[t], b[t]);

            var ran = new HashSet<int>();
            for (int t = 1; t < 4; t++) foreach (int e in a[t]) ran.Add(e);   // after the first tick, within three ticks
            Assert.Equal(6, ran.Count);
        }

        /// <summary>
        /// ⭐ CE-3037 — the bands share the budget (Critical 50 %, Normal 35 %, the rest Low, unused slack rolling down): a
        /// tick runs the oldest of EACH band, so a busy Critical band never starves Low. The first sensor of a band always
        /// starts.
        /// </summary>
        [Fact]
        public void S4_EveryBand_RunsEveryTick_CriticalFirst_ThenTheOldest()
        {
            var w = ScheduleWorld();
            try
            {
                var low1  = LocalSensor(w, Light, EqsPriorityBand.Low);
                var low2  = LocalSensor(w, Light, EqsPriorityBand.Low);
                var norm1 = LocalSensor(w, Light);
                var norm2 = LocalSensor(w, Light);
                var crit1 = LocalSensor(w, Light, EqsPriorityBand.Critical);
                var crit2 = LocalSensor(w, Light, EqsPriorityBand.Critical);
                var solver = new EqsSolverSystem { BudgetUnits = 330 };

                Tick(w, solver);                                   // warm-up: no estimates yet
                var t2 = Tick(w, solver);
                var t3 = Tick(w, solver);
                Assert.Equal(3, t2.Count);
                Assert.Equal(3, t3.Count);
                Assert.Equal(new[] { crit1.Index, crit2.Index }, Sorted(t2[0], t3[0]));   // Critical first, alternating
                Assert.Equal(new[] { norm1.Index, norm2.Index }, Sorted(t2[1], t3[1]));
                Assert.Equal(new[] { low1.Index, low2.Index }, Sorted(t2[2], t3[2]));     // Low is never starved
            }
            finally { DisposeEqsSingletons(w); }

            static int[] Sorted(int a, int b) => a < b ? new[] { a, b } : new[] { b, a };
        }

        /// <summary>
        /// ⭐ CE-3037 — a sensor heavier than what is left waits one tick, is then the oldest, and runs FIRST — alone when it
        /// costs the whole budget. It is never skipped for good.
        /// </summary>
        [Fact]
        public void S4_AHeavySensor_WaitsATick_ThenRunsAlone()
        {
            var w = ScheduleWorld();
            try
            {
                var light1 = LocalSensor(w, Light);
                var light2 = LocalSensor(w, Light);
                var heavy  = LocalSensor(w, Heavy);
                var solver = new EqsSolverSystem { BudgetUnits = 330 };

                Tick(w, solver);                                   // warm-up: everyone runs once, costs are learnt
                var t2 = Tick(w, solver);
                Assert.Equal(new[] { light1.Index, light2.Index }, t2);   // the heavy one does not fit after them
                var t3 = Tick(w, solver);
                Assert.Equal(new[] { heavy.Index }, t3);                  // oldest ⇒ first ⇒ alone
                Assert.Equal(1100, solver.LastSpentUnits);
                Assert.Equal(1100, w.GetComponentRO<SensorEvalState>(heavy).LastCost);
            }
            finally { DisposeEqsSingletons(w); }
        }

        /// <summary>
        /// ⭐⭐ CE-3037 — the MEMORY STAGE: a perception sensor's sightings become its unit's acquired / lost transitions
        /// (the same hysteresis as the visual chain), and a unit's track is lost only when NO sensor of the unit still holds
        /// it. 📄 DESIGN_Sensors_And_Doctrine.md §5.4.
        /// </summary>
        [Fact]
        public void S4_TheMemoryStage_ReportsTheUnitsUnion_AcquiredThenLost()
        {
            var registry = (EqsTemplateRegistry)EqsTemplateRegistry.InstallDefault(_world);
            registry.Register(TestRadarTemplate, new EqsQueryTemplate
            {
                BlueprintId   = EqsTemplateRegistry.BlueprintIdOf(TestRadarTemplate),
                Generator     = new EntitiesInRadiusGenerator(),
                FilterCheap   = new IEqsTest[] { new CapabilityRangeTest() },
                MaxCandidates = 64,
            }, "TestRadar");

            var unit = _world.CreateEntity();
            _world.AddComponent(unit, new SimTransform { Position = new Vector3(10f, 10f, 0f), Rotation = Quaternion.Identity });
            _world.AddComponent(unit, new EntityInfo { ForceId = ForceId.Friend });
            _world.AddComponent(unit, new NetworkIdentity { Value = 4243 });
            _grid.Add(unit, new Vector2(10f, 10f));
            var enemy = CreateEnemyAt(new Vector2(60f, 10f));   // 50 m

            var template = new TkbTemplate("TwoRadars", 78);
            template.AddDescriptor(new SensorCapabilitiesDto { Sensors = new List<SensorEntryDto> { TestRadar(100f), TestRadar(100f) } });
            new PerceptionTkbTranslator().Inject(_world, unit, template);
            var near = SensorChildFactory.Find(_world, unit, SensorChildFactory.FirstTkbPartId);
            var far  = SensorChildFactory.Find(_world, unit, SensorChildFactory.FirstTkbPartId + 1);

            var solver = new EqsSolverSystem();
            var acquired = new List<SensorTrackStateEvent>();
            var lost     = new List<SensorTrackStateEvent>();
            void Step()
            {
                var view = (ISimulationView)_world;
                solver.Execute(view, 0.1f);
                ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
                _world.Bus.SwapBuffers();
                foreach (var e in view.ReadEvents<SensorTrackStateEvent>())
                    (e.State == Fdp.Toolkit.Perception.Events.SensorTrackStatus.Acquired ? acquired : lost).Add(e);
                _world.Tick();
            }

            Step();
            Assert.Equal(0, solver.LastSharedCopies);                // ⭐ CE-3056: perception sensors never share (own capability)
            var one = Assert.Single(acquired);                       // two sensors see it ⇒ ONE unit-level Acquired
            Assert.Equal(unit, one.Observer);
            Assert.Equal(enemy, one.Target);

            // One sensor loses it (its range shrinks); the other still holds it ⇒ nothing on the wire.
            UnitSensors.Configure(_world, near, TestRadar(10f));
            for (int i = 0; i < 30; i++) Step();
            Assert.Empty(lost);

            // The other loses it too ⇒ ONE Lost, after the hysteresis window.
            UnitSensors.Configure(_world, far, TestRadar(10f));
            for (int i = 0; i < (int)Fdp.Toolkit.Perception.Systems.ContactHysteresis.TrackLostThresholdTicks; i++) Step();
            Assert.Empty(lost);                                      // not before the window
            for (int i = 0; i < 3; i++) Step();
            var gone = Assert.Single(lost);
            Assert.Equal(enemy, gone.Target);
            Assert.Single(acquired);
            Assert.Equal(SensorModality.Radar, acquired[0].Modality);   // ⭐ CE-3060 — the kind rides the track
        }

        private static SensorEntryDto TestThermalKind(float range) => new()
        {
            Kind = SensorModality.Thermal, Template = TestRadarTemplate, SearchRadius = 400f,
            Thermal = new ThermalSensorDto { Range = range },
        };

        /// <summary>
        /// ⭐ CE-3060 — the unit's track carries the KINDS of the sensors holding it (OR), and a change of kinds on a target
        /// still held is re-published as an Acquired (the Brain's track learns it; no Lost). 🔴 Before: no kind on the track,
        /// and ThreatEvaluationSystem stamped every contact Visual. 📄 DESIGN_Thermal_And_Acoustic_Sensing.md §6 G.
        /// </summary>
        [Fact]
        public void S7_TheTrackCarriesTheKindsHoldingIt_AndAKindChangeIsRepublished_CE3060()
        {
            var registry = (EqsTemplateRegistry)EqsTemplateRegistry.InstallDefault(_world);
            registry.Register(TestRadarTemplate, new EqsQueryTemplate
            {
                BlueprintId   = EqsTemplateRegistry.BlueprintIdOf(TestRadarTemplate),
                Generator     = new EntitiesInRadiusGenerator(),
                FilterCheap   = new IEqsTest[] { new CapabilityRangeTest() },
                MaxCandidates = 64,
            }, "TestRadar");

            var unit = _world.CreateEntity();
            _world.AddComponent(unit, new SimTransform { Position = new Vector3(10f, 10f, 0f), Rotation = Quaternion.Identity });
            _world.AddComponent(unit, new EntityInfo { ForceId = ForceId.Friend });
            _world.AddComponent(unit, new NetworkIdentity { Value = 4244 });
            _grid.Add(unit, new Vector2(10f, 10f));
            var enemy = CreateEnemyAt(new Vector2(60f, 10f));   // 50 m

            var template = new TkbTemplate("RadarAndThermal", 79);
            template.AddDescriptor(new SensorCapabilitiesDto { Sensors = new List<SensorEntryDto> { TestRadar(100f), TestThermalKind(10f) } });
            new PerceptionTkbTranslator().Inject(_world, unit, template);
            var radar   = SensorChildFactory.Find(_world, unit, SensorChildFactory.FirstTkbPartId);
            var thermal = SensorChildFactory.Find(_world, unit, SensorChildFactory.FirstTkbPartId + 1);

            var solver = new EqsSolverSystem();
            var acquired = new List<SensorTrackStateEvent>();
            var lost     = new List<SensorTrackStateEvent>();
            void Step()
            {
                var view = (ISimulationView)_world;
                solver.Execute(view, 0.1f);
                ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
                _world.Bus.SwapBuffers();
                foreach (var e in view.ReadEvents<SensorTrackStateEvent>())
                    (e.State == Fdp.Toolkit.Perception.Events.SensorTrackStatus.Acquired ? acquired : lost).Add(e);
                _world.Tick();
            }

            Step();
            Assert.Equal(SensorModality.Radar, Assert.Single(acquired).Modality);   // only the radar reaches 50 m

            UnitSensors.Configure(_world, thermal, TestThermalKind(100f));           // now both hold it
            for (int i = 0; i < 5; i++) Step();
            Assert.Equal(2, acquired.Count);
            Assert.Equal(SensorModality.Radar | SensorModality.Thermal, acquired[1].Modality);

            UnitSensors.Configure(_world, radar, TestRadar(10f));                    // the radar drops it; thermal keeps it
            for (int i = 0; i < (int)Fdp.Toolkit.Perception.Systems.ContactHysteresis.TrackLostThresholdTicks + 3; i++) Step();
            Assert.Empty(lost);
            Assert.Equal(3, acquired.Count);
            Assert.Equal(SensorModality.Thermal, acquired[2].Modality);
        }

        /// <summary>
        /// ⭐ CE-3039 (S6) — a perception sensor's best answer changing is ONE <see cref="SensorChangedEvent"/> TopChanged for
        /// its unit, written by the system that writes the buffer; an unchanged answer is silent, and a query sensor (no
        /// <see cref="SensorTag"/>) never publishes one. 📄 DESIGN_Sensors_And_Doctrine.md §7.3.
        /// </summary>
        [Fact]
        public void S6_APerceptionSensorsTopChanging_IsOneTopChanged_AQuerySensorIsSilent()
        {
            var registry = (EqsTemplateRegistry)EqsTemplateRegistry.InstallDefault(_world);
            registry.Register(TestRadarTemplate, new EqsQueryTemplate
            {
                BlueprintId   = EqsTemplateRegistry.BlueprintIdOf(TestRadarTemplate),
                Generator     = new EntitiesInRadiusGenerator(),
                FilterCheap   = new IEqsTest[] { new CapabilityRangeTest() },
                MaxCandidates = 64,
            }, "TestRadar");

            var unit = _world.CreateEntity();
            _world.AddComponent(unit, new SimTransform { Position = new Vector3(10f, 10f, 0f), Rotation = Quaternion.Identity });
            _world.AddComponent(unit, new EntityInfo { ForceId = ForceId.Friend });
            _world.AddComponent(unit, new NetworkIdentity { Value = 4244 });
            _grid.Add(unit, new Vector2(10f, 10f));
            var enemy = CreateEnemyAt(new Vector2(60f, 10f));   // 50 m

            var template = new TkbTemplate("OneRadar", 79);
            template.AddDescriptor(new SensorCapabilitiesDto { Sensors = new List<SensorEntryDto> { TestRadar(100f) } });
            new PerceptionTkbTranslator().Inject(_world, unit, template);
            var radar = SensorChildFactory.Find(_world, unit, SensorChildFactory.FirstTkbPartId);

            // A query sensor over an area holding the same enemy: its answer changes too, but it is not perception.
            var area = CreateAreaEntity(new List<Vector2> { new(40f, -10f), new(80f, -10f), new(80f, 30f), new(40f, 30f) });
            var query = _world.CreateEntity();
            _world.AddComponent(query, EntitiesOfForceInArea.SensorFor(area, ForceId.Hostile));
            _world.AddComponent(query, new EqsCognitiveBuffer());

            var solver = new EqsSolverSystem();
            var update = new EqsResultUpdateSystem();
            var changes = new List<SensorChangedEvent>();
            void Step()
            {
                var view = (ISimulationView)_world;
                solver.Execute(view, 0.1f);
                ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
                _world.Bus.SwapBuffers();
                update.Execute(view, 0.1f);
                ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
                _world.Bus.SwapBuffers();
                foreach (var e in view.ReadEvents<SensorChangedEvent>()) changes.Add(e);
                _world.Tick();
            }

            Step();
            Assert.True(_world.GetComponentRO<EqsCognitiveBuffer>(query).IsReady, "the query sensor must have answered");
            var first = Assert.Single(changes);                      // the radar's first answer — the query sensor is silent
            Assert.Equal(SensorChange.TopChanged, first.What);
            Assert.Equal(unit, first.Unit);
            Assert.Equal(radar, first.Sensor);
            Assert.Equal(enemy, first.Target);

            changes.Clear();
            for (int i = 0; i < 10; i++) Step();
            Assert.Empty(changes);                                   // the same answer ⇒ no edge

            UnitSensors.Configure(_world, radar, TestRadar(10f));    // out of range ⇒ the top is gone
            for (int i = 0; i < 10 && changes.Count == 0; i++) Step();
            var gone = Assert.Single(changes);
            Assert.Equal(radar, gone.Sensor);
            Assert.True(gone.Target.IsNull);
        }

        // ── CE-3038 (S5) — vision as a sensor ──────────────────────────────────────────────────────────────────────────

        // A world of units (two forces, varied facing and field of view) and colliders; every unit built through the
        // TKB translator, so it has BOTH the receptor the old chain reads and the implicit visual sensor.
        private static (EntityRepository World, List<Entity> Units) VisionWorld(int seed)
        {
            var w = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(w);
            var rnd = new Random(seed);
            var units = new List<Entity>();
            var translator = new PerceptionTkbTranslator();
            for (int i = 0; i < 40; i++)
            {
                var e = w.CreateEntity();
                float yaw = (float)(rnd.NextDouble() * Math.PI * 2);
                w.AddComponent(e, new SimTransform
                {
                    Position = new Vector3((float)rnd.NextDouble() * 300f, (float)rnd.NextDouble() * 300f, 0f),
                    Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw),
                });
                w.AddComponent(e, new EntityInfo { ForceId = i % 2 == 0 ? ForceId.Friend : ForceId.Hostile });
                w.AddComponent(e, new NetworkIdentity { Value = 9000 + i });
                var t = new TkbTemplate("Unit" + i, 500 + i);
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = 80f + (i % 3) * 40f, FieldOfViewDegrees = (i % 3) switch { 0 => 360f, 1 => 120f, _ => 60f } });
                translator.Inject(w, e, t);
                units.Add(e);
            }
            for (int i = 0; i < 25; i++)   // blockers
            {
                var c = w.CreateEntity();
                w.AddComponent(c, new SimTransform { Position = new Vector3((float)rnd.NextDouble() * 300f, (float)rnd.NextDouble() * 300f, 0f), Rotation = Quaternion.Identity });
                w.AddComponent(c, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 4f });
            }
            return (w, units);
        }

        private static float ColliderRadius(ISimulationView v, Entity e)
            => v.HasComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>(e) ? v.GetComponentRO<Fdp.Toolkit.Physics.Components.PhysicsCollider>(e).Radius : 0f;

        /// <summary>
        /// ⭐⭐ CE-3038 — the PARITY proof: on one world, the visual sensor (EQS solver + memory stage) sees exactly the
        /// (observer, target) pairs a BRUTE-FORCE reference does — every pair, other force, within range, inside the cone,
        /// line of sight clear. ⭐ CE-3052: the reference used to be the old chain (VisionBroadphaseSystem →
        /// LosRequestBatchingSystem), retired once this proved parity; an all-pairs oracle is independent of the code
        /// under test. 📄 DESIGN_Sensors_And_Doctrine.md §5.5.
        /// </summary>
        [Fact]
        public void S5_TheVisualSensor_SeesExactlyWhatABruteForceReferenceSees()
        {
            var (w, units) = VisionWorld(seed: 3038);
            using var grid = new Fdp.Toolkit.Perception.Modules.PerceptionGridProvider();
            try
            {
                var view = (ISimulationView)w;
                var strategy = new Fdp.Toolkit.Perception.LineOfSight.PlanarCircleLosStrategy(ColliderRadius);
                new Fdp.Toolkit.Perception.Systems.LocalGridBuilderSystem(grid.Grid).Execute(view, 0.1f);

                // ── the reference: all pairs, the rule written out ──
                strategy.BeginBatch(view);
                int requests = 0;
                var chain = new HashSet<(int, long)>();
                foreach (var o in units)
                {
                    ref readonly var r = ref w.GetComponentRO<PerceptionReceptor>(o);
                    var ot = w.GetComponentRO<SimTransform>(o);
                    var fwd = Vector3.Transform(Vector3.UnitX, ot.Rotation);
                    var fwd2 = Vector2.Normalize(new Vector2(fwd.X, fwd.Y));
                    foreach (var t in units)
                    {
                        if (t == o || w.GetComponentRO<EntityInfo>(t).ForceId == w.GetComponentRO<EntityInfo>(o).ForceId) continue;
                        var d = new Vector2(w.GetComponentRO<SimTransform>(t).Position.X - ot.Position.X,
                                            w.GetComponentRO<SimTransform>(t).Position.Y - ot.Position.Y);
                        if (d.LengthSquared() > r.VisionRange * r.VisionRange) continue;
                        if (d.LengthSquared() > 0f && Vector2.Dot(fwd2, Vector2.Normalize(d)) < r.FieldOfViewCos) continue;
                        requests++;
                        if (strategy.IsVisible(view, o, t)) chain.Add((o.Index, (long)t.PackedValue));
                    }
                }

                // ── the visual sensor ──
                var registry = (EqsTemplateRegistry)EqsTemplateRegistry.InstallDefault(w);
                VisualPerception.Register(registry, grid.Grid, strategy);
                new EqsSolverSystem { BudgetUnits = int.MaxValue }.Execute(view, 0.1f);
                ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(w);
                var sensed = new HashSet<(int, long)>();
                foreach (var u in units)
                {
                    var s = UnitSensors.Of(w, u, SensorModality.Visual);
                    Assert.False(s.IsNull, "every unit that can see gets an implicit visual sensor");
                    if (!w.HasComponent<SensorContactList>(s)) continue;
                    var list = w.GetComponentRO<SensorContactList>(s);
                    unsafe { for (int i = 0; i < list.Count; i++) sensed.Add((u.Index, list.EntityIds[i])); }
                }

                Assert.True(chain.Count > 20, $"the scene must exercise sight (the reference saw {chain.Count} pairs)");
                Assert.True(requests > chain.Count, $"the scene must contain BLOCKED lines, or the sight test is not proven ({requests} candidates, {chain.Count} seen)");
                Assert.Equal(chain.OrderBy(p => p).ToList(), sensed.OrderBy(p => p).ToList());
            }
            finally { DisposeEqsSingletons(w); }
        }

        /// <summary>
        /// ⭐ CE-3061 — the THERMAL sensor sees a target only when its signature reaches the sensor's MinSignature: a cold
        /// target at rest is not seen; the same target, heated by running (ThermalHeatSystem), is. 📄
        /// DESIGN_Thermal_And_Acoustic_Sensing.md §5.2, §6 E–F.
        /// </summary>
        [Fact]
        public void S7_TheThermalSensor_SeesATargetOnlyOnceItIsHotEnough_CE3061()
        {
            using var grid = new Fdp.Toolkit.Perception.Modules.PerceptionGridProvider();
            var view = (ISimulationView)_world;
            var strategy = new Fdp.Toolkit.Perception.LineOfSight.PlanarCircleLosStrategy(ColliderRadius);
            var registry = (EqsTemplateRegistry)EqsTemplateRegistry.InstallDefault(_world);
            ThermalPerception.Register(registry, grid.Grid, strategy);

            var unit = _world.CreateEntity();
            _world.AddComponent(unit, new SimTransform { Position = new Vector3(10f, 10f, 0f), Rotation = Quaternion.Identity });
            _world.AddComponent(unit, new EntityInfo { ForceId = ForceId.Friend });
            _world.AddComponent(unit, new NetworkIdentity { Value = 4245 });   // a sensor child is solved only under a keyed unit
            var template = new TkbTemplate("ThermalOnly", 80);
            template.AddDescriptor(new SensorCapabilitiesDto { Sensors = new List<SensorEntryDto> { new()
            {
                Kind = SensorModality.Thermal, Template = ThermalPerception.AssetGuid, SearchRadius = 200f,
                Thermal = new ThermalSensorDto { Range = 100f, FieldOfViewDegrees = 360f, MinSignature = 0.5f },
            } } });
            new PerceptionTkbTranslator().Inject(_world, unit, template);
            var sensor = UnitSensors.Of(_world, unit, SensorModality.Thermal);
            Assert.False(sensor.IsNull);

            var enemy = CreateEnemyAt(new Vector2(60f, 10f));   // 50 m, in range and in sight
            _world.AddComponent(enemy, new Fdp.Toolkit.Perception.Signatures.ThermalState
            {
                BaseSignature = 0.3f, RunningHeatPerSecond = 0.5f, CooldownPerSecond = 0.05f, ReferenceSpeed = 5f,
            });

            var solver = new EqsSolverSystem { BudgetUnits = int.MaxValue };
            var heat   = new Fdp.Toolkit.Perception.Signatures.ThermalHeatSystem();
            unsafe bool Sensed()
            {
                if (!_world.HasComponent<SensorContactList>(sensor)) return false;
                var list = _world.GetComponentRO<SensorContactList>(sensor);
                for (int i = 0; i < list.Count; i++) if (list.EntityIds[i] == (long)enemy.PackedValue) return true;
                return false;
            }
            void Solve()
            {
                new Fdp.Toolkit.Perception.Systems.LocalGridBuilderSystem(grid.Grid).Execute(view, 0.1f);
                solver.Execute(view, 0.1f);
                ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
                _world.Bus.SwapBuffers();
                _world.Tick();
            }

            // ⚠ No finally/DisposeEqsSingletons here: this rail uses the fixture's _world, which Dispose() frees (a second free
            //   crashed the test host — "double free or corruption").
            {
                Solve();
                Assert.False(Sensed(), "a cold target (signature 0.3 < 0.5) is not seen");

                heat.Execute(view, 0.1f);                                         // first position sample
                for (int i = 0; i < 10; i++)                                      // 1 s running at 5 m/s, staying in range
                {
                    ref var t = ref _world.GetComponentRW<SimTransform>(enemy);
                    t.Position += new Vector3(0f, (i % 2 == 0) ? 0.5f : -0.5f, 0f);
                    heat.Execute(view, 0.1f);
                }
                Assert.True(_world.GetComponentRO<Fdp.Toolkit.Perception.Signatures.ThermalState>(enemy).Signature >= 0.5f);
                Solve();
                Assert.True(Sensed(), $"the running target is hot enough to be seen (signature {_world.GetComponentRO<Fdp.Toolkit.Perception.Signatures.ThermalState>(enemy).Signature}, " +
                    $"buffer {(_world.HasComponent<EqsCognitiveBuffer>(sensor) ? _world.GetComponentRO<EqsCognitiveBuffer>(sensor).Count : -1)}, " +
                    $"contacts {(_world.HasComponent<SensorContactList>(sensor) ? _world.GetComponentRO<SensorContactList>(sensor).Count : -1)}, " +
                    $"suspended {_world.GetComponentRO<EqsSensor>(sensor).Suspended}, last schedule {solver.LastSchedule.Count})");
            }
        }

        /// <summary>
        /// ⭐ CE-3062 — the ACOUSTIC sensor hears an enemy's sounds as ANONYMOUS estimates ("shot from the north", R-205): a
        /// SoundContactEvent for the unit, inside the uncertainty radius of the truth, of the right kind — and NO track (the
        /// source's identity never leaves the solver). A sound that does not carry that far is not heard. 📄
        /// DESIGN_Thermal_And_Acoustic_Sensing.md §5.1, §6 B–D.
        /// </summary>
        [Fact]
        public void S7_TheAcousticSensor_HearsAnonymousEstimates_NotIdentities_CE3062()
        {
            using var grid = new Fdp.Toolkit.Perception.Modules.PerceptionGridProvider();
            var view = (ISimulationView)_world;
            var registry = (EqsTemplateRegistry)EqsTemplateRegistry.InstallDefault(_world);
            AcousticPerception.Register(registry, grid.Grid);

            var unit = _world.CreateEntity();
            _world.AddComponent(unit, new SimTransform { Position = new Vector3(10f, 10f, 0f), Rotation = Quaternion.Identity });
            _world.AddComponent(unit, new EntityInfo { ForceId = ForceId.Friend });
            _world.AddComponent(unit, new NetworkIdentity { Value = 4246 });
            var template = new TkbTemplate("Ears", 81);
            template.AddDescriptor(new SensorCapabilitiesDto { Sensors = new List<SensorEntryDto> { new()
            {
                Kind = SensorModality.Acoustic, Template = AcousticPerception.AssetGuid, SearchRadius = 400f,
                Acoustic = new AcousticSensorDto { Range = 300f, UncertaintyPerMeter = 0.1f },
            } } });
            new PerceptionTkbTranslator().Inject(_world, unit, template);
            Assert.False(UnitSensors.Of(_world, unit, SensorModality.Acoustic).IsNull);

            var enemy = CreateEnemyAt(new Vector2(10f, 90f));   // 80 m north
            _world.AddComponent(enemy, new Fdp.Toolkit.Perception.Signatures.AcousticEmitter
            {
                MovingAudibleRange = 150f, FiringAudibleRange = 60f,   // movement carries 150 m, its shots only 60 m
                CurrentMovingRange = 150f, ShotTimeLeft = 0.3f, ShotX = 10f, ShotY = 90f,
                MovingClass = (byte)Fdp.Toolkit.Tkb.Domain.SoundSourceClass.TrackedEngine,   // CE-3063
                FiringClass = (byte)Fdp.Toolkit.Tkb.Domain.SoundSourceClass.HeavyWeapon,
            });

            var solver = new EqsSolverSystem { BudgetUnits = int.MaxValue };
            new Fdp.Toolkit.Perception.Systems.LocalGridBuilderSystem(grid.Grid).Execute(view, 0.1f);
            solver.Execute(view, 0.1f);
            ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
            _world.Bus.SwapBuffers();

            var heard = view.ReadEvents<Fdp.Toolkit.Perception.Events.SoundContactEvent>().ToArray();
            var one = Assert.Single(heard);                                   // the movement — the shot does not carry 80 m
            Assert.Equal(unit, one.Observer);
            Assert.Equal((byte)Fdp.Toolkit.Perception.Signatures.SoundKind.Movement, one.Kind);
            // ⭐ CE-3063 (R-207) — it sounds like a tracked engine: a class, never an identity.
            Assert.Equal((byte)Fdp.Toolkit.Tkb.Domain.SoundSourceClass.TrackedEngine, one.SourceClass);
            Assert.InRange(one.Radius, 7.9f, 8.1f);                            // 0.1 × ~80 m
            Assert.True(Vector2.Distance(new Vector2(one.X, one.Y), new Vector2(10f, 90f)) <= one.Radius + 0.01f,
                "the estimate lies inside the uncertainty radius of the truth");
            Assert.Empty(view.ReadEvents<Fdp.Toolkit.Perception.Events.SensorTrackStateEvent>().ToArray());   // ⛔ no identity
        }

        /// <summary>
        /// ⭐ CE-3038 — a TKB that lists NO sensors but can see gets one implicit visual sensor (part 1000) that reads the
        /// unit's live receptor (a Brain retunes it over the wire); a TKB with a list gets only its list; one that cannot
        /// see gets none. The unit no longer carries a SensorContactList.
        /// </summary>
        [Fact]
        public void S5_TheImplicitVisualSensor_IsBuiltOnlyForASeeingUnitWithNoSensorList()
        {
            var seeing = _world.CreateEntity();
            var t1 = new TkbTemplate("Seeing", 601);
            t1.AddDescriptor(new SensorCapabilitiesDto { VisionRange = 200f, FieldOfViewDegrees = 90f });
            new PerceptionTkbTranslator().Inject(_world, seeing, t1);
            var v = UnitSensors.Of(_world, seeing, SensorModality.Visual);
            Assert.False(v.IsNull);
            Assert.Equal(SensorChildFactory.FirstTkbPartId, _world.GetComponentRO<PartMetadata>(v).InstanceId);
            Assert.Equal(1, _world.GetComponentRO<SensorTag>(v).Implicit);
            Assert.Equal(VisualPerception.BlueprintId, _world.GetComponentRO<EqsSensor>(v).BlueprintId);
            Assert.False(_world.HasComponent<SensorContactList>(seeing));

            var listed = _world.CreateEntity();
            var t2 = new TkbTemplate("Listed", 602);
            t2.AddDescriptor(new SensorCapabilitiesDto { VisionRange = 200f, Sensors = new List<SensorEntryDto> { TestRadar(100f) } });
            new PerceptionTkbTranslator().Inject(_world, listed, t2);
            Assert.True(UnitSensors.Of(_world, listed, SensorModality.Visual).IsNull);
            Assert.False(UnitSensors.Of(_world, listed, SensorModality.Radar).IsNull);

            var blind = _world.CreateEntity();
            var t3 = new TkbTemplate("Blind", 603);
            t3.AddDescriptor(new SensorCapabilitiesDto { VisionRange = 0f });
            new PerceptionTkbTranslator().Inject(_world, blind, t3);
            Assert.True(UnitSensors.Of(_world, blind, SensorModality.Visual).IsNull);
        }
    }
}