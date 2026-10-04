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
using Fdp.Toolkit.Perception.Components;
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
    }
}