using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fbt;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.AI.Behaviors;
using Hrot.AI.Behaviors.Brains;
using Hrot.AI.Behaviors.Mappers;
using Hrot.CGF.Systems;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.SimHost;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Integration tests for the PlatoonHillAttack / HullDownAttack pipeline
    /// (SC-HA015-1 through SC-HA015-6).
    /// </summary>
    public sealed class HillAttackIntegrationTests : IDisposable
    {
        private readonly EntityRepository _repo;
        private SpatialHashGrid _grid;

        // PlatoonHillAttack behavior integer ID (stable; must not change).
        private const int PlatoonHillAttack_BT = 3014;

        // HullDownAttackRun behavior integer ID (stable; must not change).
        private const int HullDownAttackRun_BT = 3013;

        public HillAttackIntegrationTests()
        {
            _repo = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(_repo);
            _repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();

            // 100 x 100 cells, 5 m each → covers 0..500 m in both axes.
            _grid = SpatialHashGrid.Create(100, 100, 5f, 1000, Allocator.Persistent);
            _grid.Clear();
            _repo.SetSingleton(new SpatialGridData { Grid = _grid });
        }

        public void Dispose()
        {
            DisposeEqsSingletons(_repo);
            _grid.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static void DisposeEqsSingletons(EntityRepository repo)
        {
            if (repo.HasSingleton<AreaQueryBatchData>())
            {
                ref var b = ref repo.GetSingleton<AreaQueryBatchData>();
                if (b.Requests.IsCreated) b.Requests.Dispose();
                if (b.Results.IsCreated)  b.Results.Dispose();
            }
            if (repo.HasSingleton<EqsTargetPool>())
            {
                var p = repo.GetSingleton<EqsTargetPool>();
                if (p.Targets.IsCreated) p.Targets.Dispose();
            }
        }

        private Entity CreateAreaEntity(IList<Vector2> polygon)
        {
            var entity = _repo.CreateEntity();
            var ecb = (Fdp.Core.EntityCommandBuffer)((ISimulationView)_repo).GetCommandBuffer();
            ecb.AddManagedComponent(entity, new EditablePolyline
            {
                Points  = new List<Vector2>(polygon),
                Version = 1,
            });
            ecb.Playback(_repo);
            return entity;
        }

        private Entity CreateHostileAt(float x, float y)
        {
            var e = _repo.CreateEntity();
            _repo.AddComponent(e, new SimTransform
            {
                Position = new Vector3(x, y, 0f),
                Rotation = Quaternion.Identity,
            });
            _repo.AddComponent(e, new EntityInfo { ForceId = ForceId.Hostile });
            _grid.Add(e, new Vector2(x, y));
            _repo.SetSingleton(new SpatialGridData { Grid = _grid });
            return e;
        }

        private static unsafe ref HillAttackMutableState GetHeavyState(
            EntityRepository repo, Entity entity)
        {
            ref var heavy = ref repo.GetComponentRW<Blackboard1024>(entity);
            return ref Unsafe.As<Blackboard1024, HillAttackMutableState>(ref heavy);
        }

        private static unsafe void AddRoster(EntityRepository repo, Entity commander,
            Entity[] subs)
        {
            var roster = new UnitRoster { Count = subs.Length };
            for (int i = 0; i < subs.Length; i++)
                roster.SubordinateEntities[i] = (long)subs[i].PackedValue;
            repo.AddComponent(commander, roster);
        }

        private static BehaviorRegistry BuildRegistry(NetworkEntityMap entityMap)
        {
            var registry  = new BehaviorRegistry();
            var regAction = AiBehaviorFactory.BuildRegistrationAction(null, entityMap);
            regAction(registry);
            return registry;
        }

        // Runs one frame: swaps event buffers then executes all systems in order.
        // NOTE: AreaQueryInitializationSystem is intentionally omitted so that EQS
        // results submitted in a previous tick remain readable via batch.Count.
        private static void TickOnce(EntityRepository repo,
            BehaviorIngressSystem behaviorIngress,
            TacticalIntentResolutionSystem tacticalResolution,
            BTreeTickSystem btreeTick,
            AreaQuerySolverSystem eqsSolver,
            float dt = 0.1f)
        {
            repo.Bus.SwapBuffers();
            behaviorIngress.Execute(repo, dt);
            tacticalResolution.Execute(repo, dt);
            btreeTick.Execute(repo, dt);
            eqsSolver.Execute(repo, dt);
        }

        private static long ParseTargetNetworkId(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("TargetNetworkId").GetInt64();
        }

        private static (float SlotX, float SlotY) ParseSlot(string json)
        {
            using var doc = JsonDocument.Parse(json);
            float sx = doc.RootElement.GetProperty("SlotX").GetSingle();
            float sy = doc.RootElement.GetProperty("SlotY").GetSingle();
            return (sx, sy);
        }

        // ── SC-HA015-6 ────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-HA015-6: AreaQuerySolverSystem finds hostile entities inside a polygon
        /// and excludes those outside it, as well as friendly entities inside it.
        /// </summary>
        [Fact]
        public void SC_HA015_6_AreaQuerySolver_FindsEnemiesInsidePolygon_ExcludesOutside()
        {
            // Arrange — polygon (35,35)-(65,35)-(65,65)-(35,65), two hostiles inside,
            // one hostile outside, one friendly inside (must not be counted).
            var polygon = new List<Vector2>
            {
                new(35f, 35f), new(65f, 35f), new(65f, 65f), new(35f, 65f),
            };
            var areaEntity = CreateAreaEntity(polygon);
            var requester  = _repo.CreateEntity();

            CreateHostileAt(50f, 50f);  // inside
            CreateHostileAt(60f, 40f);  // inside
            CreateHostileAt(80f, 80f);  // outside — must not be returned

            var friendly = _repo.CreateEntity();
            _repo.AddComponent(friendly, new SimTransform
            {
                Position = new Vector3(45f, 45f, 0f),
                Rotation = Quaternion.Identity,
            });
            _repo.AddComponent(friendly, new EntityInfo { ForceId = ForceId.Friend });
            _grid.Add(friendly, new Vector2(45f, 45f));
            _repo.SetSingleton(new SpatialGridData { Grid = _grid });

            long requestId = AreaQueryBatchHelper.RequestAreaQuery(
                _repo, requester, areaEntity, ForceId.Hostile);

            var solver = new AreaQuerySolverSystem();

            // Act
            solver.Execute(_repo, 0.1f);

            // Assert
            var result = AreaQueryBatchHelper.GetAreaQueryResult(_repo, requestId);
            Assert.True(result.IsReady);
            Assert.Equal(2, result.TargetCount);
        }

        // ── SC-HA015-5 ────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-HA015-5: BehaviorIngressSystem activates PlatoonHillAttack within one
        /// frame when an AssignBehaviorEvent is published.
        /// </summary>
        [Fact]
        public void SC_HA015_5_BehaviorIngress_ActivatesPlatoonHillAttack()
        {
            // Arrange
            const long areaNetId = 9001L;
            var areaEntity = CreateAreaEntity(new List<Vector2>
            {
                new(10f, 10f), new(20f, 10f), new(20f, 20f), new(10f, 20f),
            });

            var entityMap = new NetworkEntityMap();
            entityMap.Register(areaNetId, areaEntity);
            _repo.SetSingletonManaged<NetworkEntityMap>(entityMap);

            var registry       = BuildRegistry(entityMap);
            var behaviorIngress = new BehaviorIngressSystem(registry);

            var commander = _repo.CreateEntity();
            _repo.AddComponent<BehaviorState>(commander, default);
            _repo.AddComponent<BrainBTreeState>(commander, default);
            _repo.AddComponent<BrainBlackboard>(commander, default);
            _repo.AddComponent<Blackboard1024>(commander, default);

            string json = "{\"firingLineStart\":{\"x\":0,\"y\":0},"
                        + "\"firingLineEnd\":{\"x\":60,\"y\":0},"
                        + "\"baselineStart\":{\"x\":0,\"y\":50},"
                        + "\"baselineEnd\":{\"x\":60,\"y\":50},"
                        + "\"tankSpacing\":30,"
                        + "\"targetAreaNetworkId\":9001}";

            _repo.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = commander,
                BehaviorName = "PlatoonHillAttack",
                JsonParams   = json,
            });

            // Act
            _repo.Bus.SwapBuffers();
            behaviorIngress.Execute(_repo, 0.1f);

            // Assert — behavior hash must equal PlatoonHillAttack_BT (3014).
            var behaviorState = _repo.GetComponent<BehaviorState>(commander);
            Assert.Equal(PlatoonHillAttack_BT, behaviorState.ActiveBehaviorHash);
        }

        // ── SC-HA015-4 ────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-HA015-4: With 3 tanks and 2 EQS targets, Action_DispatchWaveWithTargets
        /// assigns targets in round-robin order: tank0 gets target[0], tank1 gets
        /// target[1], tank2 gets target[0] again.
        /// </summary>
        [Fact]
        public unsafe void SC_HA015_4_DispatchWaveWithTargets_AssignsTargetsRoundRobin()
        {
            // Arrange
            const long netId1 = 101L;
            const long netId2 = 202L;

            var areaEntity = CreateAreaEntity(new List<Vector2>
            {
                new(10f, 10f), new(80f, 10f), new(80f, 80f), new(10f, 80f),
            });

            var enemy1 = _repo.CreateEntity();
            var enemy2 = _repo.CreateEntity();
            _repo.AddComponent(enemy1, new NetworkIdentity { Value = netId1 });
            _repo.AddComponent(enemy2, new NetworkIdentity { Value = netId2 });

            // Populate pool: slot 0 = enemy1, slot 1 = enemy2.
            ref var pool = ref _repo.GetSingleton<EqsTargetPool>();
            pool.Targets[0] = (long)enemy1.PackedValue;
            pool.Targets[1] = (long)enemy2.PackedValue;
            pool.NextFreeIndex = 2;

            var commander = _repo.CreateEntity();
            _repo.AddComponent<Blackboard1024>(commander, default);

            // 3 tanks: allParticipate = (3 <= 3) = true → all participate.
            var subs = new Entity[3];
            for (int i = 0; i < 3; i++)
            {
                subs[i] = _repo.CreateEntity();
                _repo.AddComponent(subs[i], new TkbIdentity { TkbType = TkbEntityTypes.Tank_M1Abrams });
                _repo.AddComponent<BehaviorState>(subs[i], default);
            }
            AddRoster(_repo, commander, subs);

            // Pre-set mutable state: 3 slots (line 0..90, spacing 30), pool at handle 0.
            ref var s = ref GetHeavyState(_repo, commander);
            s.CachedEqsRequestId      = -1;   // use pool fallback
            s.CachedTargetGroupHandle = 0;
            s.TotalSlots              = 3;
            s.BurnedSlotsMask         = 0;
            s.WaveUsedSlotsMask       = 0;
            s.ActiveAttackerCount     = 0;
            s.CurrentWave             = 0;

            var p = new PlatoonHillAttackParams
            {
                StartX         = 0f,  StartY         = 0f,
                EndX           = 90f, EndY           = 0f,
                BaselineStartX = 0f,  BaselineStartY = 50f,
                BaselineEndX   = 90f, BaselineEndY   = 50f,
                AttackDirX     = 0f,  AttackDirY     = 1f,
                TankSpacing    = 30f,
                TargetAreaEntity = areaEntity,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = _repo };

            // Act
            var result = HillAttackCommanderNodes.Action_DispatchWaveWithTargets(
                ref p, ref state, ref ctx);

            _repo.Bus.SwapBuffers();

            // Assert — 3 HullDownAttack intent events in roster order.
            Assert.Equal(NodeStatus.Success, result);

            var events = _repo.Bus.ReadManaged<AssignTacticalIntentEvent>()
                .Where(e => e.IntentId == "HullDownAttack")
                .ToList();

            Assert.Equal(3, events.Count);

            long assigned0 = ParseTargetNetworkId(events[0].JsonParams);
            long assigned1 = ParseTargetNetworkId(events[1].JsonParams);
            long assigned2 = ParseTargetNetworkId(events[2].JsonParams);

            Assert.Equal(netId1, assigned0);  // tank 0 → target[0%2=0] = enemy1
            Assert.Equal(netId2, assigned1);  // tank 1 → target[1%2=1] = enemy2
            Assert.Equal(netId1, assigned2);  // tank 2 → target[2%2=0] = enemy1
        }

        // ── SC-HA015-2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-HA015-2: No two tanks dispatched in the same wave receive the same
        /// firing-line slot.
        /// </summary>
        [Fact]
        public unsafe void SC_HA015_2_DispatchWaveWithTargets_AssignsUniqueSlots()
        {
            // Arrange — 3 tanks, 2 targets in pool, 3-slot firing line.
            var areaEntity = CreateAreaEntity(new List<Vector2>
            {
                new(10f, 10f), new(80f, 10f), new(80f, 80f), new(10f, 80f),
            });

            var enemy1 = _repo.CreateEntity();
            var enemy2 = _repo.CreateEntity();
            _repo.AddComponent(enemy1, new NetworkIdentity { Value = 101L });
            _repo.AddComponent(enemy2, new NetworkIdentity { Value = 202L });

            ref var pool = ref _repo.GetSingleton<EqsTargetPool>();
            pool.Targets[0] = (long)enemy1.PackedValue;
            pool.Targets[1] = (long)enemy2.PackedValue;
            pool.NextFreeIndex = 2;

            var commander = _repo.CreateEntity();
            _repo.AddComponent<Blackboard1024>(commander, default);

            var subs = new Entity[3];
            for (int i = 0; i < 3; i++)
            {
                subs[i] = _repo.CreateEntity();
                _repo.AddComponent(subs[i], new TkbIdentity { TkbType = TkbEntityTypes.Tank_M1Abrams });
                _repo.AddComponent<BehaviorState>(subs[i], default);
            }
            AddRoster(_repo, commander, subs);

            ref var s = ref GetHeavyState(_repo, commander);
            s.CachedEqsRequestId      = -1;
            s.CachedTargetGroupHandle = 0;
            s.TotalSlots              = 3;
            s.BurnedSlotsMask         = 0;
            s.WaveUsedSlotsMask       = 0;
            s.ActiveAttackerCount     = 0;
            s.CurrentWave             = 0;

            var p = new PlatoonHillAttackParams
            {
                StartX         = 0f,  StartY         = 0f,
                EndX           = 90f, EndY           = 0f,
                BaselineStartX = 0f,  BaselineStartY = 50f,
                BaselineEndX   = 90f, BaselineEndY   = 50f,
                AttackDirX     = 0f,  AttackDirY     = 1f,
                TankSpacing    = 30f,
                TargetAreaEntity = areaEntity,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = _repo };

            // Act
            HillAttackCommanderNodes.Action_DispatchWaveWithTargets(
                ref p, ref state, ref ctx);

            _repo.Bus.SwapBuffers();

            // Assert — all dispatched (SlotX, SlotY) pairs are unique.
            var events = _repo.Bus.ReadManaged<AssignTacticalIntentEvent>()
                .Where(e => e.IntentId == "HullDownAttack")
                .ToList();

            Assert.True(events.Count >= 2, $"Expected at least 2 tanks dispatched, got {events.Count}.");

            var slotPairs = events.Select(e => ParseSlot(e.JsonParams)).ToList();
            for (int i = 0; i < slotPairs.Count; i++)
            {
                for (int j = i + 1; j < slotPairs.Count; j++)
                {
                    bool sameX = MathF.Abs(slotPairs[i].SlotX - slotPairs[j].SlotX) < 0.01f;
                    bool sameY = MathF.Abs(slotPairs[i].SlotY - slotPairs[j].SlotY) < 0.01f;
                    Assert.False(sameX && sameY,
                        $"Tank {i} and tank {j} share the same firing-line slot "
                        + $"({slotPairs[i].SlotX:G4}, {slotPairs[i].SlotY:G4}).");
                }
            }
        }

        // ── SC-HA015-3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-HA015-3: When a tank is killed mid-wave, Condition_IsWaveCompleted
        /// burns its firing-line slot in BurnedSlotsMask. The wave still completes
        /// when the surviving tank finishes its run.
        /// </summary>
        [Fact]
        public unsafe void SC_HA015_3_IsWaveCompleted_BurnsSlotOfKilledTank()
        {
            // Arrange — 2 active attackers in slots 0 and 1.
            var commander = _repo.CreateEntity();
            _repo.AddComponent<Blackboard1024>(commander, default);

            var tank0 = _repo.CreateEntity();
            var tank1 = _repo.CreateEntity();
            _repo.AddComponent<BehaviorState>(tank0, default);
            _repo.AddComponent<BehaviorState>(tank1, default);

            // Mark both as having started HullDownAttackRun.
            ref var beh0 = ref _repo.GetComponentRW<BehaviorState>(tank0);
            beh0.ActiveBehaviorHash = HullDownAttackRun_BT;
            ref var beh1 = ref _repo.GetComponentRW<BehaviorState>(tank1);
            beh1.ActiveBehaviorHash = HullDownAttackRun_BT;

            ref var s = ref GetHeavyState(_repo, commander);
            s.ActiveAttackerCount       = 2;
            s.ActiveEntityPacked[0]     = (long)tank0.PackedValue;
            s.ActiveEntityPacked[1]     = (long)tank1.PackedValue;
            s.ActiveSlotIndex[0]        = 0;
            s.ActiveSlotIndex[1]        = 1;
            s.ReturnBaselineSlotIndex[0] = 0;
            s.ReturnBaselineSlotIndex[1] = 1;
            s.HasStartedRun[0]          = 1;
            s.HasStartedRun[1]          = 1;
            s.BurnedSlotsMask           = 0;

            var p     = new PlatoonHillAttackParams();
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = _repo };

            // Kill tank0 mid-wave.
            _repo.DestroyEntity(tank0);

            // Act 1 — removes dead tank and burns its slot; tank1 is still running.
            var status1 = HillAttackCommanderNodes.Condition_IsWaveCompleted(
                ref p, ref state, ref ctx);

            // Assert intermediate state.
            Assert.Equal(NodeStatus.Running, status1);
            Assert.Equal((ushort)1, s.BurnedSlotsMask);  // bit 0 = slot 0 burned
            Assert.Equal(1, s.ActiveAttackerCount);

            // Simulate tank1 completing its run (behavior cleared by MissionAdapterSystem).
            ref var beh1After = ref _repo.GetComponentRW<BehaviorState>(tank1);
            beh1After.ActiveBehaviorHash = 0;

            // Act 2 — tank1 is done; wave completes.
            var status2 = HillAttackCommanderNodes.Condition_IsWaveCompleted(
                ref p, ref state, ref ctx);

            Assert.Equal(NodeStatus.Success, status2);
            Assert.Equal(0, s.ActiveAttackerCount);
        }

        // ── SC-HA015-1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-HA015-1: Full end-to-end run of PlatoonHillAttack commander. Subordinates
        /// are pre-positioned at the baseline (NavigationResult.Arrived), the target
        /// polygon contains no hostile entities, so the EQS returns TargetCount == 0.
        /// The Repeater exits on that Failure path and BehaviorFinishedEvent is published
        /// for the commander within a few ticks.
        /// </summary>
        [Fact]
        public void SC_HA015_1_FullEndToEnd_CommanderFinishes_WhenAreaIsEmpty()
        {
            // Arrange
            const long areaNetId = 9001L;

            // Empty target polygon — solver will find 0 hostile entities inside.
            var areaEntity = CreateAreaEntity(new List<Vector2>
            {
                new(200f, 200f), new(210f, 200f), new(210f, 210f), new(200f, 210f),
            });

            var entityMap = new NetworkEntityMap();
            entityMap.Register(areaNetId, areaEntity);
            _repo.SetSingletonManaged<NetworkEntityMap>(entityMap);

            var registry       = BuildRegistry(entityMap);
            var mapperRegistry = new TacticalIntentMapperRegistry();
            mapperRegistry.Register(new HullDownAttackMapper());

            var behaviorIngress    = new BehaviorIngressSystem(registry);
            var tacticalResolution = new TacticalIntentResolutionSystem(mapperRegistry);
            var btreeTick          = new BTreeTickSystem(registry);
            var eqsSolver          = new AreaQuerySolverSystem();

            var commander = _repo.CreateEntity();
            _repo.AddComponent<BehaviorState>(commander, default);
            _repo.AddComponent<BrainBTreeState>(commander, default);
            _repo.AddComponent<BrainBlackboard>(commander, default);
            _repo.AddComponent<Blackboard1024>(commander, default);

            // 2 subordinates already at the baseline.
            var subs = new Entity[2];
            for (int i = 0; i < 2; i++)
            {
                subs[i] = _repo.CreateEntity();
                _repo.AddComponent(subs[i], new NavigationStatus
                {
                    Result = NavigationResult.Arrived,
                });
                _repo.AddComponent<BehaviorState>(subs[i], default);
            }
            AddRoster(_repo, commander, subs);

            string json = "{\"firingLineStart\":{\"x\":0,\"y\":0},"
                        + "\"firingLineEnd\":{\"x\":60,\"y\":0},"
                        + "\"baselineStart\":{\"x\":0,\"y\":50},"
                        + "\"baselineEnd\":{\"x\":60,\"y\":50},"
                        + "\"tankSpacing\":30,"
                        + "\"targetAreaNetworkId\":9001}";

            _repo.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = commander,
                BehaviorName = "PlatoonHillAttack",
                JsonParams   = json,
            });

            // Act — Tick 1: activates BTree, submits EQS request, solver resolves
            //              (0 targets, empty polygon).
            //       Tick 2: BTree resumes, Condition_IsAreaQueryResolved → Failure
            //              (TargetCount == 0), Repeater exits, BTree returns Failure,
            //              BTreeTickSystem publishes BehaviorFinishedEvent.
            TickOnce(_repo, behaviorIngress, tacticalResolution, btreeTick, eqsSolver);
            TickOnce(_repo, behaviorIngress, tacticalResolution, btreeTick, eqsSolver);

            // Move the event from write buffer to read buffer.
            _repo.Bus.SwapBuffers();

            // Assert — BehaviorFinishedEvent must be published for the commander.
            bool finishedEventPublished = false;
            foreach (var evt in _repo.Bus.Read<BehaviorFinishedEvent>())
            {
                if (evt.Entity == commander)
                {
                    finishedEventPublished = true;
                    break;
                }
            }
            Assert.True(finishedEventPublished,
                "BehaviorFinishedEvent was not published for the commander.");
        }
    }
}
