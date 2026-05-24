using System;
using Xunit;
using Fbt.Runtime;
using Fbt.Tests.TestFixtures;
using Fbt;

namespace Fbt.Tests.Unit
{
    public class ActionRegistryTests
    {
        [Fact]
        public void Register_StoresAction()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            NodeLogicDelegate<TestBlackboard, MockContext> action = (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) => NodeStatus.Success;
            
            registry.Register("TestAction", action);
            
            Assert.True(registry.TryGetAction("TestAction", out var retrieved));
            Assert.Same(action, retrieved);
        }

        [Fact]
        public void TryGetAction_ReturnsFalse_ForMissingAction()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            Assert.False(registry.TryGetAction("Missing", out _));
        }

        [Fact]
        public void Register_OverwritesExisting()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            NodeLogicDelegate<TestBlackboard, MockContext> action1 = (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) => NodeStatus.Success;
            NodeLogicDelegate<TestBlackboard, MockContext> action2 = (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) => NodeStatus.Failure;
            
            registry.Register("Action", action1);
            registry.Register("Action", action2);
            
            registry.TryGetAction("Action", out var retrieved);
            Assert.Same(action2, retrieved);
        }

        // ---- TASK-EQL-002 deactivator contract tests (T1-T5) ----

        [Fact]
        public void T1_RegisterDeactivator_TryGetDeactivator_ReturnsSameDelegateInstance()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            NodeDeactivatorDelegate<TestBlackboard, MockContext> deleg =
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) => { };

            registry.RegisterDeactivator("Foo", deleg);

            bool found = registry.TryGetDeactivator("Foo", out var retrieved);
            Assert.True(found);
            Assert.Same(deleg, retrieved);
        }

        [Fact]
        public void T2_TryGetDeactivator_MissingKey_ReturnsFalse()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            Assert.False(registry.TryGetDeactivator("Missing", out _));
        }

        [Fact]
        public void T3_RegisterDeactivator_NullKey_ThrowsArgumentNullException()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            NodeDeactivatorDelegate<TestBlackboard, MockContext> deleg =
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) => { };

            var ex = Assert.Throws<ArgumentNullException>(
                () => registry.RegisterDeactivator(null!, deleg));
            Assert.Equal("key", ex.ParamName);
        }

        [Fact]
        public void T4_RegisterDeactivator_NullDelegate_ThrowsArgumentNullException()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            Assert.Throws<ArgumentNullException>(
                () => registry.RegisterDeactivator("key", null!));
        }

        [Fact]
        public void T5_RegisterDeactivator_SameKeyTwice_SecondRegistrationWins()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            NodeDeactivatorDelegate<TestBlackboard, MockContext> deleg1 =
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) => { };
            NodeDeactivatorDelegate<TestBlackboard, MockContext> deleg2 =
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) => { };

            registry.RegisterDeactivator("Key", deleg1);
            registry.RegisterDeactivator("Key", deleg2);

            registry.TryGetDeactivator("Key", out var retrieved);
            Assert.Same(deleg2, retrieved);
            Assert.NotSame(deleg1, retrieved);
        }
    }
}
