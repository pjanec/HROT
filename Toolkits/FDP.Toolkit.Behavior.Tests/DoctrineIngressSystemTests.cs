using System;
using System.Runtime.CompilerServices;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Events;
using FDP.Toolkit.Behavior.Systems;
using Xunit;

namespace FDP.Toolkit.Behavior.Tests
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
            sys.Create(world);
            return (world, sys, registry);
        }

        // ── Test 1 ───────────────────────────────────────────────────────────────

        [Fact]
        public void DoctrineIngress_ParsesFleeBlackboard_FromJson()
        {
            var (world, sys, registry) = CreateFixture();

            // Register "FleeToSafety" doctrine with a parse delegate that writes a float.
            const string doctrineName = "FleeToSafety";
            registry.Register(doctrineName, new DoctrineDefinition
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

            sys.Run();

            // Verify: BrainBlackboard.Memory[0..3] == 50.0f.
            ref var blackboard = ref world.GetComponentRW<BrainBlackboard>(e);
            var bbPtr = (BrainBlackboard*)Unsafe.AsPointer(ref blackboard);
            var fb    = *(FleeBlackboard*)bbPtr->Memory;
            Assert.Equal(50.0f, fb.SafeDistance);

            sys.Dispose();
            world.Dispose();
        }

        // ── Test 2 ───────────────────────────────────────────────────────────────

        [Fact]
        public void DoctrineIngress_IncrementsInstanceId_MonotonicallyAcrossMultipleAssignments()
        {
            var (world, sys, registry) = CreateFixture();

            const string doctrineName = "Patrol";
            registry.Register(doctrineName, new DoctrineDefinition
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
            sys.Run();
            uint instanceId1 = world.GetComponent<DoctrineState>(e).InstanceId;

            // --- Assignment 2 ---
            world.Bus.PublishManaged(new AssignDoctrineEvent { Entity = e, DoctrineName = doctrineName, JsonParams = "" });
            world.Bus.SwapBuffers();
            sys.Run();
            uint instanceId2 = world.GetComponent<DoctrineState>(e).InstanceId;

            Assert.True(instanceId1 > 0);             // was incremented from 0
            Assert.True(instanceId2 > instanceId1);   // strictly increasing each assignment

            sys.Dispose();
            world.Dispose();
        }

        // ── Test 3 ───────────────────────────────────────────────────────────────

        [Fact]
        public void DoctrineIngress_ResetsBTreeState_OnNewDoctrine()
        {
            var (world, sys, registry) = CreateFixture();

            const string doctrineName = "Assault";
            registry.Register(doctrineName, new DoctrineDefinition
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
            sys.Run();

            var btState = world.GetComponent<BrainBTreeState>(e);
            Assert.Equal(0, btState.State.RunningNodeIndex); // reset to start

            sys.Dispose();
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
            registry.Register(doctrineName, new DoctrineDefinition
            {
                Name      = doctrineName,
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            var ingressSys     = new DoctrineIngressSystem(registry);
            var arbitrationSys = new ChannelArbitrationSystem();
            ingressSys.Create(world);
            arbitrationSys.Create(world);

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
            ingressSys.Run();

            uint newInstanceId = world.GetComponent<DoctrineState>(e).InstanceId;
            Assert.True(newInstanceId > 1); // ingress incremented it

            // Step 2: arbitration sees DoctrineInstanceId(1) != InstanceId(2) → clears channel.
            arbitrationSys.Run();

            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(0, channel.ActiveAction);          // preemption chain complete
            Assert.Equal(0u, channel.DoctrineInstanceId);   // full channel reset to default, not a selective clear

            ingressSys.Dispose();
            arbitrationSys.Dispose();
            world.Dispose();
        }
    }
}
