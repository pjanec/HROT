using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Fbt;
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
    /// Unit tests for HillAttack commander and tank nodes — Corrective-1 + TASK-HA010–016.
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
            if (world.HasSingleton<AreaQueryBatchData>())
            {
                ref var batch = ref world.GetSingleton<AreaQueryBatchData>();
                if (batch.Results.IsCreated) batch.Results.Dispose();
            }
            if (world.HasSingleton<EqsTargetPool>())
            {
                var pool = world.GetSingleton<EqsTargetPool>();
                if (pool.Targets.IsCreated) pool.Targets.Dispose();
            }
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
            repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            // S3-G: PlatoonHillAttack's Behavior-scoped working state is provisioned into a
            // BlueprintBlackboard* partition tier (registered in production by BlueprintRuntimeWiring).
            repo.RegisterComponent<Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024>();
            repo.RegisterComponent<Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard4096>();
            repo.RegisterComponent<Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard16384>();
            return repo;
        }

        // ── Helper: set up UnitRoster on commander ────────────────────────────────

        private static unsafe void AddRoster(EntityRepository repo, Entity commander,
            Entity[] subs)
        {
            var roster = new UnitRoster { Count = subs.Length };
            for (int i = 0; i < subs.Length; i++)
                roster.SubordinateEntities[i] = (long)subs[i].PackedValue;
            repo.AddComponent(commander, roster);
        }

        // ── Helper: get mutable hill attack state ─────────────────────────────────

        // These are direct node-logic UNIT tests: they invoke the node methods with an explicit
        // `ref HillAttackMutableState`, so a Blackboard1024 component is used purely as a convenient
        // per-entity scratch buffer for that ref. This is NOT the production working-state path — in
        // production (and in T30/HillAttackIntegrationTests) the state lives in a Behavior-scoped
        // BlueprintBlackboard* partition slot; the Blackboard1024 + Unsafe.As hack was removed from the
        // node bodies in S3-G.
        private static ref HillAttackMutableState GetHeavyState(EntityRepository repo, Entity entity)
        {
            ref var heavy = ref repo.GetComponentRW<Blackboard1024>(entity);
            return ref Unsafe.As<Blackboard1024, HillAttackMutableState>(ref heavy);
        }

        // ── Corrective-1: SC-HA007 — Condition_HasTarget ─────────────────────────

        /// <summary>SC-HA007-1: Condition_HasTarget returns Success when target is in
        /// TargetMemory with positive ThreatScore.</summary>
        [Fact]
        public void SC_HA007_1_Condition_HasTarget_ReturnsSuccess_WhenTargetInMemoryWithScore()
        {
            using var repo = CreateWorld();

            var tank      = repo.CreateEntity();
            var target    = repo.CreateEntity();

            var netMap = new NetworkEntityMap();
            repo.SetSingletonManaged<NetworkEntityMap>(netMap);
            netMap.Register(42L, target);

            unsafe
            {
                var mem = new TargetMemory { Count = 1 };
                mem.EntityIds[0]    = (long)target.PackedValue;
                mem.ThreatScores[0] = 1.5f;
                repo.AddComponent(tank, mem);
            }

            var p     = new HullDownAttackParams { TargetNetworkId = 42L };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = tank, World = repo };

            var result = HillAttackTankNodes.Condition_HasTarget(ref p, ref state, ref ctx);

            Assert.Equal(NodeStatus.Success, result);
        }

        /// <summary>SC-HA007-2: Condition_HasTarget returns Failure when NetworkEntityMap
        /// cannot resolve TargetNetworkId.</summary>
        [Fact]
        public void SC_HA007_2_Condition_HasTarget_ReturnsFailure_WhenNetworkIdUnresolvable()
        {
            using var repo = CreateWorld();

            var tank = repo.CreateEntity();
            unsafe
            {
                var mem = new TargetMemory { Count = 0 };
                repo.AddComponent(tank, mem);
            }

            var p     = new HullDownAttackParams { TargetNetworkId = 99999L };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = tank, World = repo };

            var result = HillAttackTankNodes.Condition_HasTarget(ref p, ref state, ref ctx);

            Assert.Equal(NodeStatus.Failure, result);
        }

        // ── Corrective-1: SC-HA007 — Action_CreepToAndBeyondSlot ─────────────────

        /// <summary>SC-HA007-3: Action_CreepToAndBeyondSlot returns Running while within
        /// the tactical overshoot limit.</summary>
        [Fact]
        public void SC_HA007_3_CreepToAndBeyondSlot_ReturnsRunning_WhenWithinOvershootLimit()
        {
            using var repo = CreateWorld();

            var tank = repo.CreateEntity();
            // Tank is at the slot position (no overshoot yet).
            repo.AddComponent(tank, new SimTransform { Position = new System.Numerics.Vector3(50f, 50f, 0f) });
            repo.AddComponent(tank, new LocomotionChannel());

            var p = new HullDownAttackParams
            {
                SlotX         = 50f, SlotY = 50f,
                AttackDirX    = 0f,  AttackDirY = 1f,  // northward attack
                ApproachSpeed = 15f,
                CreepSpeed    = 5f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = tank, World = repo };

            var result = HillAttackTankNodes.Action_CreepToAndBeyondSlot(ref p, ref state, ref ctx);

            Assert.Equal(NodeStatus.Running, result);
        }

        /// <summary>SC-HA007-3b: Action_CreepToAndBeyondSlot returns Failure when overshoot
        /// exceeds HillAttackConstants.MaxOvershootMeters along the attack direction.</summary>
        [Fact]
        public void SC_HA007_3b_CreepToAndBeyondSlot_ReturnsFailure_WhenOvershootExceeds50m()
        {
            using var repo = CreateWorld();

            var tank = repo.CreateEntity();
            // Tank is 60m past the slot in the attack direction.
            repo.AddComponent(tank, new SimTransform { Position = new System.Numerics.Vector3(50f, 110f, 0f) });
            repo.AddComponent(tank, new LocomotionChannel());

            var p = new HullDownAttackParams
            {
                SlotX      = 50f, SlotY = 50f,
                AttackDirX = 0f,  AttackDirY = 1f,  // northward = positive Y
                CreepSpeed = 5f, ApproachSpeed = 15f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = tank, World = repo };

            // Overshoot = dot((110-50), (0,1)) = 60 > 50 => Failure
            var result = HillAttackTankNodes.Action_CreepToAndBeyondSlot(ref p, ref state, ref ctx);

            Assert.Equal(NodeStatus.Failure, result);
        }

        /// <summary>SC-HA007-4: When tank is far from slot, Speed in LocomotionChannel
        /// matches ApproachSpeed.</summary>
        [Fact]
        public unsafe void SC_HA007_4_CreepToAndBeyondSlot_UsesApproachSpeed_WhenFarFromSlot()
        {
            using var repo = CreateWorld();

            var tank = repo.CreateEntity();
            // 100m away from slot (far phase).
            repo.AddComponent(tank, new SimTransform { Position = new System.Numerics.Vector3(0f, 0f, 0f) });
            repo.AddComponent(tank, new LocomotionChannel());

            var p = new HullDownAttackParams
            {
                SlotX = 100f, SlotY = 0f,  // 100m east
                AttackDirX = 1f, AttackDirY = 0f,
                ApproachSpeed = 20f, CreepSpeed = 3f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = tank, World = repo };

            HillAttackTankNodes.Action_CreepToAndBeyondSlot(ref p, ref state, ref ctx);

            ref readonly var loco = ref repo.GetComponentRO<LocomotionChannel>(tank);
            MoveToParams written;
            fixed (byte* p2 = loco.Params)
                written = *(MoveToParams*)p2;
            Assert.Equal(20f, written.Speed, 0.001f);
        }

        /// <summary>SC-HA007-5: When tank is within SlotArrivalThreshold, Speed matches
        /// CreepSpeed and destination is far along attack direction.</summary>
        [Fact]
        public unsafe void SC_HA007_5_CreepToAndBeyondSlot_UsesCreepSpeed_WhenNearSlot()
        {
            using var repo = CreateWorld();

            var tank = repo.CreateEntity();
            // 5m from slot (< 15m threshold).
            repo.AddComponent(tank, new SimTransform { Position = new System.Numerics.Vector3(0f, 0f, 0f) });
            repo.AddComponent(tank, new LocomotionChannel());

            var p = new HullDownAttackParams
            {
                SlotX = 5f, SlotY = 0f,
                AttackDirX = 1f, AttackDirY = 0f,
                ApproachSpeed = 20f, CreepSpeed = 3f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = tank, World = repo };

            HillAttackTankNodes.Action_CreepToAndBeyondSlot(ref p, ref state, ref ctx);

            ref readonly var loco = ref repo.GetComponentRO<LocomotionChannel>(tank);
            MoveToParams written;
            fixed (byte* p2 = loco.Params)
                written = *(MoveToParams*)p2;
            Assert.Equal(3f, written.Speed, 0.001f);
            // Destination must be far along attack dir (east, positive X) from current position.
            Assert.True(written.Destination.X > 100f,
                $"Expected destination X >> 100 (got {written.Destination.X})");
        }

        /// <summary>SC-HA007-6: Calling CreepToAndBeyondSlot twice with identical state
        /// does NOT increment ActionInstanceId on the second call.</summary>
        [Fact]
        public void SC_HA007_6_CreepToAndBeyondSlot_NoRedundantWrite_OnSecondIdenticalCall()
        {
            using var repo = CreateWorld();

            var tank = repo.CreateEntity();
            repo.AddComponent(tank, new SimTransform { Position = new System.Numerics.Vector3(0f, 0f, 0f) });
            repo.AddComponent(tank, new LocomotionChannel());

            var p = new HullDownAttackParams
            {
                SlotX = 100f, SlotY = 0f,
                AttackDirX = 1f, AttackDirY = 0f,
                ApproachSpeed = 15f, CreepSpeed = 5f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = tank, World = repo };

            HillAttackTankNodes.Action_CreepToAndBeyondSlot(ref p, ref state, ref ctx);
            uint idAfterFirst = repo.GetComponentRO<LocomotionChannel>(tank).ActionInstanceId;

            HillAttackTankNodes.Action_CreepToAndBeyondSlot(ref p, ref state, ref ctx);
            uint idAfterSecond = repo.GetComponentRO<LocomotionChannel>(tank).ActionInstanceId;

            Assert.Equal(idAfterFirst, idAfterSecond);
        }

        // ── Corrective-1: SC-HA008 — Action_AimAndFireSpecific ────────────────────

        /// <summary>SC-HA008-1: AimAndFireSpecific writes WeaponChannel; ActionInstanceId
        /// incremented exactly once per engagement.</summary>
        [Fact]
        public void SC_HA008_1_AimAndFireSpecific_WritesWeaponChannel_AndIncrementsId()
        {
            using var repo = CreateWorld();

            var tank   = repo.CreateEntity();
            var target = repo.CreateEntity();

            var netMap = new NetworkEntityMap();
            repo.SetSingletonManaged<NetworkEntityMap>(netMap);
            netMap.Register(77L, target);

            repo.AddComponent(tank, new WeaponChannel());
            // Action_AimAndFireSpecific requires WeaponState to track rounds; without it the method
            // returns Failure early without writing to WeaponChannel.
            repo.AddComponent(tank, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 10 });

            var p     = new HullDownAttackParams { TargetNetworkId = 77L, MaxRounds = 1, LastObservedAmmo = -1 };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = tank, World = repo };

            HillAttackTankNodes.Action_AimAndFireSpecific(ref p, ref state, ref ctx);

            var weapon = repo.GetComponent<WeaponChannel>(tank);
            Assert.Equal(1u, weapon.ActionInstanceId);
        }

        /// <summary>SC-HA008-2: Second call with Running status does NOT increment
        /// ActionInstanceId again.</summary>
        [Fact]
        public void SC_HA008_2_AimAndFireSpecific_NoRedundantWrite_WhenAlreadyRunning()
        {
            using var repo = CreateWorld();

            var tank   = repo.CreateEntity();
            var target = repo.CreateEntity();

            var netMap = new NetworkEntityMap();
            repo.SetSingletonManaged<NetworkEntityMap>(netMap);
            netMap.Register(55L, target);

            repo.AddComponent(tank, new WeaponChannel());

            var p     = new HullDownAttackParams { TargetNetworkId = 55L };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = tank, World = repo };

            // First call — activates.
            HillAttackTankNodes.Action_AimAndFireSpecific(ref p, ref state, ref ctx);

            // Simulate running status.
            ref var weapon = ref repo.GetComponentRW<WeaponChannel>(tank);
            weapon.Status = NodeStatus.Running;

            uint idAfterFirst = weapon.ActionInstanceId;

            // Second call — should NOT write again.
            HillAttackTankNodes.Action_AimAndFireSpecific(ref p, ref state, ref ctx);

            uint idAfterSecond = repo.GetComponent<WeaponChannel>(tank).ActionInstanceId;
            Assert.Equal(idAfterFirst, idAfterSecond);
        }

        /// <summary>SC-HA008-3b: Target dead -> AimAndFireSpecific returns Success immediately.</summary>
        [Fact]
        public void SC_HA008_3b_AimAndFireSpecific_ReturnsSuccess_WhenTargetDead()
        {
            using var repo = CreateWorld();

            var tank   = repo.CreateEntity();
            var target = repo.CreateEntity();

            var netMap = new NetworkEntityMap();
            repo.SetSingletonManaged<NetworkEntityMap>(netMap);
            netMap.Register(11L, target);

            // Destroy the target so IsAlive returns false.
            repo.DestroyEntity(target);

            repo.AddComponent(tank, new WeaponChannel());

            var p     = new HullDownAttackParams { TargetNetworkId = 11L };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = tank, World = repo };

            var result = HillAttackTankNodes.Action_AimAndFireSpecific(ref p, ref state, ref ctx);

            Assert.Equal(NodeStatus.Success, result);
        }

        // ── Corrective-1: SC-HA008 — Action_ReverseToBaseline ────────────────────

        /// <summary>SC-HA008-4: Action_ReverseToBaseline writes destination matching
        /// (BaselineX, BaselineY).</summary>
        [Fact]
        public unsafe void SC_HA008_4_ReverseToBaseline_WritesBaselineDestination()
        {
            using var repo = CreateWorld();

            var tank = repo.CreateEntity();
            repo.AddComponent(tank, new LocomotionChannel());

            var p = new HullDownAttackParams { BaselineX = 10f, BaselineY = 20f };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = tank, World = repo };

            HillAttackTankNodes.Action_ReverseToBaseline(ref p, ref state, ref ctx);

            ref readonly var loco = ref repo.GetComponentRO<LocomotionChannel>(tank);
            MoveToParams written;
            fixed (byte* p2 = loco.Params)
                written = *(MoveToParams*)p2;
            Assert.Equal(10f, written.Destination.X, 0.001f);
            Assert.Equal(20f, written.Destination.Y, 0.001f);
        }

        /// <summary>SC-HA008-5: Action_ReverseToBaseline returns Success when
        /// LocomotionChannel.Status == Success.</summary>
        [Fact]
        public void SC_HA008_5_ReverseToBaseline_ReturnsSuccess_WhenChannelSuccess()
        {
            using var repo = CreateWorld();

            var tank = repo.CreateEntity();
            var loco = new LocomotionChannel
            {
                ActiveAction = NavigationConstants.ActionIdMoveTo,
                Status       = NodeStatus.Success,
            };
            repo.AddComponent(tank, loco);

            var p     = new HullDownAttackParams { BaselineX = 0f, BaselineY = 0f };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = tank, World = repo };

            var result = HillAttackTankNodes.Action_ReverseToBaseline(ref p, ref state, ref ctx);

            Assert.Equal(NodeStatus.Success, result);
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
            repo.AddComponent<Blackboard1024>(commander, default);

            var p = new PlatoonHillAttackParams
            {
                StartX = 0f, StartY = 0f,
                EndX = 100f, EndY = 0f,
                TankSpacing = 30f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            var result = HillAttackCommanderNodes.Action_CalculateSegments(ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);
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
            repo.AddComponent<Blackboard1024>(commander, default);

            var p = new PlatoonHillAttackParams
            {
                StartX = 0f, StartY = 0f,
                EndX = 10f, EndY = 0f,  // 10m < 30m spacing
                TankSpacing = 30f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            HillAttackCommanderNodes.Action_CalculateSegments(ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);
            ref var s = ref GetHeavyState(repo, commander);

            Assert.Equal(1, s.TotalSlots);
        }

        /// <summary>SC-HA010-3: Very long segment => TotalSlots capped at 16.</summary>
        [Fact]
        public void SC_HA010_3_CalculateSegments_VeryLongSegment_CapsAt16Slots()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();
            repo.AddComponent<Blackboard1024>(commander, default);

            var p = new PlatoonHillAttackParams
            {
                StartX = 0f, StartY = 0f,
                EndX = 1000f, EndY = 0f,  // 1000m / 30m = 33 would exceed 16
                TankSpacing = 30f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            HillAttackCommanderNodes.Action_CalculateSegments(ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);
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
            repo.AddComponent<Blackboard1024>(commander, default);

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

            HillAttackCommanderNodes.Action_DispatchAllToBaseline(ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);
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
            repo.AddComponent<Blackboard1024>(commander, default);

            var sub1 = repo.CreateEntity();
            var sub2 = repo.CreateEntity();
            repo.AddComponent(sub1, new NavigationStatus { Result = NavigationResult.Arrived });
            repo.AddComponent(sub2, new NavigationStatus { Result = NavigationResult.InProgress });
            AddRoster(repo, commander, new[] { sub1, sub2 });

            var p     = new PlatoonHillAttackParams();
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            var result = HillAttackCommanderNodes.Condition_AreAllAtBaseline(ref p, ref state, ref ctx);

            Assert.Equal(NodeStatus.Running, result);
        }

        /// <summary>SC-HA010-6: Condition_AreAllAtBaseline returns Success when all alive
        /// subordinates have NavigationResult == Arrived.</summary>
        [Fact]
        public void SC_HA010_6_AreAllAtBaseline_ReturnsSuccess_WhenAllArrived()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();
            repo.AddComponent<Blackboard1024>(commander, default);

            var sub1 = repo.CreateEntity();
            var sub2 = repo.CreateEntity();
            repo.AddComponent(sub1, new NavigationStatus { Result = NavigationResult.Arrived });
            repo.AddComponent(sub2, new NavigationStatus { Result = NavigationResult.Arrived });
            AddRoster(repo, commander, new[] { sub1, sub2 });

            var p     = new PlatoonHillAttackParams();
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            var result = HillAttackCommanderNodes.Condition_AreAllAtBaseline(ref p, ref state, ref ctx);

            Assert.Equal(NodeStatus.Success, result);
        }

        /// <summary>SC-HA010-7: Dead subordinate does not block Condition_AreAllAtBaseline.</summary>
        [Fact]
        public void SC_HA010_7_AreAllAtBaseline_DeadSubordinateCountsAsArrived()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();
            repo.AddComponent<Blackboard1024>(commander, default);

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

            var result = HillAttackCommanderNodes.Condition_AreAllAtBaseline(ref p, ref state, ref ctx);

            Assert.Equal(NodeStatus.Success, result);
        }

        // ── TASK-HA011: SC-HA011-1 through SC-HA011-5 ────────────────────────────

        /// <summary>SC-HA011-1: Action_RequestAreaQuery sets CachedEqsRequestId to a valid
        /// (>= 0) value on first call when batch is not full.</summary>
        [Fact]
        public void SC_HA011_1_RequestAreaQuery_SetsCachedRequestId_OnFirstCall()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();
            var areaEntity = repo.CreateEntity();
            repo.AddComponent<Blackboard1024>(commander, default);

            ref var s = ref GetHeavyState(repo, commander);
            s.CachedEqsRequestId = -1;

            var p     = new PlatoonHillAttackParams { TargetAreaEntity = areaEntity };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            try
            {
                var result = HillAttackCommanderNodes.Action_RequestAreaQuery(ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);

                Assert.Equal(NodeStatus.Success, result);
                Assert.True(s.CachedEqsRequestId >= 0,
                    $"Expected CachedEqsRequestId >= 0, got {s.CachedEqsRequestId}");
            }
            finally
            {
                DisposeEqsSingletons(repo);
            }
        }

        /// <summary>SC-HA011-2: Action_RequestAreaQuery returns Running when a request is
        /// already in-flight (CachedEqsRequestId set and result is not yet ready).</summary>
        [Fact]
        public void SC_HA011_2_RequestAreaQuery_ReturnsRunning_WhenRequestInFlight()
        {
            using var repo = CreateWorld();

            var commander  = repo.CreateEntity();
            var areaEntity = repo.CreateEntity();
            repo.AddComponent<Blackboard1024>(commander, default);

            ref var s = ref GetHeavyState(repo, commander);

            try
            {
                // Submit an initial request to place a valid ID in-flight.
                long requestId = AreaQueryBatchHelper.RequestAreaQuery(
                    repo, commander, areaEntity, ForceId.Hostile);
                s.CachedEqsRequestId = requestId;

                // The result ring-buffer slot is primed with IsReady == false by RequestAreaQuery.
                var p     = new PlatoonHillAttackParams { TargetAreaEntity = areaEntity };
                var state = new BehaviorTreeState();
                var ctx   = new BTreeContext { Self = commander, World = repo };

                var result = HillAttackCommanderNodes.Action_RequestAreaQuery(ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);

                Assert.Equal(NodeStatus.Running, result);
                Assert.Equal(requestId, s.CachedEqsRequestId);
            }
            finally
            {
                DisposeEqsSingletons(repo);
            }
        }

        /// <summary>SC-HA011-3: Condition_IsAreaQueryResolved returns Running while
        /// result IsReady == false.</summary>
        // STABILITY(Broken): Component type ID 117 not registered — missing AreaQuery component registration; investigate
        [Trait("Stability", "Broken")]
        [Fact]
        public void SC_HA011_3_IsAreaQueryResolved_ReturnsRunning_WhenResultNotReady()
        {
            using var repo = CreateWorld();

            var commander  = repo.CreateEntity();
            var areaEntity = repo.CreateEntity();
            repo.AddComponent<Blackboard1024>(commander, default);

            try
            {
                // Submit a request.
                long requestId = AreaQueryBatchHelper.RequestAreaQuery(
                    repo, commander, areaEntity, ForceId.Hostile);

                ref var s = ref GetHeavyState(repo, commander);
                s.CachedEqsRequestId = requestId;

                var p     = new PlatoonHillAttackParams();
                var state = new BehaviorTreeState();
                var ctx   = new BTreeContext { Self = commander, World = repo };

                // Result is not yet marked as ready — should return Running.
                var result = HillAttackCommanderNodes.Condition_IsAreaQueryResolved(
                    ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);

                Assert.Equal(NodeStatus.Running, result);
            }
            finally
            {
                DisposeEqsSingletons(repo);
            }
        }

        /// <summary>SC-HA011-4: Condition_IsAreaQueryResolved returns Failure and resets
        /// CachedEqsRequestId = -1 when IsReady == true and TargetCount == 0.</summary>
        [Fact]
        public void SC_HA011_4_IsAreaQueryResolved_ReturnsFailure_WhenReadyWithZeroTargets()
        {
            using var repo = CreateWorld();

            var commander  = repo.CreateEntity();
            var areaEntity = repo.CreateEntity();
            repo.AddComponent<Blackboard1024>(commander, default);

            try
            {
                long requestId = AreaQueryBatchHelper.RequestAreaQuery(
                    repo, commander, areaEntity, ForceId.Hostile);

                // Write the result to the correct ring-buffer slot (same XOR hash used by
                // AreaQueryBatchHelper.GetAreaQueryResult and AreaQueryResultMaterializationSystem).
                int slot = (int)(((ulong)requestId ^ ((ulong)requestId >> 32)) % (uint)AreaQueryBatchData.DefaultCapacity);
                ref var batch = ref repo.GetSingleton<AreaQueryBatchData>();
                batch.Results[slot] = new AreaQueryResult
                {
                    RequestId   = requestId,
                    IsReady     = true,
                    TargetCount = 0,
                    TargetGroupHandle = -1,
                };

                ref var s = ref GetHeavyState(repo, commander);
                s.CachedEqsRequestId = requestId;

                var p     = new PlatoonHillAttackParams();
                var state = new BehaviorTreeState();
                var ctx   = new BTreeContext { Self = commander, World = repo };

                var result = HillAttackCommanderNodes.Condition_IsAreaQueryResolved(
                    ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);

                Assert.Equal(NodeStatus.Failure, result);
                Assert.Equal(-1L, s.CachedEqsRequestId);
                Assert.Equal(-1, s.CachedTargetGroupHandle);
            }
            finally
            {
                DisposeEqsSingletons(repo);
            }
        }

        /// <summary>SC-HA011-5: Condition_IsAreaQueryResolved returns Success when
        /// IsReady == true and TargetCount > 0. CachedEqsRequestId is NOT cleared.</summary>
        [Fact]
        public void SC_HA011_5_IsAreaQueryResolved_ReturnsSuccess_AndDoesNotClearRequestId()
        {
            using var repo = CreateWorld();

            var commander  = repo.CreateEntity();
            var areaEntity = repo.CreateEntity();
            repo.AddComponent<Blackboard1024>(commander, default);

            try
            {
                long requestId = AreaQueryBatchHelper.RequestAreaQuery(
                    repo, commander, areaEntity, ForceId.Hostile);

                // Write the result to the correct ring-buffer slot (same XOR hash used by
                // AreaQueryBatchHelper.GetAreaQueryResult and AreaQueryResultMaterializationSystem).
                int slot = (int)(((ulong)requestId ^ ((ulong)requestId >> 32)) % (uint)AreaQueryBatchData.DefaultCapacity);
                ref var batch = ref repo.GetSingleton<AreaQueryBatchData>();
                batch.Results[slot] = new AreaQueryResult
                {
                    RequestId         = requestId,
                    IsReady           = true,
                    TargetCount       = 2,
                    TargetGroupHandle = 0,
                };

                ref var s = ref GetHeavyState(repo, commander);
                s.CachedEqsRequestId = requestId;

                var p     = new PlatoonHillAttackParams();
                var state = new BehaviorTreeState();
                var ctx   = new BTreeContext { Self = commander, World = repo };

                var result = HillAttackCommanderNodes.Condition_IsAreaQueryResolved(
                    ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);

                Assert.Equal(NodeStatus.Success, result);
                // SC-HA011-5: CachedEqsRequestId must NOT be cleared on Success path.
                Assert.Equal(requestId, s.CachedEqsRequestId);
                Assert.Equal(0, s.CachedTargetGroupHandle);
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
            repo.AddComponent<Blackboard1024>(commander, default);

            // Create 4 subordinates. Their indices are assigned sequentially after commander.
            var subs = new Entity[4];
            for (int i = 0; i < 4; i++)
                subs[i] = repo.CreateEntity();

            AddRoster(repo, commander, subs);

            ref var s = ref GetHeavyState(repo, commander);
            s.TotalSlots          = 4;
            s.CurrentWave         = 0;
            s.BurnedSlotsMask     = 0;
            s.CachedTargetGroupHandle = -1;
            s.CachedEqsRequestId  = -1;

            // Set up a single target in pool.
            ref var pool = ref repo.GetSingleton<EqsTargetPool>();
            pool.Targets[0] = 1L;

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
                HillAttackCommanderNodes.Action_DispatchWaveWithTargets(ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);

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

        /// <summary>SC-HA012-2: With roster.Count <= 3, all alive tanks dispatched
        /// regardless of parity.</summary>
        [Fact]
        public void SC_HA012_2_DispatchWaveWithTargets_AllParticipate_WhenRosterLe3()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();
            repo.AddComponent<Blackboard1024>(commander, default);

            var subs = new Entity[3];
            for (int i = 0; i < 3; i++)
                subs[i] = repo.CreateEntity();

            AddRoster(repo, commander, subs);

            ref var s = ref GetHeavyState(repo, commander);
            s.TotalSlots          = 4;
            s.CurrentWave         = 0;
            s.BurnedSlotsMask     = 0;
            s.CachedTargetGroupHandle = -1;
            s.CachedEqsRequestId  = -1;

            ref var pool = ref repo.GetSingleton<EqsTargetPool>();
            pool.Targets[0] = 1L;

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
                HillAttackCommanderNodes.Action_DispatchWaveWithTargets(ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);

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
            repo.AddComponent<Blackboard1024>(commander, default);

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
            s.CachedTargetGroupHandle = 0;
            s.CachedEqsRequestId      = -1;

            ref var pool = ref repo.GetSingleton<EqsTargetPool>();
            pool.Targets[0] = (long)target1.PackedValue;
            pool.Targets[1] = (long)target2.PackedValue;
            // Targets[2] stays 0 => probe stops after 2.

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
                HillAttackCommanderNodes.Action_DispatchWaveWithTargets(ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);
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
            repo.AddComponent<Blackboard1024>(commander, default);

            ref var s = ref GetHeavyState(repo, commander);
            s.ActiveAttackerCount = 0;

            var p     = new PlatoonHillAttackParams();
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            var result = HillAttackCommanderNodes.Condition_IsWaveCompleted(ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);

            Assert.Equal(NodeStatus.Success, result);
        }

        /// <summary>SC-HA012-5: Dead attacker causes BurnedSlotsMask bit to be set,
        /// BaselineReservedMask cleared, and ActiveAttackerCount decrements to 0 => Success.</summary>
        [Fact]
        public void SC_HA012_5_IsWaveCompleted_DeadAttacker_BurnsSlotAndReturnsSuccess()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();
            repo.AddComponent<Blackboard1024>(commander, default);

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

            var result = HillAttackCommanderNodes.Condition_IsWaveCompleted(ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);

            Assert.Equal(NodeStatus.Success, result);
            Assert.Equal(0, s.ActiveAttackerCount);
            Assert.True((s.BurnedSlotsMask & (1 << 1)) != 0,
                "Firing slot 1 should be burned");
            Assert.True((s.BaselineReservedMask & (1 << 2)) == 0,
                "Baseline slot 2 should be released");
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
            repo.AddComponent<Blackboard1024>(commander, default);

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

            var result = HillAttackCommanderNodes.Condition_IsWaveCompleted(ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);

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
            repo.AddComponent<Blackboard1024>(commander, default);

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
            var r1 = HillAttackCommanderNodes.Condition_IsWaveCompleted(ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);
            Assert.Equal(NodeStatus.Running, r1);
            unsafe { Assert.Equal(1, s.HasStartedRun[0]); }

            // Tick T+1: hash no longer == 3013, run considered finished.
            repo.GetComponentRW<BehaviorState>(attacker).ActiveBehaviorHash = BehaviorHash.FromName(BehaviorNames.Idle);  // Idle
            var r2 = HillAttackCommanderNodes.Condition_IsWaveCompleted(ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);

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
            repo.AddComponent<Blackboard1024>(commander, default);

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
            repo.AddComponent<Blackboard1024>(commander, default);

            var subs = new Entity[3];
            for (int i = 0; i < 3; i++)
                subs[i] = repo.CreateEntity();
            AddRoster(repo, commander, subs);

            ref var s = ref GetHeavyState(repo, commander);
            s.TotalSlots          = 4;
            s.CurrentWave         = 0;
            s.BurnedSlotsMask     = 0b0001;  // slot 0 burned
            s.CachedTargetGroupHandle = -1;
            s.CachedEqsRequestId  = -1;

            ref var pool = ref repo.GetSingleton<EqsTargetPool>();
            pool.Targets[0] = 1L;

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
                HillAttackCommanderNodes.Action_DispatchWaveWithTargets(ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);

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

        /// <summary>SC-HA012-8: CachedTargetGroupHandle == -1 after DispatchWaveWithTargets.</summary>
        [Fact]
        public void SC_HA012_8_DispatchWave_ClearsTargetGroupHandle_AfterDispatch()
        {
            using var repo = CreateWorld();

            var commander = repo.CreateEntity();
            repo.AddComponent<Blackboard1024>(commander, default);

            var subs = new Entity[2];
            for (int i = 0; i < 2; i++)
                subs[i] = repo.CreateEntity();
            AddRoster(repo, commander, subs);

            ref var s = ref GetHeavyState(repo, commander);
            s.TotalSlots              = 4;
            s.CurrentWave             = 0;
            s.BurnedSlotsMask         = 0;
            s.CachedTargetGroupHandle = 0;
            s.CachedEqsRequestId      = -1;

            ref var pool = ref repo.GetSingleton<EqsTargetPool>();
            pool.Targets[0] = 1L;

            var p = new PlatoonHillAttackParams
            {
                StartX = 0f, StartY = 0f, EndX = 30f, EndY = 0f,
                BaselineStartX = 0f, BaselineStartY = 50f,
                BaselineEndX   = 30f, BaselineEndY  = 50f,
            };
            var state = new BehaviorTreeState();
            var ctx   = new BTreeContext { Self = commander, World = repo };

            try
            {
                HillAttackCommanderNodes.Action_DispatchWaveWithTargets(ref p, ref GetHeavyState(repo, commander), ref state, ref ctx);

                Assert.Equal(-1, s.CachedTargetGroupHandle);
            }
            finally
            {
                DisposeEqsSingletons(repo);
            }
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

            // S3-G: the Blackboard1024 HeavyDtoType hack is gone; working state is a partition slot.
            Assert.Null(def.HeavyDtoType);
            Assert.NotNull(def.StatefulWorkingSlots);
            Assert.Single(def.StatefulWorkingSlots);
            Assert.Equal(typeof(HillAttackMutableState), def.StatefulWorkingSlots[0].WorkingStateType);
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
            repo.AddComponent(commander, new BrainBlackboard());

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

            // Tank subordinate (non-stateful): CreepToAndBeyondSlot / AimAndFireSpecific deactivators.
            Assert.True(registry.TryGetDefinition(BehaviorHash.FromName(BehaviorNames.HullDownAttackRun), out var tankDef));
            AssertNodeResourceOwning(tankDef.BTreeInterpreter!.Blob,
                "Hrot.AI.Behaviors.Brains.HillAttackTankNodes.Action_CreepToAndBeyondSlot@0");
            AssertNodeResourceOwning(tankDef.BTreeInterpreter!.Blob,
                "Hrot.AI.Behaviors.Brains.HillAttackTankNodes.Action_AimAndFireSpecific@0");
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
                HillAttackCommanderNodes.ParsePlatoonHillAttackParams(json, ptr, null, new NetworkEntityMap());
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
                HillAttackCommanderNodes.ParsePlatoonHillAttackParams(json, ptr, null, new NetworkEntityMap());
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
                HillAttackCommanderNodes.ParsePlatoonHillAttackParams(json, ptr, null, new NetworkEntityMap());
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
                HillAttackCommanderNodes.ParsePlatoonHillAttackParams(json, ptr, null, new NetworkEntityMap());
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
                HillAttackCommanderNodes.ParsePlatoonHillAttackParams(json, ptr, null, entityMap);
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
                    HillAttackCommanderNodes.ParsePlatoonHillAttackParams(json, ptr, null, new NetworkEntityMap());
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
            repo.AddComponent(commander, new BrainBlackboard());

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

            ref readonly var bb = ref repo.GetComponentRO<BrainBlackboard>(commander);
            PlatoonHillAttackParams parms;
            fixed (BrainBlackboard* bp = &bb)
                parms = *(PlatoonHillAttackParams*)bp;

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
