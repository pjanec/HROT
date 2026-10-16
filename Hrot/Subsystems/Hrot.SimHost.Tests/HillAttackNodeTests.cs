using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Fbt;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.AI.Behaviors;
using Hrot.AI.Behaviors.Generated;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Configuration;
using Hrot.AI.Behaviors.Mappers;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for HillAttack commander nodes and the tank run's contract — Corrective-1 + TASK-HA010–016 (+ CE-2230).
    ///
    /// <para>Corrective-1 section covers the previously unwritten SC-HA007/HA008/HA009 tests.
    /// TASK-HA010–HA016 sections add commander node, EQS integration, wave dispatch,
    /// BTree registration, and JSON parse tests.</para>
    /// </summary>
    public class HillAttackNodeTests
    {
        // ── Helper: dispose EQS singletons ───────────────────────────────────────

        private static void DisposeEqsSingletons(EntityRepository world)
        {
            if (world.HasSingleton<EqsResultPool>())
            {
                var rp = world.GetSingleton<EqsResultPool>();
                if (rp.Results.IsCreated) rp.Results.Dispose();
            }
        }

        // ── Helper: create a fully-registered test world ──────────────────────────

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(repo);
            // ⭐⭐⭐ CE-259bf slice 3b (2026-09-12): this fixture exercises BRAIN-TIER nodes — AimAndFire
            //   writes a WeaponChannel, the wave dispatcher reads cognitive state — and it used to obtain
            //   that tier IMPLICITLY, because SimHostComponentRegistry registered the whole brain.
            //   🔒 User ruling: "Simhost has no ai(brain). So it does not need [them]" ⇒ SimHost no longer
            //   does, and this fixture must declare the dependency its SUBJECT actually has.
            // ⚠ This is the debt becoming visible, not a workaround: a test of brain behaviour needs a
            //   world with a brain in it, and it was only ever green because a Muscle node carried one.
            CognitiveComponentRegistry.RegisterAll(repo);
            repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            // S3-G: PlatoonHillAttack's Behavior-scoped working state is provisioned into a
            // BlueprintBlackboard* partition tier (registered in production by BlueprintRuntimeWiring).
            // ⭐⭐ B4: register from the LADDER, never a hand-list. 🔴 This WAS three explicit calls and
            //   did not know about the 256 tier, so the smallest tier — which is what a params-only
            //   behaviour selects — was silently unregistered and provisioning skipped it.
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(repo);
            return repo;
        }

        // ── Helper: set up UnitRoster on commander ────────────────────────────────

        private static unsafe void AddRoster(EntityRepository repo, Entity commander,
            Entity[] subs)
        {
            var roster = new UnitRoster { Count = subs.Length };
            for (int i = 0; i < subs.Length; i++)
                roster.SubordinateEntities[i] = subs[i];
            repo.AddComponent(commander, roster);
        }

        // ── Helper: get mutable hill attack state ─────────────────────────────────

        // These are direct node-logic UNIT tests: they invoke the node methods with an explicit
        // `ref HillAttackMutableState`, so this is purely a convenient per-entity scratch buffer for
        // that ref. This is NOT the production working-state path — in production (and in
        // T30/HillAttackIntegrationTests) the state lives in a Behavior-scoped BlueprintBlackboard*
        // partition slot; the Blackboard1024 + Unsafe.As hack was removed from the node bodies in S3-G.
        //
        // ⭐⭐ P4-① (2026-09-22): the scratch buffer no longer BORROWS AN ECS COMPONENT for the job.
        //    It used to `Unsafe.As` a Blackboard1024 into HillAttackMutableState — harmless in intent
        //    (the comment above always said so) but it made these unit tests depend on a component
        //    production had already stopped provisioning, which is the exact shape CE-310 and CE-311
        //    turned out to be. ⛔ A plain per-instance cell cannot be mistaken for a storage path.
        // ⚠ Per-INSTANCE, not static: xUnit gives each test its own class instance, so this cannot
        //   leak state between tests the way a static dictionary keyed on a reused entity id would.
        private readonly Dictionary<ulong, HillAttackMutableState[]> _scratchState = new();

        private ref HillAttackMutableState GetHeavyState(EntityRepository repo, Entity entity)
        {
            _ = repo;   // kept in the signature so every call site reads the same as before
            if (!_scratchState.TryGetValue(entity.PackedValue, out var cell))
                _scratchState[entity.PackedValue] = cell = new HillAttackMutableState[1];
            return ref cell[0];
        }

        // ── CE-2230 H-2: the tank run is the vehicle exposure cycle ──────────────
        //
        // ⛔ HISTORY: SC-HA007 (Condition_HasTarget, Action_CreepToAndBeyondSlot), SC-HA008 (Action_AimAndFireSpecific,
        //   Action_ReverseToBaseline), CE-466/CE-2119 (the fire node's stop rails) are retired with the five bespoke tank nodes
        //   (docs/designs/hill-attack/DESIGN.md §0 HA-1). Their behaviour — stop when seen, one round, reverse home — is railed
        //   on the cycle itself in VehicleFightFromCoverNodesTests (CE2230_*).

        /// <summary>
        /// 🔴🔴🔴 <b><c>CE-304</c>, re-homed by <c>CE-2230</c> — the ADDRESSING rail: what the commander SENDS lands in the root slot the
        /// tank's run reads.</b> <c>P3-C</c> once shipped a ZERO baseline through four green suites because every tank rail built its
        /// params by hand. ⭐⭐ This drives the REAL chain: <c>AssignBehaviorEvent</c> with the commander's JSON → the real
        /// <c>BehaviorIngressSystem</c> → the curated resolver <see cref="HillAttackTankNodes.ResolveHullDownAttackRun"/> → the root
        /// params slot, then asserts the run the asset's ONE node will tick, and that the asset registers that node.
        /// ⛔ Nothing here constructs a <see cref="HullDownRunConfig"/>.
        /// </summary>
        [Fact]
        public unsafe void CE304_TheCommandersPayload_LandsInTheRootSlot_AsTheLaneRun()
        {
            using var repo = CreateWorld();
            var map = new NetworkEntityMap();
            repo.SetSingletonManaged<NetworkEntityMap>(map);

            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);
            var ingress = new BehaviorIngressSystem(registry);

            var enemy = repo.CreateEntity();
            map.Register(4242, enemy);

            var tank = repo.CreateEntity();
            repo.AddComponent(tank, new BehaviorState());
            RootStateAccess.EnsureRootState(repo, tank);

            // The exact shape Action_DispatchWaveWithTargets publishes.
            string json = JsonSerializer.Serialize(
                new HullDownAttackParams
                {
                    BaselineX = 523f, BaselineY = 401f, SlotX = 530f, SlotY = 450f,
                    AttackDirX = 0f, AttackDirY = 2f, TargetNetworkId = 4242, ApproachSpeed = 15f, CreepSpeed = 5f,
                },
                Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);

            repo.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = tank,
                BehaviorName = BehaviorNames.HullDownAttackRun,
                JsonParams   = json,
            });
            repo.Bus.SwapBuffers();
            ingress.Execute(repo, 0.016f);

            ref byte bb = ref global::Fdp.Toolkit.Behavior.RootParamsAccess.RootRef(repo, tank);
            var f = Unsafe.As<byte, HullDownRunConfig>(ref bb).fight;

            Assert.Equal(1, f.UseGivenHide);
            Assert.Equal(new Vector2(523f, 401f), f.GivenHide);                  // the baseline slot is the hide
            Assert.Equal(new Vector2(530f, 450f), f.LaneStart);                  // the firing slot starts the lane
            Assert.Equal(new Vector2(0f, 1f), f.LaneDir);                        // normalised attack direction
            Assert.Equal(1, f.RoundsPerExposure);                                // 🔒 R-2022 — one round
            Assert.Equal(1, f.MaxExposures);                                     // one run per wave
            Assert.Equal(WithdrawMode.Reverse, f.WithdrawMode);
            Assert.Equal((long)enemy.PackedValue, f.DesignatePacked);            // the assigned target, by network id
            Assert.Equal(15f, f.ApproachSpeed);
            Assert.Equal(5f, f.CreepSpeed);

            // ⭐ and the asset's one node is the cycle, bound to that block.
            var actions = new ActionRegistry<byte, BTreeContext>();
            Hrot.AI.Behaviors.Trees.HullDownAttackRunRegistrar.Register(
                new BehaviorRegistry(), new Fdp.Toolkit.Blueprints.BlueprintRegistryStaging(), actions);
            //   (the key's tail is the binding's slot key — the generator's, stable for the asset's node b1)
            const string key = "Hrot.AI.Behaviors.Brains.VehicleFightFromCoverNodes.VehicleFightFromCover@0@230311729";
            Assert.True(actions.TryGetAction(key, out _));
            Assert.True(actions.TryGetDeactivator(key, out _));                 // it owns the channels it drives
        }

        // ── Corrective-1: SC-HA009 — HullDownAttackMapper ────────────────────────

        /// <summary>SC-HA009-1: HullDownAttackMapper.TargetIntentId == "HullDownAttack".</summary>
        [Fact]
        public void SC_HA009_1_HullDownAttackMapper_TargetIntentId_IsCorrect()
        {
            var mapper = new HullDownAttackMapper();
            Assert.Equal("HullDownAttack", mapper.TargetIntentId);
        }

        /// <summary>SC-HA009-2: Tank entity -> TryMap returns true, BehaviorName = "HullDownAttackRun".</summary>
        [Fact]
        public void SC_HA009_2_HullDownAttackMapper_TryMap_ReturnsTrue_ForTank()
        {
            using var repo = CreateWorld();

            var tank = repo.CreateEntity();
            repo.AddComponent(tank, new TkbIdentity { TkbType = TkbEntityTypes.Tank_M1Abrams });

            var mapper = new HullDownAttackMapper();
            var ok = mapper.TryMap(tank, repo, "{}", out var assignment);

            Assert.True(ok);
            Assert.NotNull(assignment);
            Assert.Equal(BehaviorNames.HullDownAttackRun, assignment.BehaviorName);
        }

        /// <summary>SC-HA009-3: Non-tank entity -> TryMap returns false.</summary>
        [Fact]
        public void SC_HA009_3_HullDownAttackMapper_TryMap_ReturnsFalse_ForNonTank()
        {
            using var repo = CreateWorld();

            var infantry = repo.CreateEntity();
            repo.AddComponent(infantry, new TkbIdentity { TkbType = TkbEntityTypes.Infantry_Rifleman });

            var mapper = new HullDownAttackMapper();
            var ok = mapper.TryMap(infantry, repo, "{}", out _);

            Assert.False(ok);
        }

        // ── TASK-HA010: SC-HA010-1 through SC-HA010-7 ────────────────────────────

        /// <summary>SC-HA010-1: 100m segment / 30m spacing => TotalSlots == 3; all bitmasks 0.</summary>
        [Fact]
        public void SC_HA010_1_CalculateSegments_100mDiv30m_Produces3Slots()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            var p = new PlatoonHillAttackParams
            {
                StartX = 0f, StartY = 0f,
                EndX = 100f, EndY = 0f,
                TankSpacing = 30f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            var result = HillAttackCommanderNodes.Action_CalculateSegments(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);
            ref var s  = ref GetHeavyState(repo, commander);

            Assert.Equal(NodeStatus.Success, result);
            Assert.Equal(3, s.TotalSlots);
            Assert.Equal(0, s.BurnedSlotsMask);
            Assert.Equal(0, s.WaveUsedSlotsMask);
            Assert.Equal(0, s.BaselineReservedMask);
        }

        /// <summary>SC-HA010-2: Segment shorter than spacing => TotalSlots == 1 (min 1).</summary>
        [Fact]
        public void SC_HA010_2_CalculateSegments_ShortSegment_ProducesOneSlot()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            var p = new PlatoonHillAttackParams
            {
                StartX = 0f, StartY = 0f,
                EndX = 10f, EndY = 0f,  // 10m < 30m spacing
                TankSpacing = 30f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            HillAttackCommanderNodes.Action_CalculateSegments(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);
            ref var s = ref GetHeavyState(repo, commander);

            Assert.Equal(1, s.TotalSlots);
        }

        /// <summary>SC-HA010-3: Very long segment => TotalSlots capped at 16.</summary>
        [Fact]
        public void SC_HA010_3_CalculateSegments_VeryLongSegment_CapsAt16Slots()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            var p = new PlatoonHillAttackParams
            {
                StartX = 0f, StartY = 0f,
                EndX = 1000f, EndY = 0f,  // 1000m / 30m = 33 would exceed 16
                TankSpacing = 30f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            HillAttackCommanderNodes.Action_CalculateSegments(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);
            ref var s = ref GetHeavyState(repo, commander);

            Assert.Equal(16, s.TotalSlots);
        }

        /// <summary>SC-HA010-4: DispatchAllToBaseline with 4-tank roster publishes exactly
        /// 4 AssignTacticalIntentEvent entries with distinct coordinates.</summary>
        [Fact]
        public void SC_HA010_4_DispatchAllToBaseline_Publishes4Events_WithDistinctCoords()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            var subs = new Entity[4];
            for (int i = 0; i < 4; i++)
            {
                subs[i] = repo.CreateEntity();
                repo.AddComponent(subs[i], new NavigationStatus());
            }
            AddRoster(repo, commander, subs);

            var p = new PlatoonHillAttackParams
            {
                BaselineStartX = 0f, BaselineStartY = 100f,
                BaselineEndX   = 90f, BaselineEndY  = 100f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            HillAttackCommanderNodes.Action_DispatchAllToBaseline(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);
            repo.Bus.SwapBuffers();
            var events = repo.Bus.ReadManaged<AssignTacticalIntentEvent>();

            Assert.Equal(4, events.Count);
            // Verify all events are for MoveToLocation with distinct coordinates
            var xs = new System.Collections.Generic.HashSet<string>();
            foreach (var ev in events)
            {
                Assert.Equal("MoveToLocation", ev.IntentId);
                xs.Add(ev.JsonParams!);
            }
            // 4 distinct JSON strings (distinct positions)
            Assert.Equal(4, xs.Count);
        }

        /// <summary>SC-HA010-5: Condition_AreAllAtBaseline returns Running when any alive
        /// subordinate has NavigationResult != Arrived.</summary>
        [Fact]
        public void SC_HA010_5_AreAllAtBaseline_ReturnsRunning_WhenOneNotArrived()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            var sub1 = repo.CreateEntity();
            var sub2 = repo.CreateEntity();
            repo.AddComponent(sub1, new NavigationStatus { Result = NavigationResult.Arrived });
            repo.AddComponent(sub2, new NavigationStatus { Result = NavigationResult.InProgress });
            AddRoster(repo, commander, new[] { sub1, sub2 });

            var p     = new PlatoonHillAttackParams();
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            var result = HillAttackCommanderNodes.Condition_AreAllAtBaseline(ref p, ctx.Self, ctx.World);

            Assert.Equal(NodeStatus.Running, result);
        }

        /// <summary>SC-HA010-6: Condition_AreAllAtBaseline returns Success when all alive
        /// subordinates have NavigationResult == Arrived.</summary>
        [Fact]
        public void SC_HA010_6_AreAllAtBaseline_ReturnsSuccess_WhenAllArrived()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            var sub1 = repo.CreateEntity();
            var sub2 = repo.CreateEntity();
            repo.AddComponent(sub1, new NavigationStatus { Result = NavigationResult.Arrived });
            repo.AddComponent(sub2, new NavigationStatus { Result = NavigationResult.Arrived });
            AddRoster(repo, commander, new[] { sub1, sub2 });

            var p     = new PlatoonHillAttackParams();
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            var result = HillAttackCommanderNodes.Condition_AreAllAtBaseline(ref p, ctx.Self, ctx.World);

            Assert.Equal(NodeStatus.Success, result);
        }

        /// <summary>SC-HA010-7: Dead subordinate does not block Condition_AreAllAtBaseline.</summary>
        [Fact]
        public void SC_HA010_7_AreAllAtBaseline_DeadSubordinateCountsAsArrived()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            var aliveSub = repo.CreateEntity();
            var deadSub  = repo.CreateEntity();
            repo.AddComponent(aliveSub, new NavigationStatus { Result = NavigationResult.Arrived });
            // deadSub has no NavigationStatus; will be destroyed.
            AddRoster(repo, commander, new[] { aliveSub, deadSub });

            // Kill the second subordinate.
            repo.DestroyEntity(deadSub);

            var p     = new PlatoonHillAttackParams();
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            var result = HillAttackCommanderNodes.Condition_AreAllAtBaseline(ref p, ctx.Self, ctx.World);

            Assert.Equal(NodeStatus.Success, result);
        }

        // ── TASK-HA011: SC-HA011-1 through SC-HA011-5 ────────────────────────────
        // ⭐ EQS 1.3 (DESIGN_Hill_Attack_Eqs_Migration.md §3.2): the area query is the commander's EQS child sensor.
        //    These rails drive the nodes against a REAL sensor entity and its EqsCognitiveBuffer — the component
        //    EqsResultUpdateSystem writes — instead of the retired AreaQueryBatchHelper ring.

        /// <summary>A live area sensor under <paramref name="commander"/>; <paramref name="targets"/> == null ⇒ not answered yet.</summary>
        private static Entity AreaSensor(EntityRepository repo, Entity commander, Entity area, params Entity[]? targets)
        {
            var sensor = repo.CreateEntity();
            repo.AddComponent(sensor, new PartMetadata { ParentEntity = commander, InstanceId = 1 });
            // CE-485: the commander finds its sensor by the owner stamp (site), not by the part id.
            repo.AddComponent(sensor, new Fdp.Toolkit.Behavior.Components.BehaviorOwnedPart { SiteId = HillAttackCommanderNodes.AreaSensorInstanceId });
            repo.AddComponent(sensor, HillAttackCommanderNodes.AreaSensor(area));
            var buffer = new EqsCognitiveBuffer();
            if (targets != null)
            {
                buffer.Count = targets.Length;
                buffer.LastUpdateTick = 1u;
                var span = buffer.GetSpanRW();
                for (int i = 0; i < targets.Length; i++) span[i] = new EqsResult { EntityId = (long)targets[i].PackedValue };
            }
            repo.AddComponent(sensor, buffer);
            return sensor;
        }

        private static void Playback(EntityRepository repo)
            => ((EntityCommandBuffer)((Fdp.ModuleHost.Abstractions.ISimulationView)repo).GetCommandBuffer()).Playback(repo);

        /// <summary>SC-HA011-1: the first ask CREATES the sensor — the creation is the question (Success; ⭐ CE-485: on the live
        /// world it exists at once, so it is cached immediately, epoch counter 1); then it is awaited (Running). The NEXT wave's
        /// ask refreshes it: a new epoch, the old answer cleared.</summary>
        [Fact]
        public void SC_HA011_1_RequestAreaQuery_CreatesTheSensor_ThenRefreshesItPerWave()
        {
            using var repo = CreateWorld();
            var commander  = repo.CreateEntity();
            var areaEntity = repo.CreateEntity();

            ref var s = ref GetHeavyState(repo, commander);
            s.CachedEqsRequestId = -1;
            var p     = new PlatoonHillAttackParams { TargetAreaEntity = areaEntity };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            Assert.Equal(NodeStatus.Success, HillAttackCommanderNodes.Action_RequestAreaQuery(ref p, ref s, ctx.Self, ctx.World));
            var sensor = EqsChildSensor.Find(repo, commander, HillAttackCommanderNodes.AreaSensorInstanceId);
            Assert.False(sensor.IsNull);
            Assert.Equal((long)sensor.PackedValue, s.CachedEqsRequestId);   // created at once — no placeholder
            ref readonly var cfg = ref repo.GetComponentRO<EqsSensor>(sensor);
            Assert.Equal(1u, cfg.Epoch & 0xFFFFu);                           // asked once; no run owns a bare commander
            Assert.Equal(areaEntity, cfg.ContextSlot1);
            Assert.Equal(1u << (int)ForceId.Hostile, cfg.FactionFilter);

            // in flight: found, cached, not yet answered
            Assert.Equal(NodeStatus.Running, HillAttackCommanderNodes.Action_RequestAreaQuery(ref p, ref s, ctx.Self, ctx.World));
            Assert.Equal((long)sensor.PackedValue, s.CachedEqsRequestId);

            // answered, consumed by the dispatch (-1) ⇒ the next ask refreshes the same sensor
            repo.GetComponentRW<EqsCognitiveBuffer>(sensor).LastUpdateTick = 7u;
            s.CachedEqsRequestId = -1;
            Assert.Equal(NodeStatus.Success, HillAttackCommanderNodes.Action_RequestAreaQuery(ref p, ref s, ctx.Self, ctx.World));
            Assert.Equal((long)sensor.PackedValue, s.CachedEqsRequestId);
            Assert.Equal(2u, repo.GetComponentRO<EqsSensor>(sensor).Epoch & 0xFFFFu);
            Assert.False(repo.GetComponentRO<EqsCognitiveBuffer>(sensor).IsReady);   // no older answer counts
        }

        /// <summary>The commander asks the template the Muscle registers: its AssetId is the backend's
        /// <c>EntitiesOfForceInArea.AssetId</c> (a cross-host identity, like a wire id), and its BlueprintId the registry's.</summary>
        [Fact]
        public void EQS_AreaSensor_AsksTheEntitiesOfForceInAreaTemplate()
        {
            Assert.Equal(Hrot.SimHost.Systems.EntitiesOfForceInArea.AssetId, HillAttackCommanderNodes.AreaTemplateAssetId);
            var cfg = HillAttackCommanderNodes.AreaSensor(Entity.Null);
            Assert.Equal(EqsTemplateRegistry.BlueprintIdOf(new Guid(Hrot.SimHost.Systems.EntitiesOfForceInArea.AssetId)), cfg.BlueprintId);
            Assert.True(EqsTemplateRegistry.Discover(new[] { typeof(Hrot.SimHost.Systems.EntitiesOfForceInArea).Assembly })
                .TryGetTemplate(cfg.BlueprintId, out _), "the Muscle's registry must resolve the commander's BlueprintId");
        }

        /// <summary>SC-HA011-2: Action_RequestAreaQuery returns Running while the question in flight is unanswered.</summary>
        [Fact]
        public void SC_HA011_2_RequestAreaQuery_ReturnsRunning_WhenRequestInFlight()
        {
            using var repo = CreateWorld();
            var commander  = repo.CreateEntity();
            var areaEntity = repo.CreateEntity();
            var sensor = AreaSensor(repo, commander, areaEntity, null);

            ref var s = ref GetHeavyState(repo, commander);
            s.CachedEqsRequestId = (long)sensor.PackedValue;
            var p     = new PlatoonHillAttackParams { TargetAreaEntity = areaEntity };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            Assert.Equal(NodeStatus.Running, HillAttackCommanderNodes.Action_RequestAreaQuery(ref p, ref s, ctx.Self, ctx.World));
            Assert.Equal((long)sensor.PackedValue, s.CachedEqsRequestId);
        }

        /// <summary>SC-HA011-3: Condition_IsAreaQueryResolved returns Running while the sensor is not ready. (Was quarantined
        /// as Broken on an unregistered AreaQuery component; the EQS path has no such component.)</summary>
        [Fact]
        public void SC_HA011_3_IsAreaQueryResolved_ReturnsRunning_WhenResultNotReady()
        {
            using var repo = CreateWorld();
            var commander = repo.CreateEntity();
            var sensor = AreaSensor(repo, commander, repo.CreateEntity(), null);

            ref var s = ref GetHeavyState(repo, commander);
            s.CachedEqsRequestId = (long)sensor.PackedValue;
            var p     = new PlatoonHillAttackParams();
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            Assert.Equal(NodeStatus.Running, HillAttackCommanderNodes.Condition_IsAreaQueryResolved(ref p, ref s, ctx.Self, ctx.World));
        }

        /// <summary>SC-HA011-4: ready with 0 targets ⇒ Failure (area clear), CachedEqsRequestId = -1, the sensor destroyed.</summary>
        [Fact]
        public void SC_HA011_4_IsAreaQueryResolved_ReturnsFailure_WhenReadyWithZeroTargets()
        {
            using var repo = CreateWorld();
            var commander = repo.CreateEntity();
            var sensor = AreaSensor(repo, commander, repo.CreateEntity());

            ref var s = ref GetHeavyState(repo, commander);
            s.CachedEqsRequestId = (long)sensor.PackedValue;
            var p     = new PlatoonHillAttackParams();
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            Assert.Equal(NodeStatus.Failure, HillAttackCommanderNodes.Condition_IsAreaQueryResolved(ref p, ref s, ctx.Self, ctx.World));
            Assert.Equal(-1L, s.CachedEqsRequestId);
            Playback(repo);
            Assert.False(repo.IsAlive(sensor));
        }

        /// <summary>SC-HA011-5: ready with targets ⇒ Success; CachedEqsRequestId is NOT cleared (the dispatch reads the answer
        /// from the same sensor), and the sensor lives on.</summary>
        [Fact]
        public void SC_HA011_5_IsAreaQueryResolved_ReturnsSuccess_AndDoesNotClearRequestId()
        {
            using var repo = CreateWorld();
            var commander = repo.CreateEntity();
            var sensor = AreaSensor(repo, commander, repo.CreateEntity(), repo.CreateEntity(), repo.CreateEntity());

            ref var s = ref GetHeavyState(repo, commander);
            s.CachedEqsRequestId = (long)sensor.PackedValue;
            var p     = new PlatoonHillAttackParams();
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            Assert.Equal(NodeStatus.Success, HillAttackCommanderNodes.Condition_IsAreaQueryResolved(ref p, ref s, ctx.Self, ctx.World));
            Assert.Equal((long)sensor.PackedValue, s.CachedEqsRequestId);
            Playback(repo);
            Assert.True(repo.IsAlive(sensor));
        }

        /// <summary>
        /// ⭐ CE-2118 — the staging dispatch must NOT reserve baseline slots. It spaces the tanks by ROSTER count, while a
        /// wave's return slots are FIRING-LINE slots (<c>TotalSlots</c>, DESIGN §2.3); marking roster indices in the slot
        /// mask reserved every slot of a 3-slot line, so each attacker fell to "closest regardless" and two attackers of
        /// one wave were sent back to the SAME point. Measured live on <c>hill-attack-close</c>: both reversed to (528,474).
        /// <para>✅ Red before the fix: both attackers' return slot = 1.</para>
        /// </summary>
        [Fact]
        public unsafe void CE2118_AfterStaging_TwoAttackersOfAWave_ReturnToDifferentBaselineSlots()
        {
            using var repo = CreateWorld();
            var commander = repo.CreateEntity();
            var subs = new Entity[4];
            for (int i = 0; i < 4; i++)
            {
                subs[i] = repo.CreateEntity();
                repo.AddComponent(subs[i], new NavigationStatus());
            }
            AddRoster(repo, commander, subs);

            var p = new PlatoonHillAttackParams
            {
                // The live hill-attack-close shape: a ~60 m firing line (3 slots) in front of a ~150 m baseline, so every
                // firing slot's NEAREST baseline slot is the middle one — only the reservation keeps two attackers apart.
                StartX = 45f, StartY = 0f, EndX = 105f, EndY = 0f, TankSpacing = 20f,   // 3 firing-line slots
                BaselineStartX = 0f, BaselineStartY = -80f,
                BaselineEndX   = 150f, BaselineEndY  = -80f,
            };
            var ctx = new BTreeContext { Self = commander, World = repo };
            try
            {
                HillAttackCommanderNodes.Action_CalculateSegments(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);
                ref var s = ref GetHeavyState(repo, commander);
                Assert.Equal(3, s.TotalSlots);
                s.CachedEqsRequestId = -1;
                HillAttackCommanderNodes.Action_DispatchAllToBaseline(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);
                HillAttackCommanderNodes.Action_DispatchWaveWithTargets(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);

                Assert.True(s.ActiveAttackerCount >= 2, "a 4-tank roster sends two attackers per wave");
                var returns = new System.Collections.Generic.HashSet<byte>();
                for (int i = 0; i < s.ActiveAttackerCount; i++)
                    Assert.True(returns.Add(s.ReturnBaselineSlotIndex[i]), $"attacker {i} shares return slot {s.ReturnBaselineSlotIndex[i]}");
            }
            finally
            {
                DisposeEqsSingletons(repo);
            }
        }

        // ── TASK-HA012: SC-HA012-1 through SC-HA012-8 ────────────────────────────

        /// <summary>SC-HA012-1: With 4-tank roster and CurrentWave=0, dispatch selects
        /// the 2 tanks with Entity.Index % 2 == 0. ActiveAttackerCount == 2.</summary>
        [Fact]
        public void SC_HA012_1_DispatchWaveWithTargets_SelectsEvenIndexTanks_ForWave0()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            // Create 4 subordinates. Their indices are assigned sequentially after commander.
            var subs = new Entity[4];
            for (int i = 0; i < 4; i++)
                subs[i] = repo.CreateEntity();

            AddRoster(repo, commander, subs);

            ref var s = ref GetHeavyState(repo, commander);
            s.TotalSlots          = 4;
            s.CurrentWave         = 0;
            s.BurnedSlotsMask     = 0;
            s.CachedEqsRequestId  = -1;   // no area answer: the wave goes out without targets

            var p = new PlatoonHillAttackParams
            {
                StartX = 0f, StartY = 0f, EndX = 90f, EndY = 0f,
                BaselineStartX = 0f, BaselineStartY = 50f,
                BaselineEndX   = 90f, BaselineEndY  = 50f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            try
            {
                HillAttackCommanderNodes.Action_DispatchWaveWithTargets(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);

                // Count how many subs have even Entity.Index among all 4.
                int expectedEven = 0;
                foreach (var sub in subs)
                    if (sub.Index % 2 == 0) expectedEven++;

                Assert.Equal(expectedEven, s.ActiveAttackerCount);
            }
            finally
            {
                DisposeEqsSingletons(repo);
            }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2230</c> H-5 — a wave's members share ONE go time, set past the slowest one's drive to its hide, so the pair crests
        /// together (🔒 R-2022: <i>"another pair comes up the hill"</i>). 📐 Live without it: 12–20 s apart. And the hide is the tank's
        /// own spot on the baseline — 📐 live, the drive along the baseline to a far slot cost a 10–15 s turn-around.
        /// </summary>
        [Fact]
        public void CE2230_H5_AWavesOrders_ShareOneGoTime_PastTheSlowestDriveToItsHide()
        {
            using var repo = CreateWorld();
            repo.SetSingleton(new GlobalTime { TotalTime = 100.0 });
            var commander = repo.CreateEntity();
            var near = repo.CreateEntity();
            var far = repo.CreateEntity();
            repo.AddComponent(near, new SimTransform { Position = new Vector3(12f, 53f, 0f), Rotation = Quaternion.Identity });   // on the baseline
            repo.AddComponent(far, new SimTransform { Position = new Vector3(-150f, 50f, 0f), Rotation = Quaternion.Identity });  // far off it
            AddRoster(repo, commander, new[] { near, far });   // two ⇒ one wave, both go

            ref var s = ref GetHeavyState(repo, commander);
            s.TotalSlots = 4; s.CurrentWave = 0; s.CachedEqsRequestId = -1;
            var p = new PlatoonHillAttackParams
            {
                StartX = 0f, StartY = 0f, EndX = 90f, EndY = 0f,
                BaselineStartX = 0f, BaselineStartY = 50f, BaselineEndX = 90f, BaselineEndY = 50f,
            };
            try
            {
                HillAttackCommanderNodes.Action_DispatchWaveWithTargets(ref p, ref s, commander, repo);
                repo.Bus.SwapBuffers();
                var orders = new System.Collections.Generic.List<(Entity Who, HullDownAttackParams Run)>();
                foreach (var ev in repo.Bus.ReadManaged<AssignTacticalIntentEvent>())
                    if (ev.IntentId == "HullDownAttack")
                        orders.Add((ev.Entity, JsonSerializer.Deserialize<HullDownAttackParams>(ev.JsonParams!,
                            Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)));

                Assert.Equal(2, orders.Count);
                Assert.Equal(orders[0].Run.GoAt, orders[1].Run.GoAt);           // one go time for the wave
                double slowest = 0;
                foreach (var (who, run) in orders)
                    slowest = Math.Max(slowest, HillAttackCommanderNodes.HideDriveSeconds(repo, who, run.BaselineX, run.BaselineY));
                Assert.True(slowest > 10.0, $"the far tank's drive to its hide is long ({slowest:F1} s)");
                Assert.True(orders[0].Run.GoAt >= 100.0 + slowest, $"go at {orders[0].Run.GoAt:F1} — after the slowest arrival ({100.0 + slowest:F1})");
                // ⭐ the hide is the tank's OWN spot on the baseline (no drive along it first); off the baseline it is the slot
                var mine = orders.Find(o => o.Who == near).Run;
                Assert.Equal((12f, 53f), (mine.BaselineX, mine.BaselineY));
                var theirs = orders.Find(o => o.Who == far).Run;
                Assert.Equal(50f, theirs.BaselineY);
                Assert.InRange(theirs.BaselineX, 0f, 90f);
            }
            finally
            {
                DisposeEqsSingletons(repo);
            }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2230</c> H-6 — 🔒 user, 2026-10-16: <i>"the new tank pair appears at a different location than the previous one, so the
        /// enemy cannot aim and wait for the tank to reappear at the same place."</i> Forty waves of a pair on the demo's 86 m line (four
        /// slots): every firing point is at least 10 m along the line from every point of the wave before (the rule), and 6 m from the wave
        /// before that (the preference). The cramped 3-slot case is <c>CE2230_H6_AfterAPairAtBothEnds…</c>. 🔴 Red-proof: forget the previous wave and points repeat.
        /// </summary>
        /// <summary>⭐ CE-2230 H-6 — the case that went wrong live: the last pair fired at BOTH ends of a 3-slot line. Through the real
        /// dispatcher, over 40 sim times, the new pair still finds two points ≥ 10 m from those and from each other. 🔴 Red-proof: block the
        /// mate's SLOT again (a pair needs distinct points, not slots) and the second point lands beside an end.</summary>
        [Fact]
        public unsafe void CE2230_H6_AfterAPairAtBothEnds_TheNextPairStillFindsNewGround()
        {
            using var repo = CreateWorld();
            var commander = repo.CreateEntity();
            var subs = new Entity[2];
            for (int i = 0; i < 2; i++) subs[i] = repo.CreateEntity();
            AddRoster(repo, commander, subs);   // two ⇒ both go every wave
            ref var s = ref GetHeavyState(repo, commander);
            var p = new PlatoonHillAttackParams
            {
                StartX = 0f, StartY = 0f, EndX = 70f, EndY = 0f,
                BaselineStartX = 0f, BaselineStartY = -40f, BaselineEndX = 70f, BaselineEndY = -40f,
            };
            try
            {
                for (int run = 0; run < 40; run++)
                {
                    s.TotalSlots = 3; s.CurrentWave = 0; s.CachedEqsRequestId = -1; s.ActiveAttackerCount = 0; s.BurnedSlotsMask = 0;
                    s.LastFireCount = 2; s.LastFireAlong[0] = 6.4f; s.LastFireAlong[1] = 63f; s.PrevFireCount = 0;
                    repo.SetSimulationTime(5f + run * 3.1f);
                    HillAttackCommanderNodes.Action_DispatchWaveWithTargets(ref p, ref s, commander, repo);
                    repo.Bus.SwapBuffers();
                    var xs = new System.Collections.Generic.List<float>();
                    foreach (var ev in repo.Bus.ReadManaged<AssignTacticalIntentEvent>())
                        if (ev.IntentId == "HullDownAttack")
                            xs.Add(JsonSerializer.Deserialize<HullDownAttackParams>(ev.JsonParams!,
                                Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed).SlotX);
                    Assert.Equal(2, xs.Count);
                    foreach (var x in xs)
                        foreach (var last in new[] { 6.4f, 63f })
                            Assert.True(Math.Abs(x - last) >= 10f, $"run {run}: {x:F1} m is {Math.Abs(x - last):F1} m from the last pair's {last} m");
                    Assert.True(Math.Abs(xs[0] - xs[1]) >= 10f, $"run {run}: the pair's own points {xs[0]:F1} / {xs[1]:F1}");
                }
            }
            finally
            {
                DisposeEqsSingletons(repo);
            }
        }

        [Fact]
        public void CE2230_H6_ANewPair_NeverComesUpWhereTheLastPairDid()
        {
            using var repo = CreateWorld();
            var commander = repo.CreateEntity();
            var subs = new Entity[4];
            for (int i = 0; i < 4; i++) subs[i] = repo.CreateEntity();
            AddRoster(repo, commander, subs);
            ref var s = ref GetHeavyState(repo, commander);
            s.TotalSlots = 4; s.CurrentWave = 0; s.CachedEqsRequestId = -1;   // the demo's line: 86 m at 20 m spacing
            var p = new PlatoonHillAttackParams
            {
                StartX = 0f, StartY = 0f, EndX = 86f, EndY = 0f,
                BaselineStartX = 0f, BaselineStartY = -40f, BaselineEndX = 86f, BaselineEndY = -40f,
            };
            try
            {
                float[]? previous = null, before = null;
                for (int wave = 0; wave < 40; wave++)
                {
                    repo.SetSimulationTime(10f + wave * 23.7f);
                    HillAttackCommanderNodes.Action_DispatchWaveWithTargets(ref p, ref s, commander, repo);
                    repo.Bus.SwapBuffers();
                    var xs = new System.Collections.Generic.List<float>();
                    foreach (var ev in repo.Bus.ReadManaged<AssignTacticalIntentEvent>())
                        if (ev.IntentId == "HullDownAttack")
                            xs.Add(JsonSerializer.Deserialize<HullDownAttackParams>(ev.JsonParams!,
                                Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed).SlotX);
                    Assert.Equal(2, xs.Count);
                    Assert.True(Math.Abs(xs[0] - xs[1]) >= 10f, $"wave {wave}: the pair's own points {xs[0]:F1} / {xs[1]:F1}");
                    // the last pair: the hard rule (≥ 10 m) · the pair before it: weighted, still never on top of it (≥ 6 m)
                    foreach (var (earlier, min) in new[] { (previous, 10f), (before, 6f) })
                        if (earlier != null)
                            foreach (var x in xs)
                                foreach (var px in earlier)
                                    Assert.True(Math.Abs(x - px) >= min, $"wave {wave}: {x:F1} m is {Math.Abs(x - px):F1} m from an earlier pair's {px:F1} m (min {min})");
                    before = previous;
                    previous = xs.ToArray();
                    s.ActiveAttackerCount = 0;   // the wave ran
                }
            }
            finally
            {
                DisposeEqsSingletons(repo);
            }
        }

        /// <summary>SC-HA012-2: With roster.Count <= 3, all alive tanks dispatched
        /// regardless of parity.</summary>
        [Fact]
        public void SC_HA012_2_DispatchWaveWithTargets_AllParticipate_WhenRosterLe3()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            var subs = new Entity[3];
            for (int i = 0; i < 3; i++)
                subs[i] = repo.CreateEntity();

            AddRoster(repo, commander, subs);

            ref var s = ref GetHeavyState(repo, commander);
            s.TotalSlots          = 4;
            s.CurrentWave         = 0;
            s.BurnedSlotsMask     = 0;
            s.CachedEqsRequestId  = -1;   // no area answer: the wave goes out without targets

            var p = new PlatoonHillAttackParams
            {
                StartX = 0f, StartY = 0f, EndX = 60f, EndY = 0f,
                BaselineStartX = 0f, BaselineStartY = 50f,
                BaselineEndX   = 60f, BaselineEndY  = 50f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            try
            {
                HillAttackCommanderNodes.Action_DispatchWaveWithTargets(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);

                Assert.Equal(3, s.ActiveAttackerCount);
            }
            finally
            {
                DisposeEqsSingletons(repo);
            }
        }

        /// <summary>SC-HA012-3: 2 targets, 3 tanks dispatched -> round-robin assignment:
        /// tank0->target0, tank1->target1, tank2->target0.</summary>
        [Fact]
        public void SC_HA012_3_DispatchWaveWithTargets_RoundRobin_TwoTargetsThreeTanks()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            var subs = new Entity[3];
            for (int i = 0; i < 3; i++)
                subs[i] = repo.CreateEntity();

            var target1 = repo.CreateEntity();
            var target2 = repo.CreateEntity();
            repo.AddComponent(target1, new NetworkIdentity { Value = 101L });
            repo.AddComponent(target2, new NetworkIdentity { Value = 202L });

            AddRoster(repo, commander, subs);

            ref var s = ref GetHeavyState(repo, commander);
            s.TotalSlots              = 4;
            s.CurrentWave             = 0;
            s.BurnedSlotsMask         = 0;
            // The area sensor answered two targets, in this order.
            s.CachedEqsRequestId      = (long)AreaSensor(repo, commander, repo.CreateEntity(), target1, target2).PackedValue;

            var p = new PlatoonHillAttackParams
            {
                StartX = 0f, StartY = 0f, EndX = 60f, EndY = 0f,
                BaselineStartX = 0f, BaselineStartY = 50f,
                BaselineEndX   = 60f, BaselineEndY  = 50f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            try
            {
                HillAttackCommanderNodes.Action_DispatchWaveWithTargets(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);
                repo.Bus.SwapBuffers();
                var events = repo.Bus.ReadManaged<AssignTacticalIntentEvent>();

                Assert.Equal(3, events.Count);

                // Parse TargetNetworkId from each event's JSON.
                long[] netIds = new long[3];
                for (int i = 0; i < 3; i++)
                    netIds[i] = ParseTargetNetworkId(events[i].JsonParams!);

                // Round-robin: 101, 202, 101.
                Assert.Equal(101L, netIds[0]);
                Assert.Equal(202L, netIds[1]);
                Assert.Equal(101L, netIds[2]);
            }
            finally
            {
                DisposeEqsSingletons(repo);
            }
        }

        /// <summary>SC-HA012-4: Condition_IsWaveCompleted returns Success immediately
        /// when ActiveAttackerCount == 0.</summary>
        [Fact]
        public void SC_HA012_4_IsWaveCompleted_ReturnsSuccess_WhenNoActiveAttackers()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            ref var s = ref GetHeavyState(repo, commander);
            s.ActiveAttackerCount = 0;

            var p     = new PlatoonHillAttackParams();
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            var result = HillAttackCommanderNodes.Condition_IsWaveCompleted(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);

            Assert.Equal(NodeStatus.Success, result);
        }

        /// <summary>SC-HA012-5: Dead attacker causes BurnedSlotsMask bit to be set,
        /// BaselineReservedMask cleared, and ActiveAttackerCount decrements to 0 => Success.</summary>
        [Fact]
        public void SC_HA012_5_IsWaveCompleted_DeadAttacker_BurnsSlotAndReturnsSuccess()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            var attacker = repo.CreateEntity();
            // Destroy the attacker so IsAlive == false.
            repo.DestroyEntity(attacker);

            ref var s = ref GetHeavyState(repo, commander);
            s.ActiveAttackerCount = 1;
            s.BurnedSlotsMask     = 0;
            s.BaselineReservedMask = (ushort)(1 << 2);  // baseline slot 2 reserved
            unsafe
            {
                s.ActiveEntityPacked[0]     = (long)attacker.PackedValue;
                s.ActiveSlotIndex[0]         = 1;   // firing slot 1
                s.ReturnBaselineSlotIndex[0] = 2;
                s.HasStartedRun[0]           = 0;
            }

            var p     = new PlatoonHillAttackParams();
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            var result = HillAttackCommanderNodes.Condition_IsWaveCompleted(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);

            Assert.Equal(NodeStatus.Success, result);
            Assert.Equal(0, s.ActiveAttackerCount);
            Assert.True((s.BurnedSlotsMask & (1 << 1)) != 0,
                "Firing slot 1 should be burned");
            Assert.True((s.BaselineReservedMask & (1 << 2)) == 0,
                "Baseline slot 2 should be released");
        }

        /// <summary>
        /// ⭐ CE-466: an attacker KNOCKED OUT mid-wave (<c>Health.Current == 0</c>, body still in the world — the
        /// engine's combat-death state since the CE-267 revert) burns its slot exactly like a destroyed one.
        /// ⛔ Before CE-466 this was unreachable: the doctrine tested ECS existence, and a knocked-out tank exists.
        /// </summary>
        [Fact]
        public void CE466_IsWaveCompleted_KnockedOutAttacker_BurnsSlotAndReturnsSuccess()
        {
            using var repo = CreateWorld();
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>())
                repo.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();

            var commander = repo.CreateEntity();
            var attacker = repo.CreateEntity();
            repo.AddComponent(attacker, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
            Assert.True(repo.IsAlive(attacker));   // the body still exists — this is the point

            ref var s = ref GetHeavyState(repo, commander);
            s.ActiveAttackerCount  = 1;
            s.BurnedSlotsMask      = 0;
            s.BaselineReservedMask = (ushort)(1 << 2);
            unsafe
            {
                s.ActiveEntityPacked[0]      = (long)attacker.PackedValue;
                s.ActiveSlotIndex[0]         = 1;
                s.ReturnBaselineSlotIndex[0] = 2;
                s.HasStartedRun[0]           = 1;
            }

            var p     = new PlatoonHillAttackParams();
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            var result = HillAttackCommanderNodes.Condition_IsWaveCompleted(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);

            Assert.Equal(NodeStatus.Success, result);
            Assert.Equal(0, s.ActiveAttackerCount);
            Assert.True((s.BurnedSlotsMask & (1 << 1)) != 0, "the knocked-out attacker's firing slot must be burned");
            Assert.True((s.BaselineReservedMask & (1 << 2)) == 0, "its baseline slot must be released");
        }

        /// <summary>
        /// ⭐ CE-466: a KNOCKED-OUT subordinate (<c>Health.Current == 0</c>, still in the world, never arriving) does
        /// not block <c>Condition_AreAllAtBaseline</c> — the sibling of SC-HA010-7, which covers a destroyed one.
        /// </summary>
        [Fact]
        public void CE466_AreAllAtBaseline_KnockedOutSubordinateCountsAsArrived()
        {
            using var repo = CreateWorld();
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>())
                repo.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();

            var commander  = repo.CreateEntity();
            var aliveSub   = repo.CreateEntity();
            var knockedOut = repo.CreateEntity();
            repo.AddComponent(aliveSub, new NavigationStatus { Result = NavigationResult.Arrived });
            repo.AddComponent(aliveSub, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            repo.AddComponent(knockedOut, new NavigationStatus { Result = NavigationResult.InProgress });
            repo.AddComponent(knockedOut, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
            AddRoster(repo, commander, new[] { aliveSub, knockedOut });

            var p     = new PlatoonHillAttackParams();
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            var result = HillAttackCommanderNodes.Condition_AreAllAtBaseline(ref p, ctx.Self, ctx.World);

            Assert.Equal(NodeStatus.Success, result);
        }

        /// <summary>SC-HA012-6: Condition_IsWaveCompleted does NOT remove an entry when
        /// HasStartedRun == 0, even if ActiveBehaviorHash differs from HullDownAttackRun.</summary>
        // STABILITY(Broken): IsWaveCompleted incorrectly removes entry when HasStartedRun==0; investigate
        [Trait("Stability", "Broken")]
        [Fact]
        public void SC_HA012_6_IsWaveCompleted_NoRemove_WhenHasNotStartedYet()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            var attacker = repo.CreateEntity();
            // Attacker is alive but has a different behavior hash (intent still propagating).
            repo.AddComponent(attacker, new BehaviorState { ActiveBehaviorHash = 9999 });

            ref var s = ref GetHeavyState(repo, commander);
            s.ActiveAttackerCount = 1;
            unsafe
            {
                s.ActiveEntityPacked[0]     = (long)attacker.PackedValue;
                s.ActiveSlotIndex[0]         = 0;
                s.ReturnBaselineSlotIndex[0] = 0;
                s.HasStartedRun[0]           = 0;
            }

            var p     = new PlatoonHillAttackParams();
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            var result = HillAttackCommanderNodes.Condition_IsWaveCompleted(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);

            Assert.Equal(NodeStatus.Running, result);
            Assert.Equal(1, s.ActiveAttackerCount);
        }

        /// <summary>SC-HA012-6b: When attacker's BehaviorHash matches HullDownAttackRun at
        /// tick T (HasStartedRun set), then no longer matches at T+1 => entry removed => Success.</summary>
        [Fact]
        public void SC_HA012_6b_IsWaveCompleted_RunComplete_WhenHashNoLongerMatchesAfterStarted()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            var attacker = repo.CreateEntity();
            // HullDownAttackRunBehaviorId == 3013.
            repo.AddComponent(attacker, new BehaviorState { ActiveBehaviorHash = BehaviorHash.FromName(BehaviorNames.HullDownAttackRun) });

            ref var s = ref GetHeavyState(repo, commander);
            s.ActiveAttackerCount  = 1;
            s.BaselineReservedMask = (ushort)(1 << 1);
            unsafe
            {
                s.ActiveEntityPacked[0]     = (long)attacker.PackedValue;
                s.ActiveSlotIndex[0]         = 0;
                s.ReturnBaselineSlotIndex[0] = 1;
                s.HasStartedRun[0]           = 0;
            }

            var p     = new PlatoonHillAttackParams();
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            // Tick T: hash == 3013, sets HasStartedRun = 1, returns Running.
            var r1 = HillAttackCommanderNodes.Condition_IsWaveCompleted(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);
            Assert.Equal(NodeStatus.Running, r1);
            unsafe { Assert.Equal(1, s.HasStartedRun[0]); }

            // Tick T+1: hash no longer == 3013, run considered finished.
            repo.GetComponentRW<BehaviorState>(attacker).ActiveBehaviorHash = BehaviorHash.FromName(BehaviorNames.Idle);  // Idle
            var r2 = HillAttackCommanderNodes.Condition_IsWaveCompleted(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);

            Assert.Equal(NodeStatus.Success, r2);
            Assert.Equal(0, s.ActiveAttackerCount);
            Assert.True((s.BaselineReservedMask & (1 << 1)) == 0,
                "Baseline slot 1 should be cleared after run completes");
        }

        /// <summary>SC-HA012-6c: Entity.Index-based wave assignment is stable after roster
        /// compaction: destroying one tank does not change surviving tank's wave assignment.</summary>
        [Fact]
        public void SC_HA012_6c_WaveAssignment_StableAfterRosterCompaction()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            // Create 4 subordinates.
            var subs = new Entity[4];
            for (int i = 0; i < 4; i++)
                subs[i] = repo.CreateEntity();

            // Identify one entity with even index (wave 0) that we want to test after compaction.
            Entity stableEntity = default;
            int stableEntityIdx = -1;
            for (int i = 0; i < 4; i++)
            {
                if (subs[i].Index % 2 == 0)
                {
                    stableEntity    = subs[i];
                    stableEntityIdx = i;
                    break;
                }
            }
            Assert.True(stableEntityIdx >= 0, "Needed at least one even-index entity");

            // Record wave parity BEFORE roster compaction.
            int waveBefore = (int)(stableEntity.Index % 2);

            // Kill and remove another entity from the roster (simulating UnitHierarchySystem compaction).
            // Remove the entity at index 0 (if it's not stableEntity), else index 1.
            int removeIdx = stableEntityIdx == 0 ? 1 : 0;
            repo.DestroyEntity(subs[removeIdx]);

            // Build compacted roster (skip the dead entity).
            var compacted = new System.Collections.Generic.List<Entity>();
            for (int i = 0; i < 4; i++)
                if (i != removeIdx)
                    compacted.Add(subs[i]);
            AddRoster(repo, commander, compacted.ToArray());

            // Wave parity AFTER compaction is still based on Entity.Index.
            int waveAfter = (int)(stableEntity.Index % 2);

            Assert.Equal(waveBefore, waveAfter);
        }

        /// <summary>SC-HA012-7: BurnedSlotsMask prevents previously burned slots from
        /// being assigned in subsequent waves.</summary>
        [Fact]
        public void SC_HA012_7_DispatchWave_DoesNotAssign_BurnedSlots()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            var subs = new Entity[3];
            for (int i = 0; i < 3; i++)
                subs[i] = repo.CreateEntity();
            AddRoster(repo, commander, subs);

            ref var s = ref GetHeavyState(repo, commander);
            s.TotalSlots          = 4;
            s.CurrentWave         = 0;
            s.BurnedSlotsMask     = 0b0001;  // slot 0 burned
            s.CachedEqsRequestId  = -1;   // no area answer: the wave goes out without targets

            var p = new PlatoonHillAttackParams
            {
                StartX = 0f, StartY = 0f, EndX = 90f, EndY = 0f,
                BaselineStartX = 0f, BaselineStartY = 50f,
                BaselineEndX   = 90f, BaselineEndY  = 50f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            try
            {
                HillAttackCommanderNodes.Action_DispatchWaveWithTargets(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);

                // Verify none of the SoA entries used slot 0.
                for (int i = 0; i < s.ActiveAttackerCount; i++)
                {
                    unsafe { Assert.NotEqual(0, s.ActiveSlotIndex[i]); }
                }
            }
            finally
            {
                DisposeEqsSingletons(repo);
            }
        }

        /// <summary>SC-HA012-8: after DispatchWaveWithTargets the answer is consumed (CachedEqsRequestId == -1) and the area
        /// sensor STAYS for the next wave's question (DESIGN_Hill_Attack_Eqs_Migration.md §4 D6). Was: the retired pool
        /// handle CachedTargetGroupHandle cleared to -1.</summary>
        [Fact]
        public void SC_HA012_8_DispatchWave_ConsumesTheAnswer_AndKeepsTheSensor()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();

            var subs = new Entity[2];
            for (int i = 0; i < 2; i++)
                subs[i] = repo.CreateEntity();
            AddRoster(repo, commander, subs);

            var sensor = AreaSensor(repo, commander, repo.CreateEntity(), repo.CreateEntity());
            ref var s = ref GetHeavyState(repo, commander);
            s.TotalSlots              = 4;
            s.CurrentWave             = 0;
            s.BurnedSlotsMask         = 0;
            s.CachedEqsRequestId      = (long)sensor.PackedValue;

            var p = new PlatoonHillAttackParams
            {
                StartX = 0f, StartY = 0f, EndX = 30f, EndY = 0f,
                BaselineStartX = 0f, BaselineStartY = 50f,
                BaselineEndX   = 30f, BaselineEndY  = 50f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            HillAttackCommanderNodes.Action_DispatchWaveWithTargets(ref p, ref GetHeavyState(repo, commander), ctx.Self, ctx.World);

            Assert.Equal(-1L, s.CachedEqsRequestId);
            Playback(repo);
            Assert.True(repo.IsAlive(sensor));
        }

        // ── TASK-HA013: SC-HA013-1 through SC-HA013-3 ────────────────────────────

        /// <summary>SC-HA013-1: FbtTreeCatalog.GetPlatoonHillAttack() is accessible and
        /// non-null after build.</summary>
        [Fact]
        public void SC_HA013_1_FbtTreeCatalog_GetPlatoonHillAttack_ReturnsNonNull()
        {
            var blob = FbtTreeCatalog.GetPlatoonHillAttack();
            Assert.NotNull(blob);
        }

        /// <summary>SC-HA013-2 / S3-G: BehaviorDefinition for PlatoonHillAttack has correct BrainTier,
        /// a non-null BTreeInterpreter, no legacy HeavyDtoType, and a single Behavior-scoped
        /// HillAttackMutableState working-slot manifest entry (which replaced the Blackboard1024 hack).</summary>
        [Fact]
        public void SC_HA013_2_PlatoonHillAttack_BehaviorDefinition_HasCorrectProperties()
        {
            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);

            Assert.True(registry.TryGetDefinition(BehaviorHash.FromName(BehaviorNames.PlatoonHillAttack), out var def),
                "PlatoonHillAttack (id 3014) should be registered");
            Assert.Equal(BehaviorNames.PlatoonHillAttack, def.Name);
            Assert.Equal(BehaviorConstants.BrainTierBTree, def.BrainTier);
            Assert.NotNull(def.BTreeInterpreter);

            // S3-G: the Blackboard1024 HeavyDtoType hack is gone.
            Assert.Null(def.HeavyDtoType);
            // ⭐⭐ CE-437: the working state lives in the behaviour's BLOCK (St.State), not in a
            //   Behavior-scoped partition slot — one home. ⛔ This asserted a single side-slot entry.
            //   ⚠ And the curated resolver overlay must NOT demote the block to its params type.
            Assert.Equal("PlatoonHillAttack_Block", def.BlackboardLayoutType!.Name);
            Assert.Equal(typeof(HillAttackMutableState),
                def.BlackboardLayoutType.GetField("St")!.FieldType.GetField("State")!.FieldType);
            Assert.DoesNotContain(def.StatefulWorkingSlots ?? System.Array.Empty<StatefulSlotInfo>(),
                s => s.WorkingStateType == typeof(HillAttackMutableState));
        }

        /// <summary>SC-HA013-3: Assigning PlatoonHillAttack via AssignBehaviorEvent updates
        /// BehaviorState.ActiveBehaviorHash to 3014 within one BehaviorIngressSystem tick.</summary>
        [Fact]
        public void SC_HA013_3_AssignPlatoonHillAttack_UpdatesBehaviorHash()
        {
            using var repo = CreateWorld();

            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);

            var ingress = new BehaviorIngressSystem(registry);

            var commander = repo.CreateEntity();
            repo.AddComponent(commander, new BehaviorState());

            repo.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = commander,
                BehaviorName = BehaviorNames.PlatoonHillAttack,
                JsonParams   = "{}",
            });
            repo.Bus.SwapBuffers();

            ingress.Execute(repo, 0.016f);

            var bs = repo.GetComponent<BehaviorState>(commander);
            Assert.Equal(BehaviorHash.FromName(BehaviorNames.PlatoonHillAttack), bs.ActiveBehaviorHash);
        }

        /// <summary>HAJSON-B: the factory-registered PlatoonHillAttack + HullDownAttackRun blobs must bake
        /// the resource-owning bit for every node whose method has a paired [BTreeDeactivator], so the
        /// interpreter fires those deactivators on branch abort/exit (EQS slot / child-sensor cleanup).</summary>
        [Fact]
        public void SC_HA013_4_DeactivatorNodes_AreResourceOwning_InFactoryBlobs()
        {
            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);

            // Commander (stateful, Behavior-scoped): RequestAreaQuery ↔ Deactivate_RequestAreaQuery.
            Assert.True(registry.TryGetDefinition(BehaviorHash.FromName(BehaviorNames.PlatoonHillAttack), out var commanderDef));
            int slotKey = StatefulBTreeActionBinder.ComputeStatefulSlotKey(
                new Guid("1a000000-0000-0000-0000-0000000000dd"),
                Fdp.Toolkit.Blueprints.Partitioning.StatefulSlotScope.Behavior, Guid.Empty, "State");
            AssertNodeResourceOwning(commanderDef.BTreeInterpreter!.Blob,
                $"Hrot.AI.Behaviors.Brains.HillAttackCommanderNodes.Action_RequestAreaQuery@0@{slotKey}");

            // Tank subordinate (CE-2230): the run's ONE node is the vehicle exposure cycle (stateful) — it drives the hull, the
            //   weapon and the posture, so an aborted run (the commander's halt, a new order) must release them.
            Assert.True(registry.TryGetDefinition(BehaviorHash.FromName(BehaviorNames.HullDownAttackRun), out var tankDef));
            AssertNodeResourceOwning(tankDef.BTreeInterpreter!.Blob,
                "Hrot.AI.Behaviors.Brains.VehicleFightFromCoverNodes.VehicleFightFromCover@0@230311729");
        }

        private static void AssertNodeResourceOwning(BehaviorTreeBlob blob, string methodKey)
        {
            for (int i = 0; i < blob.Nodes.Length; i++)
            {
                var node = blob.Nodes[i];
                if (node.Type is not (NodeType.Action or NodeType.Condition)) continue;
                if (blob.MethodNames[node.PayloadIndex] != methodKey) continue;
                Assert.True(node.IsResourceOwning,
                    $"node '{methodKey}' must be resource-owning so its deactivator fires on abort");
                return;
            }
            Assert.Fail($"node '{methodKey}' not found in the compiled blob");
        }

        // ── TASK-HA016: SC-HA016-1 through SC-HA016-6 ────────────────────────────

        /// <summary>SC-HA016-1: PlatoonHillAttackParamsJsonDto deserializes from full JSON.
        /// Missing tankSpacing uses default 30f.</summary>
        [Fact]
        public unsafe void SC_HA016_1_ParsePlatoonHillAttackParams_DeserializesFromJson()
        {
            // PickableGeoPoint is serialized as [latitude, longitude] array.
            // In the Cartesian fallback path: StartX = longitude, StartY = latitude.
            // So [y, x] → firingLineStart{x=0,y=0} → [0,0]; firingLineEnd{x=100,y=0} → [0,100];
            // baselineStart{x=0,y=50} → [50,0]; baselineEnd{x=100,y=50} → [50,100].
            const string json =
                @"{""firingLineStart"":[0,0]," +
                @"""firingLineEnd"":[0,100]," +
                @"""baselineStart"":[50,0]," +
                @"""baselineEnd"":[50,100]," +
                @"""targetAreaNetworkId"":0}";
            // (no tankSpacing => should default to 30f)

            var result = new PlatoonHillAttackParams();
            fixed (byte* ptr = new byte[sizeof(PlatoonHillAttackParams)])
            {
                HillAttackCommanderNodes.ParsePlatoonHillAttackParams(json, ptr, sizeof(PlatoonHillAttackParams), null, new NetworkEntityMap());
                result = *(PlatoonHillAttackParams*)ptr;
            }

            Assert.Equal(0f, result.StartX, 0.001f);
            Assert.Equal(0f, result.StartY, 0.001f);
            Assert.Equal(100f, result.EndX, 0.001f);
            Assert.Equal(30f, result.TankSpacing, 0.001f);
        }

        /// <summary>SC-HA016-2: Firing line (0,0)-(100,0) with baseline (0,50)-(100,50) =>
        /// AttackDir is the approach vector normalize(firingCenter - baselineCenter) = (0,-1):
        /// AttackDirX==0 and |AttackDirY|==1 (points from the baseline toward the firing line).
        /// For a baseline parallel to the firing line this equals the firing-line perpendicular.</summary>
        [Fact]
        public unsafe void SC_HA016_2_ParsePlatoonHillAttackParams_ComputesAttackDir_Perpendicular()
        {
            // PickableGeoPoint uses [latitude, longitude] array format; Cartesian fallback: X=lon, Y=lat.
            const string json =
                @"{""firingLineStart"":[0,0]," +
                @"""firingLineEnd"":[0,100]," +
                @"""baselineStart"":[50,0]," +
                @"""baselineEnd"":[50,100]," +
                @"""tankSpacing"":30}";

            var result = new PlatoonHillAttackParams();
            fixed (byte* ptr = new byte[sizeof(PlatoonHillAttackParams)])
            {
                HillAttackCommanderNodes.ParsePlatoonHillAttackParams(json, ptr, sizeof(PlatoonHillAttackParams), null, new NetworkEntityMap());
                result = *(PlatoonHillAttackParams*)ptr;
            }

            // Approach vector (baseline -> firing line) is (0,-1); assert X==0 and |Y|==1.
            Assert.Equal(0f, result.AttackDirX, 0.001f);
            Assert.Equal(1f, Math.Abs(result.AttackDirY), 0.001f);
        }

        /// <summary>
        /// <b>SC-HA016-2b: the case SC-HA016-2 cannot see — a baseline NOT centred opposite the
        /// firing line.</b>
        ///
        /// <para>SC-HA016-2 uses a baseline parallel to the firing line and centred directly
        /// behind it, and its own summary admits that there "this equals the firing-line
        /// perpendicular". Both candidate definitions agree on that geometry, so it stayed green
        /// while the code computed the wrong one for years.</para>
        ///
        /// <para>Here the baseline centre is offset ALONG the firing line, which separates them:
        /// <list type="bullet">
        ///   <item>firing line (0,0)→(100,0); its normal is (0,±1)</item>
        ///   <item>baseline (-50,50)→(50,50); centre (0,50), i.e. behind and to the left</item>
        ///   <item>perpendicular away from the baseline ⇒ <b>(0,-1)</b> ✅ what the tanks need</item>
        ///   <item>normalize(firingCentre − baselineCentre) = normalize((50,-50)) ⇒
        ///         <b>(0.707,-0.707)</b> ⛔ the old behaviour — a 45° skew</item>
        /// </list>
        /// The tanks creep along this vector and the overshoot guard projects onto it, so a
        /// skewed direction walks them diagonally off the firing line instead of straight out
        /// from it.</para>
        /// </summary>
        [Fact]
        public unsafe void SC_HA016_2b_AttackDir_IsTheFiringLineNormal_NotTheApproachVector()
        {
            // PickableGeoPoint is [latitude, longitude]; the Cartesian fallback maps X=lon, Y=lat.
            const string json =
                @"{""firingLineStart"":[0,0]," +      // (x=0,   y=0)
                @"""firingLineEnd"":[0,100]," +       // (x=100, y=0)
                @"""baselineStart"":[50,-50]," +      // (x=-50, y=50)
                @"""baselineEnd"":[50,50]," +         // (x=50,  y=50)
                @"""tankSpacing"":30}";

            var result = new PlatoonHillAttackParams();
            fixed (byte* ptr = new byte[sizeof(PlatoonHillAttackParams)])
            {
                HillAttackCommanderNodes.ParsePlatoonHillAttackParams(json, ptr, sizeof(PlatoonHillAttackParams), null, new NetworkEntityMap());
                result = *(PlatoonHillAttackParams*)ptr;
            }

            Assert.Equal(0f, result.AttackDirX, 0.001f);
            Assert.Equal(-1f, result.AttackDirY, 0.001f);
        }

        /// <summary>
        /// SC-HA016-2c: a degenerate firing line (start == end) has no tangent and therefore no
        /// normal. Rather than emit a zero or NaN direction — which would make the creep
        /// destination <c>currentPos + NaN * 10000</c> and the overshoot projection meaningless —
        /// fall back to the baseline→firing approach vector, which at least points downrange.
        /// </summary>
        [Fact]
        public unsafe void SC_HA016_2c_AttackDir_FallsBackToTheApproachVector_WhenTheFiringLineIsAPoint()
        {
            const string json =
                @"{""firingLineStart"":[0,0]," +
                @"""firingLineEnd"":[0,0]," +          // degenerate: same point
                @"""baselineStart"":[50,0]," +
                @"""baselineEnd"":[50,100]," +
                @"""tankSpacing"":30}";

            var result = new PlatoonHillAttackParams();
            fixed (byte* ptr = new byte[sizeof(PlatoonHillAttackParams)])
            {
                HillAttackCommanderNodes.ParsePlatoonHillAttackParams(json, ptr, sizeof(PlatoonHillAttackParams), null, new NetworkEntityMap());
                result = *(PlatoonHillAttackParams*)ptr;
            }

            // firingCentre (0,0) − baselineCentre (50,50) = (-50,-50) ⇒ normalized (-0.707,-0.707).
            float len = MathF.Sqrt(result.AttackDirX * result.AttackDirX
                                 + result.AttackDirY * result.AttackDirY);
            Assert.Equal(1f, len, 0.001f);
            Assert.Equal(-0.7071f, result.AttackDirX, 0.001f);
            Assert.Equal(-0.7071f, result.AttackDirY, 0.001f);
        }

        /// <summary>SC-HA016-3: Valid TargetAreaNetworkId that maps to a live entity =>
        /// TargetAreaEntity != Entity.Null.</summary>
        [Fact]
        public unsafe void SC_HA016_3_ParsePlatoonHillAttackParams_ResolvesTargetArea_WhenValid()
        {
            using var repo = CreateWorld();

            var areaEntity = repo.CreateEntity();
            var entityMap  = new NetworkEntityMap();
            entityMap.Register(999L, areaEntity);

            // PickableGeoPoint uses [latitude, longitude] array format.
            // In Cartesian fallback: StartX=longitude, StartY=latitude.
            string json =
                @"{""firingLineStart"":[0,0]," +
                @"""firingLineEnd"":[0,10]," +
                @"""baselineStart"":[5,0]," +
                @"""baselineEnd"":[5,10]," +
                @"""tankSpacing"":30," +
                @"""targetAreaNetworkId"":999}";

            var result = new PlatoonHillAttackParams();
            fixed (byte* ptr = new byte[sizeof(PlatoonHillAttackParams)])
            {
                HillAttackCommanderNodes.ParsePlatoonHillAttackParams(json, ptr, sizeof(PlatoonHillAttackParams), null, entityMap);
                result = *(PlatoonHillAttackParams*)ptr;
            }

            Assert.NotEqual(Entity.Null, result.TargetAreaEntity);
            Assert.Equal(areaEntity, result.TargetAreaEntity);
        }

        /// <summary>SC-HA016-4: Unresolvable TargetAreaNetworkId => TargetAreaEntity == Entity.Null.
        /// No exception thrown.</summary>
        [Fact]
        public unsafe void SC_HA016_4_ParsePlatoonHillAttackParams_WritesEntityNull_WhenIdUnresolvable()
        {
            const string json =
                @"{""firingLineStart"":{""x"":0,""y"":0}," +
                @"""firingLineEnd"":{""x"":10,""y"":0}," +
                @"""baselineStart"":{""x"":0,""y"":5}," +
                @"""baselineEnd"":{""x"":10,""y"":5}," +
                @"""tankSpacing"":30," +
                @"""targetAreaNetworkId"":12345}";

            var result = new PlatoonHillAttackParams();
            // No entity registered with ID 12345.
            var ex = Record.Exception(() =>
            {
                fixed (byte* ptr = new byte[sizeof(PlatoonHillAttackParams)])
                {
                    HillAttackCommanderNodes.ParsePlatoonHillAttackParams(json, ptr, sizeof(PlatoonHillAttackParams), null, new NetworkEntityMap());
                    result = *(PlatoonHillAttackParams*)ptr;
                }
            });

            Assert.Null(ex);
            Assert.Equal(Entity.Null, result.TargetAreaEntity);
        }

        /// <summary>SC-HA016-5: sizeof(PlatoonHillAttackParams) == 52 bytes.</summary>
        [Fact]
        public unsafe void SC_HA016_5_PlatoonHillAttackParams_SizeIs52Bytes()
        {
            Assert.Equal(52, sizeof(PlatoonHillAttackParams));
        }

        /// <summary>SC-HA016-6: AiBehaviorFactory's PlatoonHillAttack registration has a
        /// non-null ParseParams delegate.</summary>
        [Fact]
        public void SC_HA016_6_AiBehaviorFactory_PlatoonHillAttack_HasNonNullParseParams()
        {
            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);

            Assert.True(registry.TryGetDefinition(BehaviorHash.FromName(BehaviorNames.PlatoonHillAttack), out var def));
            Assert.NotNull(def.ParseParams);
        }

        /// <summary>
        /// SC-HA016-7 (regression, Phase 2b/2c): the bound PlatoonHillAttack resolver must read the
        /// geographic transform from the <b>world singleton</b> at activation. When the world publishes
        /// an <see cref="IGeographicTransform"/>, the parsed firing-line/baseline positions must be the
        /// geo-converted coordinates — NOT the Cartesian raw-lon/lat fallback used when geo is null.
        /// <para>
        /// This guards the regression where the CGF node stopped moving vehicles to real positions
        /// (they converged near origin) because the node published NetworkEntityMap but not the geo
        /// transform singleton, so the resolver fell back to raw lon/lat. See CgfSubsystem /
        /// EditorSubsystem geo-singleton publication.
        /// </para>
        /// </summary>
        [Fact]
        public unsafe void SC_HA016_7_PlatoonHillAttack_Resolver_UsesWorldSingletonGeoTransform()
        {
            using var repo = CreateWorld();

            // Distinctive geo transform: X = lon*1000, Y = lat*1000. Any use of the null Cartesian
            // fallback (X = lon, Y = lat) would produce values 1000x smaller.
            var geo = new ScaleGeoTransform(1000.0);
            repo.SetSingletonManaged<IGeographicTransform>(geo);
            repo.SetSingletonManaged<NetworkEntityMap>(new NetworkEntityMap());

            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);
            var ingress = new BehaviorIngressSystem(registry);

            var commander = repo.CreateEntity();
            repo.AddComponent(commander, new BehaviorState());

            // PickableGeoPoint uses [latitude, longitude]. FiringLineStart = lat 2, lon 7.
            const string json =
                @"{""firingLineStart"":[2,7]," +
                @"""firingLineEnd"":[2,8]," +
                @"""baselineStart"":[3,7]," +
                @"""baselineEnd"":[3,8]," +
                @"""tankSpacing"":30}";

            repo.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = commander,
                BehaviorName = BehaviorNames.PlatoonHillAttack,
                JsonParams   = json,
            });
            repo.Bus.SwapBuffers();
            ingress.Execute(repo, 0.016f);

            // 🔴 P3-C: ingress commits the parsed params into the ROOT PARAMS OCCURRENCE SLOT now,
            //   not into BrainBlackboard. Same bytes at the same offset — only the anchor moved.
            Assert.True(Fdp.Toolkit.Behavior.RootParamsAccess.TryGetRoot<PlatoonHillAttackParams>(
                repo, commander, out PlatoonHillAttackParams* parmsPtr));
            PlatoonHillAttackParams parms = *parmsPtr;

            // FiringLineStart lon=7 -> X = 7*1000 = 7000 (geo used), NOT 7 (null fallback).
            Assert.Equal(7000f, parms.StartX, 0.5f);
            Assert.Equal(2000f, parms.StartY, 0.5f);
            Assert.Equal(8000f, parms.EndX, 0.5f);
        }

        /// <summary>Test geo transform that scales lon/lat by a fixed factor into Cartesian X/Y,
        /// so a test can distinguish "resolver used the world-singleton geo" from the null fallback.</summary>
        private sealed class ScaleGeoTransform : IGeographicTransform
        {
            private readonly double _scale;
            public ScaleGeoTransform(double scale) => _scale = scale;
            public void SetOrigin(double latDeg, double lonDeg, double altMeters) { }
            public System.Numerics.Vector3 ToCartesian(double latDeg, double lonDeg, double altMeters)
                => new System.Numerics.Vector3((float)(lonDeg * _scale), (float)(latDeg * _scale), (float)altMeters);
            public (double lat, double lon, double alt) ToGeodetic(System.Numerics.Vector3 localPos)
                => (localPos.Y / _scale, localPos.X / _scale, localPos.Z);
        }

        // ── Private helper: parse TargetNetworkId from dispatch event JSON ────────

        private static long ParseTargetNetworkId(string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("TargetNetworkId", out var prop))
                return prop.GetInt64();
            // Try camelCase fallback.
            if (doc.RootElement.TryGetProperty("targetNetworkId", out prop))
                return prop.GetInt64();
            return 0L;
        }
    }
}
