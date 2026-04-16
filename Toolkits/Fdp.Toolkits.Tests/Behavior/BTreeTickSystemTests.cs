using System;
using Fdp.Core;
using Fbt;
using Fbt.Runtime;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    public class BTreeTickSystemTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Build a one-node BTree whose single Action node invokes <paramref name="actionName"/>.
        /// </summary>
        private static BehaviorTreeBlob BuildSingleActionBlob(string actionName)
        {
            return new BehaviorTreeBlob
            {
                TreeName    = "Test",
                Nodes       = new[] { new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 } },
                MethodNames = new[] { actionName },
                FloatParams = Array.Empty<float>(),
                IntParams   = Array.Empty<int>(),
            };
        }

        // ── Test 1 ───────────────────────────────────────────────────────────────

        [Fact]
        public void BTreeTick_DoesNotThrow_WhenBlobNotRegistered()
        {
            // Arrange — entity with a doctrine hash that is NOT in the registry.
            var world    = TestWorldFactory.Create();
            var registry = new DoctrineRegistry();
            var sys      = new BTreeTickSystem(registry);
            sys.Create(world);

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState
            {
                ActiveDoctrineHash = 999,                          // not registered
                BrainTier          = BehaviorConstants.BrainTierBTree,
            });
            world.AddComponent(e, new BrainBTreeState());
            world.AddComponent(e, new BrainBlackboard());

            var stateBefore = world.GetComponent<BrainBTreeState>(e);

            // Act + Assert — must not throw, state must be unchanged.
            sys.Run();

            var stateAfter = world.GetComponent<BrainBTreeState>(e);
            Assert.Equal(stateBefore.State.RunningNodeIndex, stateAfter.State.RunningNodeIndex);

            sys.Dispose();
            world.Dispose();
        }

        // ── Test 2 ───────────────────────────────────────────────────────────────

        [Fact]
        public void BTreeTick_DoesNotTick_WhenBrainTierIsNotBTree()
        {
            // Arrange — entity with HSM tier; register a tree that counts invocations.
            var world    = TestWorldFactory.Create();
            var registry = new DoctrineRegistry();

            int tickCount = 0;
            var blob      = BuildSingleActionBlob("CountTick");
            var actionReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
            actionReg.Register("CountTick", (ref BrainBlackboard _, ref BehaviorTreeState _, ref BTreeContext _, int _) =>
            {
                tickCount++;
                return NodeStatus.Success;
            });
            var interpreter = new Interpreter<BrainBlackboard, BTreeContext>(blob, actionReg);

            const string doctrineName = "CountTick";
            const int   doctrineId   = 9001;
            registry.Register(doctrineId, doctrineName, new DoctrineDefinition
            {
                Name             = doctrineName,
                BrainTier        = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = interpreter,
            });

            var sys = new BTreeTickSystem(registry);
            sys.Create(world);

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState
            {
                ActiveDoctrineHash = doctrineId,
                BrainTier          = BehaviorConstants.BrainTierHsm, // WRONG tier
            });
            world.AddComponent(e, new BrainBTreeState());
            world.AddComponent(e, new BrainBlackboard());

            // Act.
            sys.Run();

            // Assert — tree was never ticked because BrainTier != BrainTierBTree.
            Assert.Equal(0, tickCount);

            sys.Dispose();
            world.Dispose();
        }

        // ── Test 3 ───────────────────────────────────────────────────────────────

        [Fact]
        public void BTreeTick_WritesActionToChannel_ForRegisteredTree()
        {
            // Arrange — minimal one-node tree that writes LocomotionChannel.
            var world    = TestWorldFactory.Create();
            var registry = new DoctrineRegistry();

            var blob      = BuildSingleActionBlob("SetLocomotion");
            var actionReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
            actionReg.Register("SetLocomotion",
                (ref BrainBlackboard _, ref BehaviorTreeState _, ref BTreeContext ctx, int _) =>
                {
                    ref var ch = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);
                    ch.ActiveAction    = 1;
                    ch.ActionInstanceId = 1;
                    return NodeStatus.Success;
                });
            var interpreter = new Interpreter<BrainBlackboard, BTreeContext>(blob, actionReg);

            const string doctrineName = "SetLocomotion";
            const int   doctrineId   = 9002;
            registry.Register(doctrineId, doctrineName, new DoctrineDefinition
            {
                Name             = doctrineName,
                BrainTier        = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = interpreter,
            });

            var sys = new BTreeTickSystem(registry);
            sys.Create(world);

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState
            {
                ActiveDoctrineHash = doctrineId,
                BrainTier          = BehaviorConstants.BrainTierBTree,
            });
            world.AddComponent(e, new BrainBTreeState());
            world.AddComponent(e, new BrainBlackboard());
            world.AddComponent(e, new LocomotionChannel()); // BTree node writes here

            // Act.
            sys.Run();

            // Assert — BTree node wrote the expected action into the channel.
            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(1, channel.ActiveAction);      // BTree wrote the action
            Assert.Equal(1u, channel.ActionInstanceId); // instance was stamped

            sys.Dispose();
            world.Dispose();
        }

        // ── Task-1 Tests: DoctrineFinishedEvent ──────────────────────────────────

        // Helper: build a one-node tree that always returns the given status.
        private static (DoctrineRegistry registry, BTreeTickSystem sys) BuildTerminalSystem(
            EntityRepository world, int doctrineId, string doctrineName, NodeStatus status)
        {
            var registry  = new DoctrineRegistry();
            var blob      = BuildSingleActionBlob(doctrineName);
            var actionReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
            actionReg.Register(doctrineName,
                (ref BrainBlackboard _, ref BehaviorTreeState _, ref BTreeContext _, int _) => status);
            var interpreter = new Interpreter<BrainBlackboard, BTreeContext>(blob, actionReg);
            registry.Register(doctrineId, doctrineName, new DoctrineDefinition
            {
                Name             = doctrineName,
                BrainTier        = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = interpreter,
            });
            var sys = new BTreeTickSystem(registry);
            sys.Create(world);
            return (registry, sys);
        }

        [Fact]
        public void DoctrineRoot_Success_PublishesDoctrineFinishedEvent()
        {
            var world = TestWorldFactory.Create();
            const int doctrineId = 8001;
            var (_, sys) = BuildTerminalSystem(world, doctrineId, "SuccessDoc", NodeStatus.Success);

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { ActiveDoctrineHash = doctrineId, BrainTier = BehaviorConstants.BrainTierBTree });
            world.AddComponent(e, new BrainBTreeState());
            world.AddComponent(e, new BrainBlackboard());

            sys.Run();

            // Events published by the system land in the write buffer; swap to read them.
            world.Bus.SwapBuffers();
            var events = world.Bus.Read<DoctrineFinishedEvent>();

            int count = 0;
            DoctrineFinishedEvent? found = null;
            foreach (var evt in events)
            {
                if (evt.Entity.Index == e.Index) { found = evt; count++; }
            }

            Assert.Equal(1, count);
            Assert.NotNull(found);
            Assert.Equal(NodeStatus.Success, found!.Value.Result);

            sys.Dispose();
            world.Dispose();
        }

        [Fact]
        public void DoctrineRoot_Failure_PublishesDoctrineFinishedEvent()
        {
            var world = TestWorldFactory.Create();
            const int doctrineId = 8002;
            var (_, sys) = BuildTerminalSystem(world, doctrineId, "FailureDoc", NodeStatus.Failure);

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { ActiveDoctrineHash = doctrineId, BrainTier = BehaviorConstants.BrainTierBTree });
            world.AddComponent(e, new BrainBTreeState());
            world.AddComponent(e, new BrainBlackboard());

            sys.Run();

            world.Bus.SwapBuffers();
            var events = world.Bus.Read<DoctrineFinishedEvent>();

            DoctrineFinishedEvent? found = null;
            foreach (var evt in events)
                if (evt.Entity.Index == e.Index) found = evt;

            Assert.NotNull(found);
            Assert.Equal(NodeStatus.Failure, found!.Value.Result);

            sys.Dispose();
            world.Dispose();
        }

        [Fact]
        public void DoctrineRoot_Running_DoesNotPublishEvent()
        {
            var world = TestWorldFactory.Create();
            const int doctrineId = 8003;
            var (_, sys) = BuildTerminalSystem(world, doctrineId, "RunningDoc", NodeStatus.Running);

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { ActiveDoctrineHash = doctrineId, BrainTier = BehaviorConstants.BrainTierBTree });
            world.AddComponent(e, new BrainBTreeState());
            world.AddComponent(e, new BrainBlackboard());

            sys.Run();

            world.Bus.SwapBuffers();
            var events = world.Bus.Read<DoctrineFinishedEvent>();

            bool anyForEntity = false;
            foreach (var evt in events)
                if (evt.Entity.Index == e.Index) anyForEntity = true;

            Assert.False(anyForEntity);

            sys.Dispose();
            world.Dispose();
        }

        [Fact]
        public void DoctrineRoot_Success_PublishedOnlyOnce()
        {
            // BTree always returns Success. Event must be published on frame 1 but NOT frame 2
            // (same InstanceId — suppressed by _publishedTerminalForInstanceId guard).
            var world = TestWorldFactory.Create();
            const int doctrineId = 8004;
            var (_, sys) = BuildTerminalSystem(world, doctrineId, "AlwaysSuccessDoc", NodeStatus.Success);

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { ActiveDoctrineHash = doctrineId, BrainTier = BehaviorConstants.BrainTierBTree });
            world.AddComponent(e, new BrainBTreeState());
            world.AddComponent(e, new BrainBlackboard());

            // Frame 1: expect event.
            sys.Run();
            world.Bus.SwapBuffers();
            int frame1Count = 0;
            foreach (var evt in world.Bus.Read<DoctrineFinishedEvent>())
                if (evt.Entity.Index == e.Index) frame1Count++;

            // Frame 2: same InstanceId — must NOT re-publish.
            sys.Run();
            world.Bus.SwapBuffers();
            int frame2Count = 0;
            foreach (var evt in world.Bus.Read<DoctrineFinishedEvent>())
                if (evt.Entity.Index == e.Index) frame2Count++;

            Assert.Equal(1, frame1Count);
            Assert.Equal(0, frame2Count);

            sys.Dispose();
            world.Dispose();
        }

        [Fact]
        public void DoctrineFinished_NotPublishedByLocomotionDispatcher()
        {
            // Running LocomotionDispatcherSystem alone (no BTreeTickSystem) must NOT
            // produce a DoctrineFinishedEvent even when the executor sets channel status.
            var world = TestWorldFactory.Create();

            var dispatcher = new LocomotionDispatcherSystem();
            var spy        = new WritingSpyExecutor<LocomotionChannel>(); // writes Status = Running
            dispatcher.RegisterExecutor(1, spy);
            dispatcher.Create(world);

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { ActiveDoctrineHash = 1, BrainTier = BehaviorConstants.BrainTierBTree, InstanceId = 1 });
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction         = 1,
                ActionInstanceId     = 1,
                DoctrineInstanceId   = 1,
                DispatchedInstanceId = 0, // triggers OnEnter + Execute
            });
            world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });

            dispatcher.Run();

            // No DoctrineFinishedEvent should appear on the bus.
            world.Bus.SwapBuffers();
            bool anyEvent = false;
            foreach (var evt in world.Bus.Read<DoctrineFinishedEvent>())
                if (evt.Entity.Index != 0) anyEvent = true;

            Assert.False(anyEvent);

            dispatcher.Dispose();
            world.Dispose();
        }

        // ── CORRECTIVE-1 Test: memory leak pruning ───────────────────────────────

        [Fact]
        public void DestroyedEntity_PrunedFromTerminalTrackingDictionary()
        {
            // Arrange: terminal doctrine so the deduplication dictionary gets an entry.
            var world = TestWorldFactory.Create();
            const int doctrineId = 8010;
            var (_, sys) = BuildTerminalSystem(world, doctrineId, "PruneDoc", NodeStatus.Success);

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState
            {
                ActiveDoctrineHash = doctrineId,
                BrainTier          = BehaviorConstants.BrainTierBTree,
            });
            world.AddComponent(e, new BrainBTreeState());
            world.AddComponent(e, new BrainBlackboard());

            // Frame 1: entity processed, entry added to deduplication dictionary.
            sys.Run();
            Assert.Equal(1, sys.TrackedEntityCount);

            // Destroy the entity — no longer in query.
            world.DestroyEntity(e);

            // Frame 2: entity absent from query → entry pruned from dictionary.
            sys.Run();
            Assert.Equal(0, sys.TrackedEntityCount);

            sys.Dispose();
            world.Dispose();
        }
    }
}
