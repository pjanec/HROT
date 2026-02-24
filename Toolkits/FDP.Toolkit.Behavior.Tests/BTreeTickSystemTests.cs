using System;
using Fdp.Kernel;
using Fbt;
using Fbt.Runtime;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Systems;
using Xunit;

namespace FDP.Toolkit.Behavior.Tests
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
            registry.Register(doctrineName, new DoctrineDefinition
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
                ActiveDoctrineHash = doctrineName.GetHashCode(),
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
            registry.Register(doctrineName, new DoctrineDefinition
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
                ActiveDoctrineHash = doctrineName.GetHashCode(),
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
    }
}
