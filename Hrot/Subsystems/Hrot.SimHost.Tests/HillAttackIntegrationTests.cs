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
    ///
    /// <para>
    /// All SC-HA015-1…4 tests exercise the commander behavior exclusively via
    /// <see cref="BTreeTickSystem"/> — no BTree node methods are called directly.
    /// The EQS solver (<see cref="AreaQuerySolverSystem"/>) is invoked in the same
    /// simulated tick as the BTree (collapsing the production 10-Hz EqsModule latency
    /// to zero for deterministic in-process testing).
    /// <see cref="AreaQueryInitializationSystem"/> is intentionally excluded from the
    /// per-tick helper so that EQS results written by the solver in tick N remain
    /// readable when the BTree resumes in tick N+1.  In production the results become
    /// visible within one EqsModule cycle (~100 ms); the test collapses that window
    /// to a single-frame solver call.
    /// </para>
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
                if (b.Results.IsCreated)  b.Results.Dispose();
            }
            if (repo.HasSingleton<EqsTargetPool>())
            {
                var p = repo.GetSingleton<EqsTargetPool>();
                if (p.Targets.IsCreated) p.Targets.Dispose();
            }
            if (repo.HasSingleton<EqsResultPool>())
            {
                var r = repo.GetSingleton<EqsResultPool>();
                if (r.Results.IsCreated) r.Results.Dispose();
            }
        }

        private Entity CreateAreaEntity(IList<Vector2> polygon)
        {
            var entity = _repo.CreateEntity();
            // Polygon vertices are relative to the area entity's SimTransform position.
            // Place the origin at (0,0,0) so local space equals world space in these tests.
            _repo.AddComponent(entity, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
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

        // Builds the reusable pipeline tuple for BTree-based tests.
        private static (BehaviorIngressSystem ingress,
                        TacticalIntentResolutionSystem resolution,
                        BTreeTickSystem btree,
                        AreaQuerySolverSystem eqs)
            BuildPipeline(NetworkEntityMap entityMap)
        {
            var registry       = BuildRegistry(entityMap);
            var mapperRegistry = new TacticalIntentMapperRegistry();
            mapperRegistry.Register(new HullDownAttackMapper());
            return (
                new BehaviorIngressSystem(registry),
                new TacticalIntentResolutionSystem(mapperRegistry, registry),
                new BTreeTickSystem(registry),
                new AreaQuerySolverSystem()
            );
        }

        // Runs one simulation tick through the full Brain pipeline.
        //
        // The EQS solver is driven within the SAME tick to collapse the production 10-Hz
        // EqsModule latency to zero for deterministic in-process testing.
        // Extra SwapBuffers calls are needed because events published in the BTree phase
        // are in the WRITE buffer and must be swapped to the READ buffer before the solver
        // can consume them; similarly, result events must be swapped to READ before
        // materialization can write them to the ring buffer.
        private static void TickOnce(EntityRepository repo,
            BehaviorIngressSystem behaviorIngress,
            TacticalIntentResolutionSystem tacticalResolution,
            BTreeTickSystem btreeTick,
            AreaQuerySolverSystem eqsSolver,
            float dt = 0.1f)
        {
            // Begin frame: swap previous write->read so ingress systems can see last frame's events.
            repo.Bus.SwapBuffers();
            behaviorIngress.Execute(repo, dt);
            tacticalResolution.Execute(repo, dt);
            // BTree runs: may publish AreaQueryRequestEvent to WRITE buffer.
            btreeTick.Execute(repo, dt);

            // Collapse EQS latency: swap so solver can read the request events just published.
            repo.Bus.SwapBuffers();
            var ecb = (Fdp.Core.EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
            eqsSolver.Execute(repo, dt);
            ecb.Playback(repo);

            // Swap again so the result events published by the solver are readable by materialization.
            repo.Bus.SwapBuffers();
            new AreaQueryResultMaterializationSystem().Execute(repo, dt);
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

            // Act: swap so solver sees the request events, run solver, playback cmd,
            // swap so materialization sees the result events, then materialize.
            var ecb = (Fdp.Core.EntityCommandBuffer)((ISimulationView)_repo).GetCommandBuffer();
            var materialization = new AreaQueryResultMaterializationSystem();
            _repo.Bus.SwapBuffers();
            solver.Execute(_repo, 0.1f);
            ecb.Playback(_repo);
            _repo.Bus.SwapBuffers();
            materialization.Execute(_repo, 0.1f);

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
        /// SC-HA015-4: With 3 tanks and 2 EQS targets, the BTree orchestrator
        /// assigns targets in round-robin order via <see cref="BTreeTickSystem"/>:
        /// the first and third dispatched tank receive the same target, while the
        /// second receives the other.
        /// </summary>
        [Fact]
        public void SC_HA015_4_DispatchWaveWithTargets_AssignsTargetsRoundRobin_ViaBTreeSystem()
        {
            // Arrange — 90 m firing line (3 slots at 30 m spacing), 2 hostiles inside polygon.
            const long areaNetId = 9004L;
            const long netId1    = 101L;
            const long netId2    = 202L;

            var areaEntity = CreateAreaEntity(new List<Vector2>
            {
                new(10f, 10f), new(80f, 10f), new(80f, 80f), new(10f, 80f),
            });

            var entityMap = new NetworkEntityMap();
            entityMap.Register(areaNetId, areaEntity);
            _repo.SetSingletonManaged<NetworkEntityMap>(entityMap);

            // Two hostile entities inside the polygon — visible to the EQS solver.
            var hostile1 = CreateHostileAt(40f, 40f);
            var hostile2 = CreateHostileAt(60f, 60f);
            _repo.AddComponent(hostile1, new NetworkIdentity { Value = netId1 });
            _repo.AddComponent(hostile2, new NetworkIdentity { Value = netId2 });

            var (ingress, resolution, btree, eqs) = BuildPipeline(entityMap);

            var commander = _repo.CreateEntity();
            _repo.AddComponent<BehaviorState>(commander, default);
            _repo.AddComponent<BrainBTreeState>(commander, default);
            _repo.AddComponent<BrainBlackboard>(commander, default);
            _repo.AddComponent<Blackboard1024>(commander, default);

            // 3 subs already at baseline — AreAllAtBaseline returns Success immediately.
            var subs = new Entity[3];
            for (int i = 0; i < 3; i++)
            {
                subs[i] = _repo.CreateEntity();
                _repo.AddComponent(subs[i], new TkbIdentity { TkbType = TkbEntityTypes.Tank_M1Abrams });
                _repo.AddComponent<BehaviorState>(subs[i], default);
                _repo.AddComponent(subs[i], new NavigationStatus { Result = NavigationResult.Arrived });
            }
            AddRoster(_repo, commander, subs);

            // Publish AssignBehaviorEvent with a 90 m firing line (3 slots @ 30 m).
            string json = "{\"firingLineStart\":{\"x\":0,\"y\":0},"
                        + "\"firingLineEnd\":{\"x\":90,\"y\":0},"
                        + "\"baselineStart\":{\"x\":0,\"y\":50},"
                        + "\"baselineEnd\":{\"x\":90,\"y\":50},"
                        + "\"tankSpacing\":30,"
                        + $"\"targetAreaNetworkId\":{areaNetId}}}";

            _repo.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = commander,
                BehaviorName = "PlatoonHillAttack",
                JsonParams   = json,
            });

            // Tick 1: BehaviorIngress activates PlatoonHillAttack.
            //         BTree runs: CalculateSegments → DispatchAllToBaseline →
            //         AreAllAtBaseline (arrived) → RequestAreaQuery (submits, returns
            //         Success) → IsAreaQueryResolved (not ready yet, returns Running).
            //         EQS solver resolves the query: TargetCount = 2.
            TickOnce(_repo, ingress, resolution, btree, eqs);

            // Tick 2: BTree resumes at IsAreaQueryResolved (result ready, TargetCount=2
            //         → Success) → Action_DispatchWaveWithTargets publishes one
            //         "HullDownAttack" AssignTacticalIntentEvent per tank →
            //         Condition_IsWaveCompleted returns Running (wave active).
            TickOnce(_repo, ingress, resolution, btree, eqs);

            // Move the HullDownAttack events from the write buffer to the read buffer.
            _repo.Bus.SwapBuffers();

            // Assert — exactly 3 HullDownAttack events, one per tank.
            var events = _repo.Bus.ReadManaged<AssignTacticalIntentEvent>()
                .Where(e => e.IntentId == "HullDownAttack")
                .ToList();

            Assert.Equal(3, events.Count);

            long t0 = ParseTargetNetworkId(events[0].JsonParams);
            long t1 = ParseTargetNetworkId(events[1].JsonParams);
            long t2 = ParseTargetNetworkId(events[2].JsonParams);

            // Round-robin: tank 0 and tank 2 must share the same target,
            // tank 1 must get the other.  The assertion is pool-order-agnostic
            // (we don't hard-code which network ID is "first" in the EQS result).
            Assert.NotEqual(t0, t1);        // tank 0 ≠ tank 1
            Assert.Equal(t0, t2);           // tank 2 wraps round to tank 0's target
            Assert.NotEqual(t1, t2);        // tank 1 ≠ tank 2
        }

        // ── SC-HA015-2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-HA015-2: No two tanks dispatched in the same wave receive the same
        /// firing-line slot.  The test drives the commander through
        /// <see cref="BTreeTickSystem"/> — no BTree node methods are called directly.
        /// </summary>
        [Fact]
        public void SC_HA015_2_DispatchWaveWithTargets_AssignsUniqueSlots_ViaBTreeSystem()
        {
            // Arrange — 3 tanks, 2 hostiles inside a 90 m polygon.
            const long areaNetId = 9002L;

            var areaEntity = CreateAreaEntity(new List<Vector2>
            {
                new(10f, 10f), new(80f, 10f), new(80f, 80f), new(10f, 80f),
            });

            var entityMap = new NetworkEntityMap();
            entityMap.Register(areaNetId, areaEntity);
            _repo.SetSingletonManaged<NetworkEntityMap>(entityMap);

            var hostile1 = CreateHostileAt(40f, 40f);
            var hostile2 = CreateHostileAt(60f, 60f);
            _repo.AddComponent(hostile1, new NetworkIdentity { Value = 101L });
            _repo.AddComponent(hostile2, new NetworkIdentity { Value = 202L });

            var (ingress, resolution, btree, eqs) = BuildPipeline(entityMap);

            var commander = _repo.CreateEntity();
            _repo.AddComponent<BehaviorState>(commander, default);
            _repo.AddComponent<BrainBTreeState>(commander, default);
            _repo.AddComponent<BrainBlackboard>(commander, default);
            _repo.AddComponent<Blackboard1024>(commander, default);

            var subs = new Entity[3];
            for (int i = 0; i < 3; i++)
            {
                subs[i] = _repo.CreateEntity();
                _repo.AddComponent(subs[i], new TkbIdentity { TkbType = TkbEntityTypes.Tank_M1Abrams });
                _repo.AddComponent<BehaviorState>(subs[i], default);
                _repo.AddComponent(subs[i], new NavigationStatus { Result = NavigationResult.Arrived });
            }
            AddRoster(_repo, commander, subs);

            string json = "{\"firingLineStart\":{\"x\":0,\"y\":0},"
                        + "\"firingLineEnd\":{\"x\":90,\"y\":0},"
                        + "\"baselineStart\":{\"x\":0,\"y\":50},"
                        + "\"baselineEnd\":{\"x\":90,\"y\":50},"
                        + "\"tankSpacing\":30,"
                        + $"\"targetAreaNetworkId\":{areaNetId}}}";

            _repo.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = commander,
                BehaviorName = "PlatoonHillAttack",
                JsonParams   = json,
            });

            // Tick 1: activates behavior, BTree submits EQS request, solver resolves.
            TickOnce(_repo, ingress, resolution, btree, eqs);

            // Tick 2: BTree reads EQS result (2 targets) → DispatchWaveWithTargets
            //         publishes 3 "HullDownAttack" events → IsWaveCompleted Running.
            TickOnce(_repo, ingress, resolution, btree, eqs);

            _repo.Bus.SwapBuffers();

            // Assert — all (SlotX, SlotY) pairs are distinct.
            var events = _repo.Bus.ReadManaged<AssignTacticalIntentEvent>()
                .Where(e => e.IntentId == "HullDownAttack")
                .ToList();

            Assert.True(events.Count >= 2,
                $"Expected ≥2 HullDownAttack dispatch events, got {events.Count}.");

            var slots = events.Select(e => ParseSlot(e.JsonParams)).ToList();
            for (int i = 0; i < slots.Count; i++)
            {
                for (int j = i + 1; j < slots.Count; j++)
                {
                    bool same = MathF.Abs(slots[i].SlotX - slots[j].SlotX) < 0.01f
                             && MathF.Abs(slots[i].SlotY - slots[j].SlotY) < 0.01f;
                    Assert.False(same,
                        $"Tank {i} and tank {j} share firing-line slot "
                        + $"({slots[i].SlotX:G4}, {slots[i].SlotY:G4}).");
                }
            }
        }

        // ── SC-HA015-3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// SC-HA015-3: When a tank is killed mid-wave, <c>Condition_IsWaveCompleted</c>
        /// (invoked via <see cref="BTreeTickSystem"/>) permanently burns that tank's
        /// firing-line slot into <c>BurnedSlotsMask</c>.  The wave still completes
        /// once the surviving tank finishes its run.
        /// </summary>
        [Fact]
        public unsafe void SC_HA015_3_IsWaveCompleted_BurnsSlotOfKilledTank_ViaBTreeSystem()
        {
            // Arrange — 2 subs, 2 hostiles inside polygon.
            const long areaNetId = 9003L;

            var areaEntity = CreateAreaEntity(new List<Vector2>
            {
                new(10f, 10f), new(80f, 10f), new(80f, 80f), new(10f, 80f),
            });

            var entityMap = new NetworkEntityMap();
            entityMap.Register(areaNetId, areaEntity);
            _repo.SetSingletonManaged<NetworkEntityMap>(entityMap);

            var hostile1 = CreateHostileAt(40f, 40f);
            var hostile2 = CreateHostileAt(60f, 60f);
            _repo.AddComponent(hostile1, new NetworkIdentity { Value = 301L });
            _repo.AddComponent(hostile2, new NetworkIdentity { Value = 302L });

            var (ingress, resolution, btree, eqs) = BuildPipeline(entityMap);

            var commander = _repo.CreateEntity();
            _repo.AddComponent<BehaviorState>(commander, default);
            _repo.AddComponent<BrainBTreeState>(commander, default);
            _repo.AddComponent<BrainBlackboard>(commander, default);
            _repo.AddComponent<Blackboard1024>(commander, default);

            // 2 subs at baseline.
            var subs = new Entity[2];
            for (int i = 0; i < 2; i++)
            {
                subs[i] = _repo.CreateEntity();
                _repo.AddComponent(subs[i], new TkbIdentity { TkbType = TkbEntityTypes.Tank_M1Abrams });
                _repo.AddComponent<BehaviorState>(subs[i], default);
                _repo.AddComponent(subs[i], new NavigationStatus { Result = NavigationResult.Arrived });
            }
            AddRoster(_repo, commander, subs);

            string json = "{\"firingLineStart\":{\"x\":0,\"y\":0},"
                        + "\"firingLineEnd\":{\"x\":60,\"y\":0},"
                        + "\"baselineStart\":{\"x\":0,\"y\":50},"
                        + "\"baselineEnd\":{\"x\":60,\"y\":50},"
                        + "\"tankSpacing\":30,"
                        + $"\"targetAreaNetworkId\":{areaNetId}}}";

            _repo.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = commander,
                BehaviorName = "PlatoonHillAttack",
                JsonParams   = json,
            });

            // Tick 1: behavior activated, EQS query submitted and resolved (2 targets).
            TickOnce(_repo, ingress, resolution, btree, eqs);

            // Tick 2: IsAreaQueryResolved → Success → DispatchWaveWithTargets dispatches
            //         2 "HullDownAttack" events → IsWaveCompleted Running.
            TickOnce(_repo, ingress, resolution, btree, eqs);

            // Post-dispatch: read mutable state to locate the dispatched tanks.
            ref var s = ref GetHeavyState(_repo, commander);
            Assert.Equal(2, s.ActiveAttackerCount);

            // Kill the first dispatched attacker mid-wave.
            // HasStartedRun[i] is left at 0 (set by DispatchWaveWithTargets) so that
            // Condition_IsWaveCompleted handles the dead-tank and the alive-tank checks
            // independently: the dead tank is burned immediately; the alive tank is kept
            // Running because its behavior has not yet transitioned to HullDownAttackRun.
            var tank0 = new Entity((ulong)s.ActiveEntityPacked[0]);
            byte burnedSlot = s.ActiveSlotIndex[0];
            _repo.DestroyEntity(tank0);

            // Tick 3: BTreeTickSystem resumes at Condition_IsWaveCompleted.
            //         Dead tank0 → its slot is burned into BurnedSlotsMask (independent
            //         of HasStartedRun).
            //         Alive tank1 → HasStartedRun=0, behavior not yet HullDownAttackRun
            //         (TacticalResolution delivers that event in the next tick) → Running.
            TickOnce(_repo, ingress, resolution, btree, eqs);

            Assert.Equal((ushort)(1 << burnedSlot), s.BurnedSlotsMask);
            Assert.Equal(1, s.ActiveAttackerCount);

            // Surviving tank is now at index 0 after SwapRemove.
            // Advance HasStartedRun so the next IsWaveCompleted check treats the run as
            // started, and clear the behavior hash to signal the run is done.
            s.HasStartedRun[0] = 1;
            var tank1 = new Entity((ulong)s.ActiveEntityPacked[0]);
            ref var beh1 = ref _repo.GetComponentRW<BehaviorState>(tank1);
            beh1.ActiveBehaviorHash = 0;  // run finished

            // Tick 4: Condition_IsWaveCompleted sees surviving tank done → Success.
            TickOnce(_repo, ingress, resolution, btree, eqs);

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
            var tacticalResolution = new TacticalIntentResolutionSystem(mapperRegistry, registry);
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
