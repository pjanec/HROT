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
    public unsafe class DoctrineIngressSystemTests
    {
        // ── Nested types used by individual tests ─────────────────────────────────

        /// <summary>
        /// Minimal blackboard used by the FleeToSafety doctrine test.
        /// Must be the first field so it aligns with offset 0 of BrainBlackboard.Memory.
        /// </summary>
        private struct FleeBlackboard { public float SafeDistance; }

        // ── Helper ───────────────────────────────────────────────────────────────

        private static (EntityRepository world, DoctrineIngressSystem sys, DoctrineRegistry registry)
            CreateFixture()
        {
            var world    = TestWorldFactory.Create();
            var registry = new DoctrineRegistry();
            var sys      = new DoctrineIngressSystem(registry);
            return (world, sys, registry);
        }

        // ── Test 1 ───────────────────────────────────────────────────────────────

        [Fact]
        public void DoctrineIngress_ParsesFleeBlackboard_FromJson()
        {
            var (world, sys, registry) = CreateFixture();

            // Register "FleeToSafety" doctrine with a parse delegate that writes a float.
            const string doctrineName = "FleeToSafety";
            registry.Register(DoctrineIds.PanicFlee, doctrineName, new DoctrineDefinition
            {
                Name      = doctrineName,
                BrainTier = BehaviorConstants.BrainTierBTree,
                ParseParams = static (string json, byte* mem) =>
                {
                    *(float*)mem = float.Parse(json,
                        System.Globalization.CultureInfo.InvariantCulture);
                },
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState());
            world.AddComponent(e, new BrainBlackboard());

            // Publish event then swap buffers so ConsumeManaged returns it.
            world.Bus.PublishManaged(new AssignDoctrineEvent
            {
                Entity       = e,
                DoctrineName = doctrineName,
                JsonParams   = "50.0",
            });
            world.Bus.SwapBuffers();

            sys.Execute(world, 0.016f);

            // Verify: BrainBlackboard.Memory[0..3] == 50.0f.
            ref var blackboard = ref world.GetComponentRW<BrainBlackboard>(e);
            var bbPtr = (BrainBlackboard*)Unsafe.AsPointer(ref blackboard);
            var fb    = *(FleeBlackboard*)bbPtr->Memory;
            Assert.Equal(50.0f, fb.SafeDistance);

            world.Dispose();
        }

        // ── Test 2 ───────────────────────────────────────────────────────────────

        [Fact]
        public void DoctrineIngress_IncrementsInstanceId_MonotonicallyAcrossMultipleAssignments()
        {
            var (world, sys, registry) = CreateFixture();

            const string doctrineName = "Patrol";
            const int PatrolId = 3001;
            registry.Register(PatrolId, doctrineName, new DoctrineDefinition
            {
                Name      = doctrineName,
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { InstanceId = 0 });
            world.AddComponent(e, new BrainBlackboard());

            // --- Assignment 1 ---
            world.Bus.PublishManaged(new AssignDoctrineEvent { Entity = e, DoctrineName = doctrineName, JsonParams = "" });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
            uint instanceId1 = world.GetComponent<DoctrineState>(e).InstanceId;

            // --- Assignment 2 ---
            world.Bus.PublishManaged(new AssignDoctrineEvent { Entity = e, DoctrineName = doctrineName, JsonParams = "" });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
            uint instanceId2 = world.GetComponent<DoctrineState>(e).InstanceId;

            Assert.True(instanceId1 > 0);             // was incremented from 0
            Assert.True(instanceId2 > instanceId1);   // strictly increasing each assignment

            world.Dispose();
        }

        // ── Test 3 ───────────────────────────────────────────────────────────────

        [Fact]
        public void DoctrineIngress_ResetsBTreeState_OnNewDoctrine()
        {
            var (world, sys, registry) = CreateFixture();

            const string doctrineName = "Assault";
            const int AssaultId = 3002;
            registry.Register(AssaultId, doctrineName, new DoctrineDefinition
            {
                Name      = doctrineName,
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState());
            world.AddComponent(e, new BrainBlackboard());
            // Give entity a mid-execution BTree state (RunningNodeIndex != 0).
            world.AddComponent(e, new BrainBTreeState
            {
                State = new Fbt.BehaviorTreeState { RunningNodeIndex = 5 }
            });

            world.Bus.PublishManaged(new AssignDoctrineEvent { Entity = e, DoctrineName = doctrineName, JsonParams = "" });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            var btState = world.GetComponent<BrainBTreeState>(e);
            Assert.Equal(0, btState.State.RunningNodeIndex); // reset to start

            world.Dispose();
        }

        // ── Test 4 (integration chain) ────────────────────────────────────────────

        [Fact]
        public void DoctrineIngress_StaleSetsNewInstanceId_ArbitrationClearsOldAction()
        {
            // Full preemption chain: ingress bumps InstanceId, then arbitration
            // clears the now-stale channel. Each system is called explicitly.
            var world    = TestWorldFactory.Create();
            var registry = new DoctrineRegistry();

            const string doctrineName = "Flank";
            const int FlankId = 3003;
            registry.Register(FlankId, doctrineName, new DoctrineDefinition
            {
                Name      = doctrineName,
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            var ingressSys     = new DoctrineIngressSystem(registry);
            var arbitrationSys = new ChannelArbitrationSystem();

            var e = world.CreateEntity();
            // Setup: channel and doctrine both at version 1 (valid/matching).
            world.AddComponent(e, new DoctrineState { InstanceId = 1 });
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction       = 1,
                DoctrineInstanceId = 1, // matches DoctrineState.InstanceId — not yet stale
            });
            world.AddComponent(e, new BrainBlackboard());

            // Step 1: assign new doctrine → InstanceId becomes 2.
            world.Bus.PublishManaged(new AssignDoctrineEvent
            {
                Entity       = e,
                DoctrineName = doctrineName,
                JsonParams   = "",
            });
            world.Bus.SwapBuffers();
            ingressSys.Execute(world, 0.016f);

            uint newInstanceId = world.GetComponent<DoctrineState>(e).InstanceId;
            Assert.True(newInstanceId > 1); // ingress incremented it

            // Step 2: arbitration sees DoctrineInstanceId(1) != InstanceId(2) → clears channel.
            arbitrationSys.Execute(world, 0.016f);

            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(0, channel.ActiveAction);          // preemption chain complete
            Assert.Equal(1u, channel.DoctrineInstanceId);   // selective-clear preserves DoctrineInstanceId (only ActiveAction zeroed)

            world.Dispose();
        }

        // ── Test 5 (DEBT-008 / DEBT-035) ────────────────────────────────────────
        /// <summary>
        /// DEBT-035 fix verification: when <see cref="DoctrineDefinition.ParseParams"/> throws,
        /// <see cref="DoctrineIngressSystem"/> must not propagate the exception AND must leave
        /// the entity entirely on its previous doctrine (no partial transition).
        /// </summary>
        [Fact]
        public void DoctrineIngress_DoesNotThrow_WhenParseParamsFails()
        {
            var (world, sys, registry) = CreateFixture();

            const string doctrineName = "BrokenDoctrine";
            const int BrokenId = 7001;
            registry.Register(BrokenId, doctrineName, new DoctrineDefinition
            {
                Name      = doctrineName,
                BrainTier = BehaviorConstants.BrainTierBTree,
                // ParseParams delegate that always throws.
                ParseParams = static (string json, byte* mem) =>
                    throw new InvalidOperationException("Simulated parse failure"),
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { InstanceId = 5 });
            world.AddComponent(e, new BrainBlackboard());

            world.Bus.PublishManaged(new AssignDoctrineEvent
            {
                Entity       = e,
                DoctrineName = doctrineName,
                JsonParams   = "bad_json",
            });
            world.Bus.SwapBuffers();

            // Must not throw.
            var exception = Record.Exception(() => sys.Execute(world, 0.016f));
            Assert.Null(exception);

            // DEBT-035 fix: ParseParams now runs BEFORE DoctrineState is written.
            // A parse failure aborts the transition — InstanceId must remain at 5.
            var state = world.GetComponent<DoctrineState>(e);
            Assert.Equal(5u, state.InstanceId);

            world.Dispose();
        }

        // ── Test 6 (DEBT-035 required test) ──────────────────────────────────────
        /// <summary>
        /// Required by BATCH-14 Corrective-0: verifies that both
        /// <see cref="DoctrineState.ActiveDoctrineHash"/> AND
        /// <see cref="DoctrineState.InstanceId"/> are unchanged when
        /// <see cref="DoctrineDefinition.ParseParams"/> fails.
        /// </summary>
        [Fact]
        public void DoctrineIngress_DoctrineStateUnchanged_WhenParseParamsFails()
        {
            var (world, sys, registry) = CreateFixture();

            const int OldId = 9000;
            const int NewId = 9001;
            const string oldDoctrineName = "OldDoctrine";
            const string newDoctrineName = "NewDoctrine";

            registry.Register(OldId, oldDoctrineName, new DoctrineDefinition
            {
                Name      = oldDoctrineName,
                BrainTier = BehaviorConstants.BrainTierBTree,
            });
            registry.Register(NewId, newDoctrineName, new DoctrineDefinition
            {
                Name      = newDoctrineName,
                BrainTier = BehaviorConstants.BrainTierBTree,
                // ParseParams delegate that always throws.
                ParseParams = static (string json, byte* mem) =>
                    throw new InvalidOperationException("Test-induced parse failure"),
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState
            {
                ActiveDoctrineHash = OldId,
                InstanceId         = 0
            });
            world.AddComponent(e, new BrainBlackboard());

            // Attempt to switch to NewDoctrine — ParseParams will throw.
            world.Bus.PublishManaged(new AssignDoctrineEvent
            {
                Entity       = e,
                DoctrineName = newDoctrineName,
                JsonParams   = "{}",
            });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            var state = world.GetComponent<DoctrineState>(e);
            // ActiveDoctrineHash must still point to OldId — NOT switched to NewId.
            Assert.Equal(OldId, state.ActiveDoctrineHash);
            // InstanceId must NOT have been bumped.
            Assert.Equal(0u, state.InstanceId);

            world.Dispose();
        }

        // ── Task-2 Tests: ClearDoctrineEvent ─────────────────────────────────────

        [Fact]
        public void ClearDoctrineEvent_SetsDoctrineToNone()
        {
            var (world, sys, _) = CreateFixture();

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState
            {
                ActiveDoctrineHash = 2001,
                InstanceId         = 5,
                BrainTier          = BehaviorConstants.BrainTierBTree,
            });
            world.AddComponent(e, new BrainBTreeState
            {
                State = new Fbt.BehaviorTreeState { RunningNodeIndex = 3 }
            });

            world.Bus.Publish(new ClearDoctrineEvent { Entity = e });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            var doctrine = world.GetComponent<DoctrineState>(e);
            Assert.Equal(DoctrineIds.None, doctrine.ActiveDoctrineHash);  // cleared
            Assert.Equal(6u,              doctrine.InstanceId);           // incremented
            Assert.Equal(0,               doctrine.BrainTier);            // reset to none

            var btState = world.GetComponent<BrainBTreeState>(e);
            Assert.Equal(0, btState.State.RunningNodeIndex);              // execution pointer reset

            world.Dispose();
        }

        [Fact]
        public void ClearDoctrineEvent_NoDoctrineState_IsIgnored()
        {
            var (world, sys, _) = CreateFixture();

            // Entity without DoctrineState — event must be silently skipped.
            var e = world.CreateEntity();
            // Intentionally no DoctrineState component added.

            world.Bus.Publish(new ClearDoctrineEvent { Entity = e });
            world.Bus.SwapBuffers();

            var exception = Record.Exception(() => sys.Execute(world, 0.016f));
            Assert.Null(exception);

            world.Dispose();
        }

        [Fact]
        public void ClearDoctrineEvent_DoesNotAffectOtherEntities()
        {
            var (world, sys, _) = CreateFixture();

            var entityA = world.CreateEntity();
            world.AddComponent(entityA, new DoctrineState { ActiveDoctrineHash = 1001, InstanceId = 1 });

            var entityB = world.CreateEntity();
            world.AddComponent(entityB, new DoctrineState { ActiveDoctrineHash = 1001, InstanceId = 1 });

            // Only clear entity A.
            world.Bus.Publish(new ClearDoctrineEvent { Entity = entityA });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            var docA = world.GetComponent<DoctrineState>(entityA);
            var docB = world.GetComponent<DoctrineState>(entityB);

            Assert.Equal(DoctrineIds.None, docA.ActiveDoctrineHash); // cleared
            Assert.Equal(1001,            docB.ActiveDoctrineHash);  // untouched

            world.Dispose();
        }

        [Fact]
        public void ClearVsAssign_AreIndependent()
        {
            // In the same frame: AssignDoctrineEvent for A and ClearDoctrineEvent for B.
            // After one Run, A has the assigned doctrine; B has DoctrineIds.None.
            var (world, sys, registry) = CreateFixture();

            const string doctrineName = "Patrol";
            const int PatrolId = 5001;
            registry.Register(PatrolId, doctrineName, new DoctrineDefinition
            {
                Name      = doctrineName,
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            var entityA = world.CreateEntity();
            world.AddComponent(entityA, new DoctrineState { ActiveDoctrineHash = 0, InstanceId = 0 });
            world.AddComponent(entityA, new BrainBlackboard());

            var entityB = world.CreateEntity();
            world.AddComponent(entityB, new DoctrineState { ActiveDoctrineHash = PatrolId, InstanceId = 1 });

            world.Bus.PublishManaged(new AssignDoctrineEvent { Entity = entityA, DoctrineName = doctrineName, JsonParams = "" });
            world.Bus.Publish(new ClearDoctrineEvent  { Entity = entityB });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            var docA = world.GetComponent<DoctrineState>(entityA);
            var docB = world.GetComponent<DoctrineState>(entityB);

            Assert.Equal(PatrolId,        docA.ActiveDoctrineHash); // assigned
            Assert.Equal(DoctrineIds.None, docB.ActiveDoctrineHash); // cleared

            world.Dispose();
        }
    }
}
