using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    public unsafe class BehaviorIngressSystemTests
    {
        // ── Nested types used by individual tests ─────────────────────────────────

        /// <summary>
        /// Minimal blackboard used by the FleeToSafety behavior test.
        /// Must be the first field so it aligns with offset 0 of BrainBlackboard.BehaviorParameters.
        /// </summary>
        private struct FleeBlackboard { public float SafeDistance; }

        // ── Helper ───────────────────────────────────────────────────────────────

        private static (EntityRepository world, BehaviorIngressSystem sys, BehaviorRegistry registry)
            CreateFixture()
        {
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();
            var sys      = new BehaviorIngressSystem(registry);
            return (world, sys, registry);
        }

        // ── Test 1 ───────────────────────────────────────────────────────────────

        [Fact]
        public void BehaviorIngress_ParsesFleeBlackboard_FromJson()
        {
            var (world, sys, registry) = CreateFixture();

            // Register "FleeToSafety" behavior with a parse delegate that writes a float.
            const string behaviorName = "FleeToSafety";
            registry.Register(BehaviorIds.PanicFlee, behaviorName, new BehaviorDefinition
            {
                Name      = behaviorName,
                BrainTier = BehaviorConstants.BrainTierBTree,
                ParseParams = static (string json, byte* mem) =>
                {
                    *(float*)mem = float.Parse(json,
                        System.Globalization.CultureInfo.InvariantCulture);
                },
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            world.AddComponent(e, new BrainBlackboard());

            // Publish event then swap buffers so ConsumeManaged returns it.
            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = e,
                BehaviorName = behaviorName,
                JsonParams   = "50.0",
            });
            world.Bus.SwapBuffers();

            sys.Execute(world, 0.016f);

            // Verify: BrainBlackboard.BehaviorParameters[0..3] == 50.0f.
            ref var blackboard = ref world.GetComponentRW<BrainBlackboard>(e);
            var bbPtr = (BrainBlackboard*)Unsafe.AsPointer(ref blackboard);
            var fb    = *(FleeBlackboard*)bbPtr->BehaviorParameters;
            Assert.Equal(50.0f, fb.SafeDistance);

            world.Dispose();
        }

        // ── Test 2 ───────────────────────────────────────────────────────────────

        [Fact]
        public void BehaviorIngress_IncrementsInstanceId_MonotonicallyAcrossMultipleAssignments()
        {
            var (world, sys, registry) = CreateFixture();

            const string behaviorName = "Patrol";
            const int PatrolId = 3001;
            registry.Register(PatrolId, behaviorName, new BehaviorDefinition
            {
                Name      = behaviorName,
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { InstanceId = 0 });
            world.AddComponent(e, new BrainBlackboard());

            // --- Assignment 1 ---
            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = behaviorName, JsonParams = "" });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
            uint instanceId1 = world.GetComponent<BehaviorState>(e).InstanceId;

            // --- Assignment 2 ---
            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = behaviorName, JsonParams = "" });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
            uint instanceId2 = world.GetComponent<BehaviorState>(e).InstanceId;

            Assert.True(instanceId1 > 0);             // was incremented from 0
            Assert.True(instanceId2 > instanceId1);   // strictly increasing each assignment

            world.Dispose();
        }

        // ── Test 3 ───────────────────────────────────────────────────────────────

        [Fact]
        public void BehaviorIngress_ResetsBTreeState_OnNewBehavior()
        {
            var (world, sys, registry) = CreateFixture();

            const string behaviorName = "Assault";
            const int AssaultId = 3002;
            registry.Register(AssaultId, behaviorName, new BehaviorDefinition
            {
                Name      = behaviorName,
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            world.AddComponent(e, new BrainBlackboard());
            // Give entity a mid-execution BTree state (RunningNodeIndex != 0).
            world.AddComponent(e, new BrainBTreeState
            {
                State = new Fbt.BehaviorTreeState { RunningNodeIndex = 5 }
            });

            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = behaviorName, JsonParams = "" });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            var btState = world.GetComponent<BrainBTreeState>(e);
            Assert.Equal(0, btState.State.RunningNodeIndex); // reset to start

            world.Dispose();
        }

        // ── Test 4 (integration chain) ────────────────────────────────────────────

        [Fact]
        public void BehaviorIngress_StaleSetsNewInstanceId_ArbitrationClearsOldAction()
        {
            // Full preemption chain: ingress bumps InstanceId, then arbitration
            // clears the now-stale channel. Each system is called explicitly.
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();

            const string behaviorName = "Flank";
            const int FlankId = 3003;
            registry.Register(FlankId, behaviorName, new BehaviorDefinition
            {
                Name      = behaviorName,
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            var ingressSys     = new BehaviorIngressSystem(registry);
            var arbitrationSys = new ChannelArbitrationSystem();

            var e = world.CreateEntity();
            // Setup: channel and behavior both at version 1 (valid/matching).
            world.AddComponent(e, new BehaviorState { InstanceId = 1 });
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction       = 1,
                BehaviorInstanceId = 1, // matches BehaviorState.InstanceId — not yet stale
            });
            world.AddComponent(e, new BrainBlackboard());

            // Step 1: assign new behavior → InstanceId becomes 2.
            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = e,
                BehaviorName = behaviorName,
                JsonParams   = "",
            });
            world.Bus.SwapBuffers();
            ingressSys.Execute(world, 0.016f);

            uint newInstanceId = world.GetComponent<BehaviorState>(e).InstanceId;
            Assert.True(newInstanceId > 1); // ingress incremented it

            // Step 2: arbitration sees BehaviorInstanceId(1) != InstanceId(2) → clears channel.
            arbitrationSys.Execute(world, 0.016f);

            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(0, channel.ActiveAction);          // preemption chain complete
            Assert.Equal(1u, channel.BehaviorInstanceId);   // selective-clear preserves BehaviorInstanceId (only ActiveAction zeroed)

            world.Dispose();
        }

        // ── Test 5 (DEBT-008 / DEBT-035) ────────────────────────────────────────
        /// <summary>
        /// DEBT-035 fix verification: when <see cref="BehaviorDefinition.ParseParams"/> throws,
        /// <see cref="BehaviorIngressSystem"/> must not propagate the exception AND must leave
        /// the entity entirely on its previous behavior (no partial transition).
        /// </summary>
        [Fact]
        public void BehaviorIngress_DoesNotThrow_WhenParseParamsFails()
        {
            var (world, sys, registry) = CreateFixture();

            const string behaviorName = "BrokenBehavior";
            const int BrokenId = 7001;
            registry.Register(BrokenId, behaviorName, new BehaviorDefinition
            {
                Name      = behaviorName,
                BrainTier = BehaviorConstants.BrainTierBTree,
                // ParseParams delegate that always throws.
                ParseParams = static (string json, byte* mem) =>
                    throw new InvalidOperationException("Simulated parse failure"),
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { InstanceId = 5 });
            world.AddComponent(e, new BrainBlackboard());

            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = e,
                BehaviorName = behaviorName,
                JsonParams   = "bad_json",
            });
            world.Bus.SwapBuffers();

            // Must not throw.
            var exception = Record.Exception(() => sys.Execute(world, 0.016f));
            Assert.Null(exception);

            // DEBT-035 fix: ParseParams now runs BEFORE BehaviorState is written.
            // A parse failure aborts the transition — InstanceId must remain at 5.
            var state = world.GetComponent<BehaviorState>(e);
            Assert.Equal(5u, state.InstanceId);

            world.Dispose();
        }

        // ── Test 6 (DEBT-035 required test) ──────────────────────────────────────
        /// <summary>
        /// Required by BATCH-14 Corrective-0: verifies that both
        /// <see cref="BehaviorState.ActiveBehaviorHash"/> AND
        /// <see cref="BehaviorState.InstanceId"/> are unchanged when
        /// <see cref="BehaviorDefinition.ParseParams"/> fails.
        /// </summary>
        [Fact]
        public void BehaviorIngress_BehaviorStateUnchanged_WhenParseParamsFails()
        {
            var (world, sys, registry) = CreateFixture();

            const int OldId = 9000;
            const int NewId = 9001;
            const string oldBehaviorName = "OldBehavior";
            const string newBehaviorName = "NewBehavior";

            registry.Register(OldId, oldBehaviorName, new BehaviorDefinition
            {
                Name      = oldBehaviorName,
                BrainTier = BehaviorConstants.BrainTierBTree,
            });
            registry.Register(NewId, newBehaviorName, new BehaviorDefinition
            {
                Name      = newBehaviorName,
                BrainTier = BehaviorConstants.BrainTierBTree,
                // ParseParams delegate that always throws.
                ParseParams = static (string json, byte* mem) =>
                    throw new InvalidOperationException("Test-induced parse failure"),
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = OldId,
                InstanceId         = 0
            });
            world.AddComponent(e, new BrainBlackboard());

            // Attempt to switch to NewBehavior — ParseParams will throw.
            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = e,
                BehaviorName = newBehaviorName,
                JsonParams   = "{}",
            });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            var state = world.GetComponent<BehaviorState>(e);
            // ActiveBehaviorHash must still point to OldId — NOT switched to NewId.
            Assert.Equal(OldId, state.ActiveBehaviorHash);
            // InstanceId must NOT have been bumped.
            Assert.Equal(0u, state.InstanceId);

            world.Dispose();
        }

        // ── Task-2 Tests: ClearBehaviorEvent ─────────────────────────────────────

        [Fact]
        public void ClearBehaviorEvent_SetsBehaviorToNone()
        {
            var (world, sys, _) = CreateFixture();

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = 2001,
                InstanceId         = 5,
                BrainTier          = BehaviorConstants.BrainTierBTree,
            });
            world.AddComponent(e, new BrainBTreeState
            {
                State = new Fbt.BehaviorTreeState { RunningNodeIndex = 3 }
            });

            world.Bus.Publish(new ClearBehaviorEvent { Entity = e });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            var behavior = world.GetComponent<BehaviorState>(e);
            Assert.Equal(BehaviorIds.None, behavior.ActiveBehaviorHash);  // cleared
            Assert.Equal(6u,              behavior.InstanceId);           // incremented
            Assert.Equal(0,               behavior.BrainTier);            // reset to none

            var btState = world.GetComponent<BrainBTreeState>(e);
            Assert.Equal(0, btState.State.RunningNodeIndex);              // execution pointer reset

            world.Dispose();
        }

        [Fact]
        public void ClearBehaviorEvent_NoBehaviorState_IsIgnored()
        {
            var (world, sys, _) = CreateFixture();

            // Entity without BehaviorState — event must be silently skipped.
            var e = world.CreateEntity();
            // Intentionally no BehaviorState component added.

            world.Bus.Publish(new ClearBehaviorEvent { Entity = e });
            world.Bus.SwapBuffers();

            var exception = Record.Exception(() => sys.Execute(world, 0.016f));
            Assert.Null(exception);

            world.Dispose();
        }

        [Fact]
        public void ClearBehaviorEvent_DoesNotAffectOtherEntities()
        {
            var (world, sys, _) = CreateFixture();

            var entityA = world.CreateEntity();
            world.AddComponent(entityA, new BehaviorState { ActiveBehaviorHash = 1001, InstanceId = 1 });

            var entityB = world.CreateEntity();
            world.AddComponent(entityB, new BehaviorState { ActiveBehaviorHash = 1001, InstanceId = 1 });

            // Only clear entity A.
            world.Bus.Publish(new ClearBehaviorEvent { Entity = entityA });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            var docA = world.GetComponent<BehaviorState>(entityA);
            var docB = world.GetComponent<BehaviorState>(entityB);

            Assert.Equal(BehaviorIds.None, docA.ActiveBehaviorHash); // cleared
            Assert.Equal(1001,            docB.ActiveBehaviorHash);  // untouched

            world.Dispose();
        }

        [Fact]
        public void ClearVsAssign_AreIndependent()
        {
            // In the same frame: AssignBehaviorEvent for A and ClearBehaviorEvent for B.
            // After one Run, A has the assigned behavior; B has BehaviorIds.None.
            var (world, sys, registry) = CreateFixture();

            const string behaviorName = "Patrol";
            const int PatrolId = 5001;
            registry.Register(PatrolId, behaviorName, new BehaviorDefinition
            {
                Name      = behaviorName,
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            var entityA = world.CreateEntity();
            world.AddComponent(entityA, new BehaviorState { ActiveBehaviorHash = 0, InstanceId = 0 });
            world.AddComponent(entityA, new BrainBlackboard());

            var entityB = world.CreateEntity();
            world.AddComponent(entityB, new BehaviorState { ActiveBehaviorHash = PatrolId, InstanceId = 1 });

            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = entityA, BehaviorName = behaviorName, JsonParams = "" });
            world.Bus.Publish(new ClearBehaviorEvent  { Entity = entityB });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            var docA = world.GetComponent<BehaviorState>(entityA);
            var docB = world.GetComponent<BehaviorState>(entityB);

            Assert.Equal(PatrolId,        docA.ActiveBehaviorHash); // assigned
            Assert.Equal(BehaviorIds.None, docB.ActiveBehaviorHash); // cleared

            world.Dispose();
        }
    }
}
